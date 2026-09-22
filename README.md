# HanakaServer

HanakaServer là backend monolith của hệ thống Hanaka Sport/Pickleball. Một ứng dụng phục vụ đồng thời REST API cho mobile, public web, admin web, cổng trọng tài, cổng đánh giá trình và các kết nối WebSocket realtime.

Tài liệu này hướng dẫn chạy dự án. Ngữ cảnh kỹ thuật và trạng thái chức năng hiện tại được duy trì tại [`HanakaServer/context.md`](HanakaServer/context.md).

> Cập nhật gần nhất: 22/09/2026. Các tài liệu task, handoff và test report cũ đã được hợp nhất vào `context.md` rồi xóa để tránh nhiều nguồn thông tin mâu thuẫn.

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

- .NET SDK và ASP.NET Core Runtime 10.x.
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

Điều phối sân/trạng thái lịch đấu: chạy `database/updates/20260922_add_match_coordination.sql` trước khi chạy server mới, kể cả khi tắt `Coordination__Enabled`. Sau khi cập nhật server và app, admin vào **Vòng đấu → Phân công điều phối** để cấp quyền theo giải. Quy tắc, API và hướng dẫn tắt tính năng nằm trong phần “Điều phối sân và trạng thái lịch đấu” của `HanakaServer/context.md`.

Trang riêng cho người điều phối: **`/CoordinatorPortal/Login`** → **`/CoordinatorPortal/Matches`**. Đăng nhập bằng email/số điện thoại và mật khẩu của tài khoản được phân công, chọn giải, lọc trận theo sân/vòng/trạng thái, sửa sân và bật/tắt chuẩn bị. Khi trọng tài chấm điểm, điều phối trận đó tự khóa. Thay đổi dùng chung dữ liệu và cập nhật trực tiếp với lịch đấu app. Portal dùng cookie riêng, không cần thêm SQL ngoài migration điều phối ở trên.

DbContext có mapping database-first lớn trong `PickleballDbContext.cs`; bracket template và relay được tách sang partial file.

Không tự động chạy script lên database cấu hình thật. Đọc từng script, sao lưu và thử trên database riêng trước. Với relay, thứ tự bắt buộc là:

1. Schema bracket template hiện hữu.
2. `database/updates/20260906_add_relay_foundation.sql`.
3. `database/updates/20260906_relay_informational_timer_configurable_target.sql`.
4. `database/updates/20260906_add_relay_bracket_snapshots.sql`.
5. `database/updates/20260906_add_bracket_template_participant_mode.sql`.
6. `database/updates/20260907_optimize_public_relay_registrations.sql`.
7. `database/updates/20260908_remove_relay_lineup_lock_and_add_match_snapshots.sql`.
8. `database/updates/20260910_add_relay_reserve_members.sql`.
9. `database/updates/20260912_add_relay_three_part_scores.sql`.
10. Chỉ bật `Relay__AdminPreviewEnabled` sau khi các script trên đã chạy thành công.

Bản chấm tiếp sức ba phần cần script ngày 12/09 trước khi chạy server mới, kể cả khi tắt cờ relay, vì model lịch sử điểm có thêm cột. Script giữ nguyên tỷ số cũ; trận đã có điểm yêu cầu trọng tài phân bổ đúng tổng vào ba phần trước khi chấm tiếp. Không tự suy đoán điểm từng phần từ tổng cũ.

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

Kiểm thử ổn định trang điều phối trên Windows/SQL LocalDB (không dùng database cấu hình thật):

```powershell
$env:HANAKA_RELAY_SQL_TESTS = '1'
$env:HANAKA_AUTH_SQL_TESTS = '1'
dotnet test HanakaServer.sln --no-restore
node --test --test-concurrency=1 HanakaServer.Tests/JavaScript/*.test.js
```

Test tải HTTP/SQL/WebSocket dùng 10 điều phối, 10 trọng tài, 200 người xem và ba mức 100/500/2.000 trận. Chạy riêng, mặc định đủ 120 phút; log và kết quả ở `artifacts/coordination-stability`. Đặt `HANAKA_COORDINATION_SOAK_MINUTES=1` chỉ để kiểm tra nhanh công cụ, không thay thế lần chạy 2 giờ.

```powershell
$env:HANAKA_COORDINATION_SOAK = '1'
$env:HANAKA_COORDINATION_SOAK_MINUTES = '120'
dotnet test HanakaServer.Tests/HanakaServer.Tests.csproj --filter FullyQualifiedName~CoordinatorSoakTests
```

Trong terminal thứ hai, sau khi `artifacts/coordination-stability/active-host.json` có host đang chạy, kiểm tra Edge với Razor/API/SQL/WebSocket thật. Các test này chỉ nhận host thử nghiệm loopback và dùng những trận dành riêng ngoài 20 trận của công cụ tạo tải. Lần chạy vòng đời tiêu thụ một trận dự phòng nên tối đa tám lần trên cùng host.

```powershell
$env:HANAKA_COORDINATION_E2E = '1'
node --test --test-concurrency=1 HanakaServer.Tests/JavaScript/coordinator-live.test.js HanakaServer.Tests/JavaScript/coordinator-large-schedule.test.js
$env:HANAKA_COORDINATION_BROWSER_SOAK = '1'
$env:HANAKA_COORDINATION_BROWSER_SOAK_MINUTES = '90'
node --test HanakaServer.Tests/JavaScript/coordinator-browser-soak.test.js
```

Chạy kiểm tra trình duyệt dài khi test HTTP còn đủ thời gian hoạt động. Đo retained heap có gọi GC chủ động mỗi phút để phân biệt đối tượng còn được giữ với rác chưa được thu gom. Đây là kiểm chứng cục bộ; bundle Android/iOS và test module app không thay thế UAT trên điện thoại thật. Xóa các biến opt-in khỏi terminal khi muốn chạy lại bộ hồi quy nhanh.

## Quy tắc duy trì tài liệu

- `README.md` chỉ chứa hướng dẫn vào dự án và vận hành cơ bản.
- `HanakaServer/context.md` là nguồn ngữ cảnh kỹ thuật duy nhất.
- Không tạo lại các file `Task_*.md`, handoff hoặc test report rời rạc ở thư mục gốc.
- Khi chức năng hoặc schema thay đổi, cập nhật `context.md` trong cùng thay đổi mã nguồn.
