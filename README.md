# HanakaServer

HanakaServer là backend monolith của hệ thống Hanaka Sport/Pickleball. Một ứng dụng phục vụ đồng thời REST API cho mobile, public web, admin web, cổng trọng tài, cổng đánh giá trình và các kết nối WebSocket realtime.

Tài liệu này hướng dẫn chạy dự án. Ngữ cảnh kỹ thuật và trạng thái chức năng hiện tại được duy trì tại [`HanakaServer/context.md`](HanakaServer/context.md).

> Cập nhật gần nhất: 06/09/2026. Các tài liệu task, handoff và test report cũ đã được hợp nhất vào `context.md` rồi xóa để tránh nhiều nguồn thông tin mâu thuẫn.

## Công nghệ

- ASP.NET Core MVC/API, target `net10.0`.
- Razor Views cho public web, admin, referee portal và rating portal.
- Entity Framework Core 10 + SQL Server.
- Cookie authentication cho web; JWT Bearer cho mobile/API và WebSocket đã xác thực.
- Hai WebSocket hub tự quản lý kết nối trong tiến trình.
- MailKit/MimeKit cho email và OTP; HTTP client cho nhà cung cấp OTP và SePay.
- xUnit, EF Core InMemory và SQL LocalDB cho test.
- Docker multi-stage dùng .NET 10.

## Solution

```text
HanakaServer.sln
├── HanakaServer/          Ứng dụng web/API chính
└── HanakaServer.Tests/    Test server, bracket, realtime và relay
```

Các thư mục quan trọng:

- `HanakaServer/Program.cs`: dependency injection, auth, CORS, route và WebSocket.
- `HanakaServer/Controllers`: API controller và MVC controller.
- `HanakaServer/Service`: auth, OTP, payment, bracket, standings, realtime và relay.
- `HanakaServer/Data`: EF Core DbContext và mapping theo partial class.
- `HanakaServer/Models`: entity ánh xạ SQL Server.
- `HanakaServer/Dtos`: request/response contract.
- `HanakaServer/Views`: Razor views.
- `HanakaServer/wwwroot`: static assets và thư mục upload.
- `database/migrations`, `database/updates`: script SQL được quản lý thủ công.
- `artifacts`: log và đầu ra kiểm chứng cục bộ; không phải mã nguồn.

## Cấu hình an toàn

Ứng dụng đọc cấu hình từ `appsettings*.json`, user secrets và environment variables theo cơ chế chuẩn của ASP.NET Core.

Các key quan trọng:

- `ConnectionStrings:PickleballDb`
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:Key`, `Jwt:AccessTokenMinutes`
- `PublicBaseUrl`
- `Otp`, `AbenlaOtp`
- `Email`, `Support`, `Smtp`
- `SePay`
- `Relay:AdminPreviewEnabled`

Không thêm secret thật vào source. Với environment variables, dùng dạng:

```powershell
$env:ConnectionStrings__PickleballDb = '<connection-string>'
$env:Jwt__Key = '<long-random-secret>'
$env:Smtp__Pass = '<smtp-secret>'
```

File cấu hình hiện tại từng chứa credential plaintext. Phải xoay các credential đó và chuyển chúng sang secret store trước khi triển khai.

## Chạy local

Yêu cầu:

- .NET SDK và ASP.NET Core Runtime 9.x.
- SQL Server với schema phù hợp.
- Cấu hình kết nối và secret hợp lệ.

```powershell
dotnet restore HanakaServer.sln
dotnet run --project .\HanakaServer\HanakaServer.csproj
```

Launch profile mặc định:

- HTTP: `http://localhost:5062`
- HTTPS: `https://localhost:7156`

## Route và xác thực

- `/`: public Pickleball web.
- Route MVC mặc định: `/{controller=Home}/{action=Login}/{id?}`.
- `/RefereePortal/{action=Login}/{id?}`: cổng trọng tài.
- `/ws`: WebSocket yêu cầu JWT.
- `/ws-public`: WebSocket công khai.

Cookie scheme là mặc định cho MVC. JWT Bearer được chỉ định trên các API mobile/client. Các policy chính:

- `AdminOnly`
- `RefereeOnly`
- `RatingAssessorOnly`

## Nhóm chức năng

- Auth, OTP, quên mật khẩu, profile và rating.
- CLB, chat CLB, chat trực tiếp, notification và moderation.
- Giải đấu, đăng ký đơn/đôi, tìm partner và lời mời ghép đôi.
- Thanh toán đăng ký giải qua SePay.
- Vòng đấu, bảng đấu, trận, lịch, trọng tài, điểm và standings.
- Thư viện bracket template, draft/publish, generator, seeding, apply/reset/reconcile.
- Public web/API cho giải, sân, HLV, trọng tài, banner, video và bracket.
- Thể thức đồng đội tiếp sức có luồng tạo riêng trong trang quản trị giải, cấu hình đội 4/6/8 người, điểm đích, roster và bracket theo feature flag.

## Database

DbContext có mapping database-first lớn trong `PickleballDbContext.cs`; bracket template và relay được tách sang partial file.

Không tự động chạy script lên database cấu hình thật. Đọc từng script, sao lưu và thử trên database riêng trước. Với relay, thứ tự bắt buộc là:

1. Schema bracket template hiện hữu.
2. `database/updates/20260906_add_relay_foundation.sql`.
3. `database/updates/20260906_relay_informational_timer_configurable_target.sql`.
4. `database/updates/20260906_add_relay_bracket_snapshots.sql`.
5. `database/updates/20260906_add_bracket_template_participant_mode.sql`.
6. `database/updates/20260907_optimize_public_relay_registrations.sql`.
7. `database/updates/20260908_remove_relay_lineup_lock_and_add_match_snapshots.sql`.
8. Chỉ bật `Relay__AdminPreviewEnabled` sau khi các script trên đã chạy thành công.

Không bật `RelayTournamentSettings.IsEnabled` trực tiếp bằng SQL. Dùng thao tác kích hoạt trên trang chuẩn bị relay để hệ thống kiểm tra cấu hình và các đội hình đầy đủ.

## Kiểm thử

Chạy test thông thường:

```powershell
dotnet test HanakaServer.sln --no-restore
```

SQL integration tests dùng database LocalDB riêng và cần bật rõ ràng:

```powershell
$env:HANAKA_RELAY_SQL_TESTS = '1'
dotnet test HanakaServer.sln --no-restore
```

Dự án đã được nâng lên .NET 10 và kiểm chứng trực tiếp bằng runtime .NET 10. Log lịch sử trong `artifacts` có thể vẫn chứa đường dẫn build `net9.0`; đây không phải cấu hình chạy hiện tại.

## Quy tắc duy trì tài liệu

- `README.md` chỉ chứa hướng dẫn vào dự án và vận hành cơ bản.
- `HanakaServer/context.md` là nguồn ngữ cảnh kỹ thuật duy nhất.
- Không tạo lại các file `Task_*.md`, handoff hoặc test report rời rạc ở thư mục gốc.
- Khi chức năng hoặc schema thay đổi, cập nhật `context.md` trong cùng thay đổi mã nguồn.
