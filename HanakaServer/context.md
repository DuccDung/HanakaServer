# HanakaServer — Ngữ cảnh kỹ thuật chuẩn

> Đối chiếu tổng thể: 06/09/2026; phần điều phối cập nhật: 22/09/2026.
> Phạm vi: solution `HanakaServer.sln` và các script SQL nằm trong repository này.  
> Đây là nguồn ngữ cảnh kỹ thuật duy nhất của dự án. Nếu tài liệu mâu thuẫn với code, SQL hoặc test hiện tại thì code/SQL/test là nguồn xác nhận cuối cùng và file này phải được cập nhật ngay.

## 1. Trạng thái repository

- Solution có hai project: ứng dụng `HanakaServer` và test `HanakaServer.Tests`.
- Ứng dụng target `net10.0`, nullable và implicit usings đang bật.
- Kiến trúc là modular monolith nhưng boundary chưa đồng đều: phần cũ chứa nhiều nghiệp vụ trong controller; bracket, payment và relay đã có service riêng rõ hơn.
- Data layer dùng EF Core 10 + SQL Server. Schema được duy trì bằng mapping database-first và script SQL thủ công, không có chuỗi EF Migration chuẩn với model snapshot.
- Có nhiều thư mục build tạm, `bin`, `obj` và `artifacts`; không dùng chúng làm nguồn đọc code.
- Git hiện không có trong `PATH` của môi trường đã khảo sát, vì vậy chưa thể xác nhận danh sách thay đổi chưa commit bằng `git status`.

## 2. Luồng ứng dụng

```text
Mobile / Browser / Admin / Referee
                │
                ├── REST hoặc Razor MVC
                │       ↓
                │   Controllers
                │       ↓
                │   Services + EF DbContext
                │       ↓
                │     SQL Server
                │
                ├── /ws         → WebSocketHandler → RealtimeHub
                └── /ws-public  → PublicWebSocketHandler → PublicRealtimeHub
```

Entry point là `Program.cs`:

- `AddControllersWithViews()` phục vụ cả API và Razor MVC.
- `PickleballDbContext` lấy connection string `PickleballDb`.
- CORS policy `AllowAll` hiện cho phép mọi origin/header/method.
- Cookie là authentication scheme mặc định.
- JWT Bearer dùng cho API client/mobile và `/ws`.
- `TournamentPairRequestExpiryService` chạy nền để xử lý lời mời ghép đôi hết hạn.
- `RelayLineupService` chỉ có một constructor công khai nhận `PickleballDbContext` và `RelayMatchLineupSnapshotService`. Test tạo trực tiếp phải truyền service snapshot; không thêm overload nhận `TimeProvider` vì cả hai dependency đều có trong DI và sẽ gây lỗi constructor không rõ ràng tại `builder.Build()`.
- Hai realtime hub là singleton trong tiến trình; trạng thái subscription không được chia sẻ giữa nhiều instance server.

Route cấp ứng dụng:

- `/` → `PickleballWebController.Index`.
- `/{controller=Home}/{action=Login}/{id?}` → MVC mặc định.
- `/RefereePortal/{action=Login}/{id?}` → cổng trọng tài.
- `/CoordinatorPortal/Login`, `/CoordinatorPortal/Matches` → trang đăng nhập và điều phối sân riêng.
- `/ws-public` → WebSocket công khai.
- `/ws` → WebSocket yêu cầu JWT hợp lệ và claim định danh user.

### Cancellation và client disconnect

- `RequestCancellationMiddleware` là boundary chung cho MVC, API và endpoint WebSocket. `OperationCanceledException` chỉ được coi là request bị bỏ khi `HttpContext.RequestAborted` đã được kích hoạt.
- Request bị client đóng không được đổi thành lỗi nghiệp vụ/500 và không ghi log Warning/Error; middleware đặt status 499 nếu response chưa bắt đầu. Cancellation không bắt nguồn từ `RequestAborted` vẫn được truyền lên để không che provider timeout hoặc lỗi ứng dụng.
- Middleware nhận diện thêm `SqlException` trực tiếp hoặc được EF bọc trong `InvalidOperationException`: request phải đã hủy và danh sách SQL errors phải có mã `0` với thông báo `Operation cancelled by user.`. Chỉ chấp nhận lỗi đi kèm đã xác minh: mã `3980` hoặc mã `0`/class `11`/state `0` với thông báo severe-error chuẩn của SqlClient. `3980` đơn lẻ, severe-error đơn lẻ, timeout, deadlock, lỗi schema và mọi SQL error khác vẫn được truyền lên. Log cancellation chỉ ghi Method/Path/TraceId/SqlErrorNumber ở mức Debug.
- Cancellation token vẫn phải được truyền xuống EF/HTTP I/O để dừng công việc không còn cần thiết. Các `catch (Exception)` có token của caller phải cho caller cancellation đi tiếp.
- Rollback sau cancellation dùng token cleanup độc lập, có timeout 5 giây, thay vì dùng lại token đã bị hủy.
- Sau khi thao tác chấm điểm đã commit, bracket propagation và thông báo người thắng dùng token do server sở hữu với timeout 15 giây; việc client rời trang không được làm mất bước giữ nhất quán sau commit.
- WebSocket disconnect/request cancellation là đóng kết nối bình thường; lỗi protocol/runtime khác vẫn được log. Timeout gửi realtime được tách khỏi request cancellation.
- Màn hình setup bracket hủy lần tải cũ bằng `AbortController`, bỏ qua `AbortError` và dùng sequence guard để response cũ không ghi đè trạng thái mới.
- Public web tải `pickleball-web/js/web-session.js` trước các script trang. `HanakaWebSession.read()` chỉ xác nhận phiên từ JSON hợp lệ hoặc HTTP 401; lỗi mạng/500/JSON không hợp lệ là trạng thái chưa kiểm tra được, còn AbortError/499 được giữ là cancellation. Không cache phiên hoặc dùng chung request giữa các caller.
- Trang chủ, tài khoản và đổi mật khẩu giữ danh tính đã xác nhận khi kiểm tra phiên lỗi; lần tải đầu chưa xác định sẽ hiển thị trạng thái trung lập. Account giữ dữ liệu đang nhập và sequence guard; chỉ xóa danh tính/chuyển đăng nhập khi server xác nhận không còn phiên. Danh sách đăng ký giải vẫn hiển thị dữ liệu công khai khi kiểm tra phiên lỗi, kèm thông báo tải lại và chặn thao tác cần xác nhận phiên.
- Kiểm thử cancellation SQL chạy với `HANAKA_AUTH_SQL_TESTS=1` trên Windows/LocalDB (`AuthRequestCancellationSqlTests`). Test tạo/xóa database `HanakaAuthCancellationTests_<guid>`, kiểm tra lỗi EF bọc, hủy SQL đang chạy, HTTP disconnect qua Kestrel, lỗi schema và các request tiếp theo. Không sử dụng connection string thật của ứng dụng. JavaScript: `node --test HanakaServer.Tests/JavaScript/*.test.js`, gồm kiểm thử phiên và tương tác account/change-password trên Edge headless.

### Phản hồi loading cho API trên web

- `wwwroot/js/api-loading.js` và `wwwroot/css/api-loading.css` là boundary giao diện dùng chung cho hoạt động API trên public web, admin, referee portal, rating portal và các trang web độc lập.
- Mọi lời gọi `fetch` được theo dõi tự động; các trang dùng Axios cài interceptor chung. Bộ quản lý dùng token và reference count nên chỉ đóng overlay sau khi toàn bộ request foreground đồng thời đã kết thúc, kể cả khi request lỗi hoặc bị hủy.
- Bộ loading dùng chung chọn spinner tại nút khi nhận biết nút kích hoạt, hoặc overlay toàn màn hình khi không có nút. API có thể chọn `global`, `section`, `button` hoặc `silent` qua tùy chọn `hanakaLoading`; chấm điểm dùng `silent`, báo trạng thái lưu ngay trong form và khóa ghi đồng thời, không phủ loading lên bảng điểm.
- Payment polling, refresh do realtime và đồng bộ chat nền phải dùng `silent` để không làm overlay nhấp nháy hoặc chặn thao tác. WebSocket là kết nối dài hạn nên không được đưa vào reference count của API loading.
- Overlay có độ trễ ngắn để tránh chớp với request rất nhanh, có thời gian hiển thị tối thiểu, thông báo khi request chậm và tự dọn trạng thái khi `pagehide`/BFCache restore.
- Web người dùng (11/09/2026) nạp `pickleball-web/js/web-activity.js` và `css/web-activity.css` từ đầu `_PickleballWebLayout` và trang sơ đồ độc lập, trước stylesheet/font bên ngoài. Lớp `HanakaWebActivity` dùng lại `HanakaApiLoading`, áp dụng riêng cho API cùng origin dưới `/api/`; theo dõi đến khi đọc JSON/text/blob xong và giao diện có cơ hội vẽ, giữ nguyên Response gốc. Các request chỉ đọc status/không có body vẫn kết thúc; timeout 30 giây hủy request phía client và giải phóng loading, không tự gửi lại thao tác ghi. Các helper không nuốt lỗi JSON thành thành công hoặc nuốt lỗi hủy/timeout.
- Trang mới có một loading khởi tạo chung bao phủ các request ban đầu và các vùng đang tải. Ảnh bìa giải được tải eager/ưu tiên cao, chờ tối đa 2,5 giây để có kích thước gốc trước khi bỏ loading; không chờ mọi avatar/ảnh ngoài màn hình. Loading khởi tạo và điều hướng có giới hạn 20 giây, thông báo phục hồi khi chờ lâu. Không thay đổi tỷ lệ hiển thị ảnh bìa.
- Điều hướng web bao phủ link nội bộ `/` và `/PickleballWeb/*`, form điều hướng thật và các lệnh chuyển trang trong auth/account/pages/native-pages/club-detail qua `navigateWeb`. Session storage chỉ giữ pathname đích và thời gian để trang mới tiếp tục loading; bỏ qua hash cùng trang, download, tab mới, phím bổ trợ, link ngoài và sự kiện bị hủy. `pagehide`/`pageshow` dọn request, timer, token và khóa tương tác, hỗ trợ BFCache; không sửa History API thành SPA. API `setRegionBusy` quản lý loading theo vùng từ state có sẵn; cập nhật nền vẫn `silent`.
- Form đăng nhập, tài khoản, danh sách/thành viên, các màn native và CLB dùng overlay trong vùng phù hợp; các nút đã có loading riêng tiếp tục dùng `button`. Bấm lặp bị chặn ngay trong thời gian xử lý; overlay toàn trang tạm đặt `inert` lên nội dung và khôi phục focus không cuộn. Không dùng blur trên web người dùng, hỗ trợ giảm chuyển động; panel loading vùng bám trong vùng nhìn khi danh sách dài. Tìm kiếm đang chạy sẽ nhận truy vấn mới nhất và bỏ kết quả cũ; một số danh sách native giữ DOM khi HTML không đổi. Lỗi refresh realtime lịch/sơ đồ/đăng ký giữ nội dung, thử đọc lại có giới hạn và thông báo khi cần người dùng kiểm tra, không reload cả trang liên tục.
- Kiểm chứng: build Release `net10.0` thành công; bộ JavaScript 29 test đạt, test mới `web-activity.test.js` có 34 kiểm tra Edge với HTTP thật trên server test, gồm body trả chậm, request đồng thời, hủy/lỗi/timeout, gửi trùng, form Login thật với API giả lập, điều hướng/handoff/Back/Forward/BFCache, ảnh bìa và viewport 320–1440 px. Đã xem 17 màn public bằng proxy chỉ cho GET lấy HTML/API từ localhost:7156 và gắn assets/layout mới trong bản xem thử; không có lỗi JS hoặc vùng loading bị kẹt. Kết quả/ảnh trong `artifacts/web-activity-20260911/`. Ứng dụng đang chạy ở 7156 dùng Razor đã biên dịch cũ; cần chạy lại ứng dụng để nạp layout mới. Không tạo tài khoản, đăng ký đội hoặc thanh toán trên dữ liệu thật.

## 3. Xác thực và phân quyền

### Cookie

- Dùng cho admin web, referee portal và rating portal.
- Cookie tên `Hanaka.Auth`, `HttpOnly`, `SameSite=Lax`, thời hạn 8 giờ và sliding expiration.
- Trang điều phối dùng scheme `CoordinatorPortal`, cookie `Hanaka.Coordinator` riêng (8 giờ, sliding, HttpOnly, SameSite=Lax), không dùng phiên admin/trọng tài. Tài khoản active và role `COORDINATOR` được kiểm tra lại từ DB mỗi request; các lệnh login/logout/điều phối dùng antiforgery token. API `/api/coordinator-portal` trả 401/403 JSON khi hết phiên/không đủ quyền.
- API dùng cookie được xử lý để trả 401/403 JSON thay vì redirect HTML.
- `HomeController` vẫn có credential admin hard-code. Không xem đây là cơ chế đăng nhập production.

### JWT

- Dùng cho auth/mobile API, user API, tournament self-registration, chat trực tiếp và WebSocket `/ws`.
- Token được đọc từ `Authorization: Bearer`, query `access_token` cho WebSocket, hoặc cookie access-token của public web.
- Cấu hình hiện tại có thời gian sống access token rất dài; cần rút ngắn và bổ sung chiến lược refresh/revoke trước production.

### Role và policy

- `AdminOnly`: role `Admin`.
- `RefereeOnly`: role `REFEREE` hoặc `Admin`.
- `RatingAssessorOnly`: role rating assessor hoặc `Admin`.
- Một số controller dùng policy, một số dùng chuỗi role trực tiếp. Khi thay đổi role phải tìm cả hai kiểu khai báo.

## 4. Data model và schema

### Nhóm entity chính

| Miền | Entity tiêu biểu |
| --- | --- |
| User/Auth | `User`, `Role`, `UserRole`, `UserOtp`, `UserRatingHistory`, `UserAchievement`, `UserBlock`, `UserNotification` |
| Club/Chat | `Club`, `ClubMember`, `ClubMessage`, `DirectChatRoom`, `DirectChatMessage`, `DirectChatRoomParticipant`, `ModerationReport` |
| Tournament | `Tournament`, `TournamentRegistration`, `TournamentPairRequest`, `TournamentRound`, `TournamentRoundMap`, `TournamentRoundGroup`, `TournamentGroupMatch`, `TournamentPrize`, `TournamentMatchScoreHistory` |
| Payment | `TournamentRegistrationPayment`, `TournamentSepayWebhook`, `SepaySetting` |
| Bracket library | `BracketTemplate`, `BracketTemplateVersion`, `BracketTemplateRound`, `BracketTemplateGroup`, `BracketTemplateMatch`, `BracketTemplateMatchSlot`, `TournamentBracketApplication`, `TournamentBracketSeedAssignment` |
| Relay | `RelayTournamentSettings`, `RelayTeam`, `RelayTeamMember`, `RelayMatchState`, `RelayLeg`, `RelayMatchCommand`, `RelayBracketSeedSnapshot` |
| Directory/Public | `Coach`, `Referee`, `Court`, `CourtImage`, `Banner`, `Link`, `Exchange` |

### Quy ước dữ liệu

- DB thường lưu đường dẫn upload tương đối; response ghép với `PublicBaseUrl` khi cần URL tuyệt đối.
- `UserRatingHistories` là nguồn chuẩn của rating. `Users.RatingSingle` và `Users.RatingDouble` là cache/legacy và vẫn được đồng bộ để tương thích.
- `Coach` và `Referee` có shadow profile liên kết với user qua external/user id; cập nhật profile/rating có thể phải đồng bộ các bảng này.
- `TournamentRegistration` vừa đại diện người/cặp đăng ký truyền thống, vừa là vị trí đội chính khi tích hợp relay. Không được dùng tên đội relay làm tên VĐV giả trong các trường legacy.
- Bracket application giữ seed snapshot để lịch sử không bị thay đổi khi registration hoặc roster về sau thay đổi.

### Script SQL

- `database/migrations` và `database/updates` đều là script thủ công; tên thư mục không có nghĩa EF sẽ tự chạy.
- Script phải có kế hoạch backup, kiểm tra repeatability và chạy trên bản sao database trước.
- Các script relay/bracket bổ sung là additive và đã được chạy thành công lên SQL Server đang cấu hình ngày 06/09/2026; vẫn phải chạy theo thứ tự này ở môi trường mới:
  1. `20260906_add_relay_foundation.sql` tạo sáu bảng nền, constraint, index và trigger.
  2. `20260906_relay_informational_timer_configurable_target.sql` bỏ ràng buộc deadline, giữ timer 600 giây ở vai trò hiển thị và cập nhật trigger khóa luật.
  3. `20260906_add_relay_bracket_snapshots.sql` tạo bảng snapshot thứ bảy và phụ thuộc schema bracket đã có.
  4. `20260906_add_bracket_template_participant_mode.sql` tách đối tượng tham gia `STANDARD`/`RELAY_TEAM` khỏi topology bracket và phân loại draft `TP_08` nếu chưa từng được dùng.
  5. `20260907_optimize_public_relay_registrations.sql` bổ sung index đọc danh sách đăng ký công khai.
  6. `20260908_remove_relay_lineup_lock_and_add_match_snapshots.sql` bỏ trigger khóa roster và tạo snapshot đội hình bất biến theo từng trận.

## 5. Bản đồ module

### Auth và user

Điểm vào chính:

- `AuthsController`: register, OTP, resend, forgot/reset password, login/logout cho client.
- `WebAuthApiController`: cùng auth flow nhưng quản lý cookie/token cho public web.
- `AppAuthService`: nghiệp vụ auth dùng chung.
- `UsersController`: profile, avatar, password, rating, lịch sử, achievement và xóa/anonymize tài khoản.
- `UserRatingService`: đọc/ghi rating chuẩn và đồng bộ cache/shadow profile.

Khi sửa auth, phải kiểm tra đồng thời mobile JWT, web cookie, portal cookie và WebSocket token extraction.

### Club, direct chat và moderation

Điểm vào chính:

- `ClubsController`: CRUD/participation CLB, member approval, cover và chat CLB.
- `DirectChatsController`: phòng chat 1-1, message, recall/edit/delete và trạng thái đọc.
- `ModerationController`, `AdminModerationController`: report, block, ẩn nội dung, eject/reinstate.
- `RealtimeHub`, `WebSocketHandler`: subscription và push theo user, CLB hoặc direct room.

REST/DB là source of truth; WebSocket chỉ phát sự kiện sau thao tác lưu. Khi sửa chat phải giữ đồng bộ giữa persistence, block/privacy, moderation và realtime.

### Tournament registration

`TournamentRegistrationUserController` cung cấp luồng JWT cho VĐV:

- Xem trạng thái đăng ký của mình.
- Tìm partner.
- Đăng ký giải đơn.
- Tạo đăng ký chờ ghép đôi.
- Tạo, nhận, từ chối hoặc hủy lời mời ghép đôi.
- Xem danh sách và chi tiết lời mời.

Admin dùng `AdminRegistrationsController` để quản lý registration. Mọi thay đổi registration có thể ảnh hưởng capacity, payment, bracket seed, match participants, notification và relay lineup; không sửa độc lập từng bảng.

Tra cứu thành viên tiếp sức trên admin (11/09/2026):

- Form thêm/sửa đội, gồm thành viên chính và dự bị, nhận “User ID hoặc số điện thoại”. Enter hoặc nút “Kiểm tra” tra cứu tài khoản; số điện thoại giữ nguyên khi hiển thị, dữ liệu đăng ký gửi User ID đã xác định. Đội trưởng vẫn là trường ID tùy chọn và phải thuộc đội hình chính; khách tiếp tục đăng ký bằng họ tên.
- `GET /api/admin/users/lookup?query=...` yêu cầu quyền Admin, tìm chính xác tài khoản đang hoạt động theo ID hoặc số điện thoại. Bỏ khoảng trắng/dấu phân cách khi so sánh, hỗ trợ số Việt Nam tương đương `0...` và `+84...`/`84...`. Trả `items` gồm UserId, FullName, AvatarUrl, Phone và trình mới nhất từ lịch sử, fallback về cache. API `find/{id}` hiện hữu giữ nguyên contract cho các màn khác. Không thay schema hoặc dữ liệu số điện thoại.
- Nếu số điện thoại khớp nhiều tài khoản hoặc đồng thời khớp một User ID khác, giao diện yêu cầu chọn đúng người. Không có kết quả thì báo tại ô nhập; trên 20 kết quả trả lỗi yêu cầu tìm cụ thể bằng ID. Nội dung nhập và tài khoản đã chọn được lưu tách biệt; sửa nội dung xóa lựa chọn cũ. Trùng người được kiểm tra bằng User ID, kể cả nhập ID ở vị trí chính và số điện thoại ở vị trí dự bị.
- Form sửa khởi tạo tài khoản đã lưu. Khi bấm đăng ký/lưu, các ô chưa xác nhận được tra cứu trước; không gửi nếu tra cứu lỗi hoặc còn kết quả chưa chọn. Chọn tài khoản khóa ô tên khách và dùng hồ sơ phía server. Xóa thông tin tra cứu mở lại ô tên khách; tên của tài khoản cũ không tự trở thành khách mới.
- Loading tra cứu dùng `HanakaApiLoading` ở chế độ button, trạng thái “Đang tìm tài khoản…” nằm tại từng VĐV, Axios dùng `silent` để tránh hai bộ loading. Nút giữ kích thước, chặn bấm lặp và các vị trí khác vẫn thao tác được. Có timeout 15 giây, AbortController và kiểm tra request/DOM hiện tại; đổi đầu vào, đóng modal hoặc pagehide hủy tra cứu, dọn loading và bỏ qua phản hồi cũ.
- Luồng lưu thêm/sửa dùng chung quản lý submit, chống gửi lặp, hiển thị “Đang kiểm tra tài khoản…” rồi “Đang lưu đăng ký…”. Cho đóng modal trong lúc tra cứu; khi đã gửi lưu thì khóa các trường và chặn đóng modal đến khi có phản hồi. Lỗi lưu giữ nội dung và phục hồi trạng thái trường/nút. Payload của đăng ký đơn/đôi giữ nguyên.
- Kiểm chứng: build Release `net10.0` thành công; 45 test .NET liên quan lookup/đăng ký admin/dự bị đạt, 3 SQL test opt-in được bỏ qua ở lượt thường. Test SQL mới `AdminUserLookupTests.Lookup_executes_phone_normalization_and_rating_projection_on_real_sql_server` được chạy riêng với `HANAKA_AUTH_SQL_TESTS=1`, đạt trên database LocalDB tạm và đã dọn database; kiểm tra dịch LINQ/chuẩn hóa số, đọc trình, lookup ID ngắn và cancellation. Toàn bộ 27 test JavaScript đạt; test trình duyệt mới dùng Razor/JS/CSS/loading thật và API giả lập đạt 48 kiểm tra, gồm form sửa, dự bị, nhập khách, luồng đơn, mạng lỗi/chậm, kết quả cũ, đóng/mở modal và kích thước 320–1280 px. Chưa thao tác tạo/sửa đội trên database thật hoặc triển khai server.

Thanh tìm kiếm trên `Views/Registrations/Index.cshtml` (10/09/2026): bỏ `w-100` khỏi ô nhập và giữ input-group không xuống dòng để kính lúp nối liền ô tìm kiếm. Ô nhập và nút cao 40 px, header có khoảng cách rõ với tiêu đề; dưới 576 px, ô nhập nằm trên và các nút chia đều hàng dưới. Từ 576 px, toolbar giữ cùng hàng và ô nhập co theo phần diện tích còn lại. Các nút là phần tử trực tiếp của toolbar nên nút “Xóa lọc” ẩn không để lại khung rỗng. Có viền focus cho thao tác bàn phím. Đã kiểm tra bằng Edge với HTML/CSS thực và dữ liệu hiển thị giả lập: 32 trường hợp ở chiều rộng 320–1920 px, bao gồm hiện/ẩn nút xóa và diện tích bị giới hạn bởi sidebar/container; JavaScript tìm kiếm giữ nguyên. Build Release thành công, 0 lỗi và 17 cảnh báo hiện có.

### Payment SePay

Điểm vào chính:

- `TournamentRegistrationPaymentsController`: tạo checkout, app-webview checkout, đọc trạng thái và nhận webhook.
- `TournamentRegistrationPaymentService`: điều phối trạng thái payment/registration.
- `SepaySettingsProvider`: đọc cấu hình runtime.
- `SepayGatewayClient`: gọi nhà cung cấp.
- `PublicRealtimeHub`: phát `tournament.payment.status.updated` cho subscriber transaction code.

Webhook phải idempotent, không tin số tiền/trạng thái từ client và không log secret/API key.

### Tournament runtime

- `AdminTournamentRoundsController`: round map và lịch hàng loạt.
- `AdminTournamentRoundGroupsController`: group trong round.
- `AdminTournamentGroupMatchesController`: trận, điểm và trọng tài.
- `TournamentClientController`: lịch, chi tiết trận và rule cho client.
- `PublicTournamentsController`: danh sách/chi tiết/registration công khai.
- `TournamentStandingsService`: tính xếp hạng bảng.
- `TournamentBracketPropagationService`: đưa winner/loser/group rank sang các slot phụ thuộc và reconcile.
- `RefereeMatchesApiController`: danh sách và đường chấm điểm chung cho đơn, đôi và đội tiếp sức.
- Khối thông tin đầu trang chấm điểm trong `Views/RefereePortal/Matches.cshtml` dùng hai hàng gọn: tên giải và trạng thái cùng hàng, giờ/sân ở hàng dưới. Trạng thái không ngắt dòng; tên giải và sân dài được xuống dòng. Đã đối chiếu bằng Edge ở chiều rộng 320–1024 px và chế độ ngang; với nội dung mẫu “hanaka”, khối cao 70 px thay vì khoảng 113 px trên màn hình 390 px.
- Tương tác cộng/trừ điểm (10/09/2026): không tự focus ô số, không hiện overlay/toast cho mỗi điểm; trạng thái lưu nằm trong form. Cả cộng/trừ và lưu thủ công đồng bộ điểm, nhãn trạng thái, công tắc kết thúc, đội thắng và lịch sử ngay từ phản hồi HTTP đã xác nhận, không phụ thuộc realtime. Các thay đổi kết quả được cập nhật tại chỗ, giữ DOM của form, tình huống đang chọn, vị trí cuộn và các khối thông tin đang mở; thay đổi quyền chấm, đội tham gia hoặc thông tin cấu trúc trận vẫn dựng lại chi tiết. Realtime dùng cùng bước đồng bộ nên sự kiện kết thúc hoặc đổi đội thắng không làm mất form và echo không nhân lịch sử.
- Phản hồi danh sách realtime được kiểm tra lại sau khi nhận: dữ liệu cũ hoặc đến trong lúc đang lưu/nhập nháp phải hoãn đồng bộ, không ghi đè điểm vừa nhập. Browser test `JavaScript/referee-score-interaction.test.js` chạy Razor/JS thật với API và realtime giả lập, bao gồm kết thúc lần đầu 1–0/0–1, tỷ số 5–2, nút trừ, lưu thủ công khi thiếu realtime, echo trước/sau/lặp, mạng chậm, hủy xác nhận, lỗi lưu, timeout đã commit, mở lại trận và đổi quyền sang chỉ xem.

Sửa tỷ số phải giữ: match result, winner, score history, propagation, public realtime và quyền trọng tài. Relay dùng hai registration đội cha trên `TournamentGroupMatch`; không tạo hoặc cập nhật state/cặp/lượt relay khi chấm điểm.

### Bracket template library

Thành phần chính:

- `BracketTemplateService`: CRUD template/version/graph, generate, publish và lifecycle.
- `BracketTemplateValidationService`: kiểm topology, source, cycle, BYE, group rank và terminal.
- `TournamentBracketApplicationService`: chọn registration, preview seed, apply, reset và lưu lịch sử.
- `AdminBracketTemplatesController`: API quản lý thư viện.
- `AdminTournamentBracketApplicationsController`: API áp dụng template cho giải.
- `TournamentBracketPropagationService`: runtime propagation sau khi có kết quả.

Nguyên tắc phải giữ:

- Template đã publish có version riêng; draft không được làm thay đổi bản đã dùng.
- Seed và random seed phải ổn định/replay được.
- Đội ảo chỉ phục vụ lấp seed và phải được ẩn ở API public phù hợp.
- Relay tái sử dụng nguyên graph template: một seed là một đội chính, không đưa cặp/lượt relay vào graph.

Giao diện thẻ trận trên `TournamentAdminBracket` (10/09/2026): header chia hai hàng, tên trận và ID ở trên, mã nhánh cùng giờ/sân ở dưới; tên nhánh đầy đủ nằm trong tooltip. Chữ dài được rút gọn bằng dấu ba chấm và có tooltip xem đầy đủ, tránh đè lên ID hoặc thông tin bên cạnh. Đã kiểm tra bằng Edge với renderer/CSS thật và dữ liệu giả lập: 48 trường hợp ở độ rộng thẻ 292–400 px, zoom 55–160%, bao gồm chữ dài, thiếu thông tin và trận có video; bố cục nhiều vòng và đường nối vẫn hoạt động. Bộ 17 test JavaScript hiện có đạt; chưa kiểm tra trang giải 37 trong phiên đăng nhập thực tế.

Lựa chọn đội theo thanh toán trên trang `TournamentBracketSetup`:

- Mặc định danh sách và sơ đồ gồm cả đội đã thanh toán và chưa thanh toán; vẫn yêu cầu đăng ký thành công, không chờ ghép cặp, đủ thành viên và không phải đội ảo. Áp dụng chung cho đơn, đôi và tiếp sức.
- `GET .../bracket/eligible-registrations` giữ `data` là mảng đội và bổ sung `registrationFeeAmount`. Service trả `TournamentBracketRegistrationListDto` gồm `Items` và lệ phí giải; giao diện phân biệt “Chưa thanh toán” với “Miễn phí”.
- Checkbox “Loại đội chưa thanh toán” mặc định tắt. `GET .../bracket/templates` nhận query `excludeUnpaidTeams`; request preview/apply nhận `ExcludeUnpaidTeams`, mặc định `false`. Chỉ lọc `Paid = true` khi tùy chọn bật và lệ phí giải lớn hơn 0. Không thay đổi đăng ký hoặc nghĩa vụ thanh toán của đội bị loại.
- Giao diện giữ đủ danh sách để đối chiếu, đánh dấu đội bị loại và hiển thị tổng đội/đã trả/chưa trả/đội đưa vào sơ đồ. Thay đổi checkbox tải lại template và danh sách, xóa preview cùng xác nhận cũ. Preview thêm đội ảo và apply dùng lại đúng tùy chọn của preview.
- Preview trả `ExcludedUnpaidRegistrationCount` riêng với tổng số đăng ký bị loại. Tùy chọn lọc được đưa vào checksum; khi apply, danh sách và trạng thái thanh toán được kiểm tra lại trong transaction cho cả giải tiêu chuẩn và tiếp sức. Dữ liệu thay đổi làm preview không còn khớp thì cần xem trước lại.
- Tùy chọn chỉ phục vụ lần tạo sơ đồ; không thêm cột database. Sơ đồ đã tạo giữ nguyên seed snapshot, không tự bổ sung đội thanh toán sau. Các đội ngoài snapshot được gọi là “đội chưa nằm trong sơ đồ”, không mặc định coi là đăng ký mới.

Kiểm chứng ngày 09/09/2026: build Release `net10.0` thành công; 26/26 test workflow bracket đạt, gồm 13 trường hợp mới về thanh toán và 2 SQL integration test trên database LocalDB riêng. Test trình duyệt `JavaScript/bracket-payment-selection.test.js` chạy Razor markup và JavaScript thực với API giả lập, đạt 22 kiểm tra hành vi; 4/4 JavaScript contract test đội ảo đạt. Chưa chạy thao tác tạo sơ đồ trên giải 37 thật.

### Kiểm tra mẫu trước khi xuất bản và áp dụng (09/09/2026)

- `BracketTemplateValidationService.Validate` vẫn cho lưu bản nháp chưa gán hết vị trí, kèm cảnh báo có vòng/bảng/trận/bên đội. `ValidateForUse` nâng các lỗi thiếu vị trí, vượt sức chứa, thừa vị trí đầu vào hoặc thiếu số trong dải sức chứa thành lỗi chặn sử dụng.
- Vòng bảng được kiểm tra theo các số vị trí đội phân biệt, không đếm lặp một đội ở nhiều trận. Các nguồn thắng/thua/hạng bảng chờ kết quả vẫn hợp lệ; vị trí `SEED` thiếu số không được coi là một đội đang chờ kết quả.
- Kiểm tra chặt áp dụng khi kiểm tra trong editor, xuất bản, liệt kê mẫu có thể dùng, preview và apply, kể cả phiên bản đã xuất bản từ trước. Response lỗi có `issues` để UI hiển thị vị trí cần sửa. Draft chưa hoàn chỉnh vẫn lưu được.
- Sức chứa/tối thiểu của phiên bản đã xuất bản không được sửa qua API settings. Thay đổi cấu trúc phải tạo phiên bản nháp mới; đổi tên template vẫn được phép và không đổi graph/hash của phiên bản. Editor hiển thị, nạp và lưu cấu hình đội, cách xếp và BYE cho draft; phiên bản đã xuất bản khóa các trường này.
- Apply kiểm tra lại graph và checksum bên trong transaction trước khi tạo dữ liệu. `SetInitialSlot` yêu cầu mỗi đầu vào SEED được giải thành registration hoặc BYE; kiểm tra runtime bổ sung nguồn hợp lệ và registration không rỗng. Giữ nguyên ràng buộc SQL, không có migration schema mới.
- Trang setup khóa xác nhận/apply khi có lỗi hoặc còn đầu vào chưa gán, kể cả khi kết thúc loading. Đổi mẫu hoặc tạo preview mới xóa bản xem trước cũ; lỗi API liệt kê vị trí cần sửa. Nguồn vòng sau vẫn hiển thị “Thắng/Thua trận…” bình thường.
- Kiểm chứng source: build Release `net10.0` đạt; toàn bộ 201 test .NET đạt khi bật `HANAKA_RELAY_SQL_TESTS=1`, không bỏ qua. Có test SQL mô phỏng TP_08 lỗi, tạo phiên bản sửa, áp dụng với constraint nguồn thật và rollback sau lỗi ghi trận. Browser test setup đạt 35 kiểm tra, editor đạt 10 kiểm tra; các test JavaScript đội ảo, API loading và public realtime cũng đạt.
- Dữ liệu đã chuẩn bị: TP_08 giữ phiên bản cũ ID 9 và xuất bản phiên bản 2 ID 11, sức chứa 8, giữ tối thiểu 2 và cho phép BYE như cấu hình gốc, cấu trúc 3 vòng/7 trận với đủ vị trí 1–8. Preview API thật của giải 37 trả 8 đội, 0 BYE, 0 lỗi, cả 8 vị trí đầu đã gán registration. Chưa gọi apply để tạo trận vào giải 37. Bản sao cấu hình trước/sau và tóm tắt preview nằm tại `artifacts/bracket-readiness-tp08-*.json`.
- Các kết quả test trên xác nhận bản source/Release mới. Tiến trình development đang chạy cần được khởi động lại để nạp các guard và Razor mới; việc sửa mẫu TP_08 trong database đã có hiệu lực trên preview hiện tại.

### Điều phối sân và trạng thái lịch đấu — 22/09/2026

- Admin mở **Vòng đấu → Phân công điều phối** (`/TournamentCoordinators/Index?tournamentId=...`), tìm tài khoản theo ID/số điện thoại rồi cấp hoặc thu hồi phân công theo giải. Trang này có liên kết đến **trang điều phối riêng** `/CoordinatorPortal/Login`; đăng nhập email/số điện thoại và mật khẩu tài khoản đã được phân công, sau đó dùng `/CoordinatorPortal/Matches`. Quy tắc: chỉ sửa sân và bật/tắt chuẩn bị trước khi trọng tài bắt đầu chấm; chưa cho đổi sân lúc đang đánh. Điều phối trên card lịch đấu app vẫn dùng được.
- Portal hiển thị các giải đang được phân công, tìm mã trận/tên đội, lọc sân/vòng/trạng thái, thống kê bốn trạng thái và form sửa sân/gọi chuẩn bị. `GET /api/coordinator-portal/session` đọc lại danh sách phân công; `PUT /api/coordinator-portal/matches/{id}` gọi cùng `MatchCoordinationService` với app. Cookie riêng, antiforgery, phiên bản trận và kiểm tra quyền DB bảo vệ thao tác; không nhận sửa tỷ số/kết quả. Tài khoản chỉ tạo qua OTP cần thiết lập mật khẩu qua luồng tài khoản hiện có trước khi dùng đăng nhập portal.
- Portal dùng `public-realtime.js`, nghe sự kiện điều phối/chấm điểm/sơ đồ, tải lại khi reconnect/focus và mỗi 30 giây. Bản REST cũ không ghi đè snapshot có phiên bản mới hơn; đổi giải hủy lần tải cũ và đổi subscription. Hộp sửa giữ bản nháp khi lỗi/xung đột, yêu cầu tải lại dữ liệu khi phiên bản đổi, khóa ngay khi trọng tài bắt đầu hoặc quyền bị thu hồi. Không cần thêm migration ngoài script điều phối đã có.
- Role `COORDINATOR` và bảng `TournamentCoordinators` xác định quyền. `GET /api/coordination/tournaments/{id}/permissions` đọc quyền hiện tại từ DB theo claim `uid`; token mobile cũ không có role claim vẫn dùng được. `PUT /api/coordination/matches/{id}` cũng kiểm tra tài khoản active, role, phân công và giải chưa xóa trong transaction. Thu hồi quyền có hiệu lực với request tiếp theo; app kiểm tra lại khi vào lịch và định kỳ 30 giây khi màn được focus.
- Request điều phối gồm `expectedVersion`, `courtText` (trim, tối đa 100 ký tự) và `preparing`. Chuẩn bị yêu cầu đủ hai đội và sân không trống. Điều phối không ghi điểm, kết quả, trọng tài hoặc nguồn đội. `MatchCoordinationHistories` lưu người thao tác, thời gian và giá trị sân/trạng thái trước-sau trong cùng transaction.
- `TournamentGroupMatches.MatchStatus`: `NOT_STARTED` trắng, `PREPARING` vàng nhạt, `IN_PROGRESS` xanh dương nhạt, `COMPLETED` xanh lá. Điểm được trọng tài lưu thành công, kể cả 0–0, chuyển sang đang đánh. Sửa điểm về 0 giữ đang đánh; kết thúc theo `IsCompleted`; mở lại trận về đang đánh. Đổi/thu hồi đội ở sơ đồ xóa trạng thái chuẩn bị; BYE hiển thị kết thúc/miễn đấu.
- `PickleballDbContext.Coordination.cs` đồng bộ trạng thái và tăng `StateVersion` khi lưu trận, bao phủ đường ghi admin, referee và propagation. `StateVersion` là concurrency token. Điều phối khóa hàng trận như chấm điểm, đối chiếu phiên bản và trả 409 khi trận thay đổi; middleware chuyển xung đột EF trên trận thành 409. API lịch/chi tiết và sự kiện chấm điểm bổ sung trạng thái/phiên bản, giữ các field cũ.
- `tournament.match.coordination.updated` phát snapshot card sau commit cho subscriber giải/trận. App giữ cập nhật đến trước REST, so phiên bản cho cả REST/WebSocket, không ghi đè tỷ số/sân/trạng thái mới bằng phản hồi cũ; tải lại khi reconnect. Sân hiển thị từ `CourtText`, tên đội/tỷ số cùng hàng, nút hành động được xuống dòng. Form điều phối giữ bản nháp khi lỗi, chặn lưu lặp và yêu cầu đọc lại khi dữ liệu đã thay đổi.
- **Triển khai:** sao lưu và chạy `database/updates/20260922_add_match_coordination.sql` trên bản sao để đối chiếu dữ liệu trước; sau đó áp dụng ở môi trường đích **trước khi chạy server mới**. Script chạy lại an toàn, thêm cột/bảng/role, không tự cấp quyền; phân loại dữ liệu cũ theo kết thúc, điểm và lịch sử chấm còn mới hơn lần cập nhật trận. Rà soát riêng dữ liệu cũ 0–0 đã từng reset hoặc chỉnh bằng SQL. Schema mới vẫn bắt buộc khi tắt tính năng vì các query EF có đọc cột mới.
- Thứ tự phát hành: SQL → server → app mới → admin phân công tài khoản. `Coordination__Enabled=false` tắt quyền/thao tác điều phối; giữ schema và dữ liệu để có thể bật lại. App cũ vẫn đọc API cũ; app mới dùng trạng thái dự phòng khi server chưa có field và ẩn điều phối nếu API quyền chưa có. Không rollback bằng cách xóa cột khi server mới còn chạy.
- Kiểm chứng: test API HTTP với JWT thật (uid-only, anonymous, sai quyền, thu hồi quyền, 409, DTO public); xUnit vòng đời trạng thái; SQL LocalDB riêng kiểm tra migration lặp, dữ liệu cũ, cập nhật đồng thời và rollback; test Node thực thi component app với native/hooks/API giả lập; test Edge cho trang phân công. `CoordinatorPortalTests` chạy HTTP/Razor thật với cookie riêng, antiforgery, email/phone, thu hồi phân công, khóa tài khoản, chuyển sang trọng tài và logout. `JavaScript/coordinator-portal.test.js` kiểm tra thao tác thực trên Edge với API giả lập, lỗi mạng/409/realtime/REST cũ, lọc trận, chống HTML injection, thu hồi quyền và bố cục 320–1280px. Chưa UAT APK/IPA trên thiết bị và chưa áp dụng migration/deploy vào database/server thật trong thay đổi này.

#### Kiểm thử ổn định điều phối — 22/09/2026

- `CoordinatorStabilityHost` dựng Kestrel, Razor, cookie, API, WebSocket và SQL Server LocalDB riêng; tạo dữ liệu giả và không dùng connection string ứng dụng. `CoordinatorStabilityTests` kiểm tra chuỗi chuẩn bị → chấm 0–0 → kết thúc cho đơn/đôi/tiếp sức; cookie hết hạn, đổi tài khoản/CSRF, tắt feature; restart server; migration dữ liệu cũ chạy hai lần; hủy request đọc; 100 cặp thao tác điều phối/chấm điểm cạnh tranh; thu hồi quyền khi đang chờ khóa và rollback khi ghi audit lỗi.
- Portal dùng `coordinator-http.js` với thời hạn 20 giây cho cả phản hồi và nội dung JSON, kiểm tra cấu trúc dữ liệu, giải phóng loading khi mất mạng/treo. Nếu lệnh ghi có kết quả chưa xác định (mất phản hồi, timeout, 5xx hoặc dữ liệu trả về sai), giữ bản nháp, chặn lưu tiếp và yêu cầu người điều phối tải dữ liệu mới; không tự phát lại lệnh. Test live cố tình bỏ phản hồi sau khi API đã commit, xác nhận app vẫn nhận sự kiện và portal đối chiếu dữ liệu trước thao tác kế tiếp.
- Tải thử phát hiện SQL phải xin memory grant lớn để sắp xếp projection lịch chứa nhiều chuỗi, làm các request đọc đồng thời chờ `RESOURCE_SEMAPHORE`. `GetRoundsWithMatches` vẫn lấy đầy đủ lịch nhưng sắp xếp trong từng nhóm sau materialization, giữ thứ tự giờ tăng dần/null cuối rồi mã trận; request cancellation được truyền vào các query chính. Pilot 1 phút sau sửa: read p95 552 ms, write p95 160 ms, giao sự kiện p95 16 ms, không lỗi. Đây là pilot, không thay thế kết quả 2 giờ.
- Burst 20 cập nhật trên Edge với 2.000 trận phát hiện việc dựng lại toàn bộ card mỗi sự kiện mất p95 14.079 ms. Portal đã giữ DOM của card không đổi, chỉ dựng lại card thay đổi và dùng chung date formatter. Chạy lại với server thật đạt p95 106–139 ms. Test kiểm tra thêm thứ tự khi lọc, trận xuất hiện lại theo realtime, khóa form ngay khi chấm điểm và giữ card không đổi.
- Bộ hồi quy hiện tại: 314 test .NET đạt và một test soak đạt riêng; 44 test JavaScript đạt, hai bài Edge live và một bài browser soak đạt riêng; 23 test module app đạt; xuất Hermes bundle Android/iOS thành công. Các test opt-in bị bỏ qua trong bộ nhanh đã được chạy riêng và đạt. Bộ JavaScript chạy tuần tự để các profile Edge tạm không tranh tài nguyên trên Windows; helper chờ file cổng debug hết khóa trước khi kết nối. Lần chạy song song từng gặp lỗi khóa/xóa profile tạm, cần phân biệt lỗi hạ tầng test với nghiệp vụ.
- Kiểm tra trình duyệt mở liên tục 90 phút đã đạt (5.400,013 giây), qua các mức 100/500/2.000 trận. Retained JS heap sau GC lần lượt nằm trong khoảng 863–924 / 1.342–1.510 / 3.188–3.519 KiB; không vượt guard bộ nhớ hoặc DOM của từng mức dữ liệu. Có 90 mẫu đo, kiểm tra card/counter/lỗi tải mỗi phút, dùng polling/realtime thật; đây là phép đo retained heap có GC chủ động, không phải tổng RAM trình duyệt.
- Đợt HTTP/SQL/WebSocket 120 phút **đã đạt**, chạy thực tế 7.200,119 giây với 10 điều phối, 10 trọng tài và 200 kết nối theo dõi; mỗi mức 100/500/2.000 trận được theo dõi khoảng 40 phút. Có 473 chu kỳ, 9.460 lệnh ghi, 1.800 phép đo đọc lịch và 1.893.800 lượt giao sự kiện, không lỗi. p95/p99 ghi: 60/110 ms; đọc: 601/672 ms; giao sự kiện: 18/31 ms. Mỗi chu kỳ kiểm tra đủ phiên bản trên toàn bộ người xem; mỗi phút nối lại năm kết nối và khôi phục bằng REST.
- Bộ nhớ working set của tiến trình thử nghiệm tại 100/500/2.000 trận có median 312/348/468 MiB, đỉnh toàn đợt 688 MiB; không thấy tăng liên tục qua các mẫu của cùng một mức dữ liệu. Đây là bộ nhớ gồm cả Kestrel và các client mô phỏng trong cùng tiến trình, không phải số đo RAM riêng của server production. Số kết nối SQL sau khởi động giữ ở mức 21 trong các mẫu. Kết quả trên Windows/SQL LocalDB máy hiện tại không phải cam kết sức chứa production.
- Báo cáo tổng hợp: `artifacts/coordination-stability/summary.json`; bằng chứng gốc gồm `coordination-final.trx`, `soak-120-minutes.trx`, `soak-20260922-112747/result.json`, `browser-soak-2026-09-22T11-45-40-659Z.json`, `live-final.log`, `large-schedule-browser.json` và `mobile-export.log`. Hướng dẫn chạy lại trong README. Host tải đã dừng và fixture SQL đã được dọn qua lifecycle của test. Phần app live chạy mã `PublicRealtimeClient`/merge/trạng thái thật trong Node; chưa chạy APK/IPA trên thiết bị Android/iOS thật. Chưa triển khai hoặc áp dụng migration vào production.

| Task | Kết quả trong đợt kiểm thử |
| --- | --- |
| T01 — Dữ liệu/môi trường | Đạt: LocalDB riêng, dữ liệu 100/500/2.000 trận, tài khoản điều phối/trọng tài giả. |
| T02 — Hồi quy/build | Đạt: .NET, JavaScript, module app và xuất bundle Android/iOS. |
| T03 — Phân quyền | Đạt: role, phân công theo giải, hết phiên, đổi tài khoản, CSRF, thu hồi quyền, feature flag. |
| T04 — Sân/trạng thái | Đạt: vòng đời đơn/đôi/tiếp sức, chấm 0–0, khóa sửa khi đang đánh/kết thúc. |
| T05 — Ghi đồng thời | Đạt: 100 cặp điều phối/chấm điểm cạnh tranh, audit và rollback transaction. |
| T06 — Phục hồi | Đạt: timeout, mất phản hồi sau commit, dữ liệu cũ, reconnect và restart server. |
| T07 — Tích hợp | Đạt tự động: Razor/API/SQL/WebSocket thật và module realtime của app trong Node. |
| T08 — Giao diện/thiết bị | Đạt Edge 320/390/768/1280px; **UAT Android/iOS trên thiết bị thật chưa chạy**. |
| T09 — Tải/chạy dài | Đạt: server 120 phút và trình duyệt 90 phút, theo các số liệu ở trên. |
| T10 — Migration/tương thích | Đạt trên DB riêng: backfill, chạy lại script, tắt feature, fallback client; chưa triển khai production. |

### Public realtime

`/ws-public` hỗ trợ subscription theo tournament, match, video feed và transaction code.

Các event server chính:

- `tournament.match.score.updated`
- `tournament.match.coordination.updated`
- `tournament.bracket.updated`
- `tournament.payment.status.updated`

`PublicRealtimeHub` serialize send theo từng socket và loại socket lỗi. Vì hub lưu subscription trong memory, scale-out cần backplane/pub-sub và chiến lược phân phối subscription.

## 6. Đồng đội tiếp sức

### Chấm điểm ba đội con — 12/09/2026

- Luồng hiện tại dùng đúng **3 phần chấm độc lập** cho mỗi trận tiếp sức theo yêu cầu nghiệp vụ, không suy ra số bảng chấm từ TeamSize 4/6/8. Mỗi phần có điểm của hai đội cha. Trọng tài tự chọn phần và tự căn mốc 21; điểm được vượt 21, sửa phần trước và giảm về 0. Không có đồng hồ, tự chuyển phần, khóa phần hay tự kết thúc khi đạt điểm đích.
- `RelayMatchScores` lưu sáu số nguyên và Version theo MatchId. `RelayScoringService` kiểm tra đủ phần 1–3, điểm không âm, giới hạn tổng theo SQL int, phiên bản và tự tính tổng trên server. Điểm phần, tổng ở `TournamentGroupMatch`, snapshot đội hình và lịch sử được commit chung. Hai endpoint ghi khóa hàng trận trước khi đọc; phiên bản cũ trả 409. Các request đọc danh sách giữ transaction RepeatableRead để tổng/phần/lịch sử được đọc nhất quán.
- `PUT /api/referee/matches/{matchId}/score` và đường admin tương ứng nhận thêm `relay: { expectedVersion, changedPart?, allocateExisting?, parts: [{partNumber, scoreTeam1, scoreTeam2}] }`. Phải có đủ 3 phần; tổng client gửi không quyết định kết quả. Server xác định phần thực sự thay đổi cho lịch sử. Trận đơn/đôi vẫn dùng contract tổng điểm cũ; yêu cầu ghi tổng đơn thuần vào trận relay bị từ chối.
- GET danh sách và kết quả lưu thêm `relayScores: { version, requiresAllocation, parts }`. Các trường tổng điểm hiện hữu tiếp tục phục vụ lịch/sơ đồ/app. Event `tournament.match.score.updated` bổ sung relayScores, dùng chung cho cả admin và trọng tài. API state/command relay cũ vẫn trả 410, không khởi động lại RelayMatchEngine/RelayLeg.
- `TournamentMatchScoreHistories` thêm `RelayPartNumber`, `RelayPartsJson` chứa đủ 3 phần tại mỗi lần lưu và `ActorName`. RefereeUserId trở thành nullable để ghi đúng thao tác của admin đăng nhập cookie không có bản ghi Users; không gán hành động admin cho trọng tài được phân công. Lịch sử hiển thị ActorName hoặc tên tài khoản và sắp theo CreatedAt/ScoreHistoryId giảm dần.
- Trận chưa có điểm phần và tổng 0 bắt đầu với ba phần 0–0. Trận cũ có tổng khác 0 hiển thị requiresAllocation; người chấm nhập phân bổ khớp hai tổng và giữ trạng thái kết thúc trước đó, bấm Lưu phân bổ rồi mới chấm tiếp. Không sửa tổng/lịch sử cũ khi chạy migration.
- Khi kết thúc, server tính lại tổng ba phần và chọn đội có tổng cao hơn; tổng hòa thì từ chối. Ví dụ 21–18, 15–21, 21–20 cho tổng 57–59, đội 2 thắng. Snapshot đội hình vẫn được giữ từ lần chấm đầu. Đường admin đổi nguồn và propagation không thay đội/đặt lại điểm của trận đã có RelayMatchScores; sửa kết quả nguồn có thể cần ban tổ chức xử lý các trận sau đã bắt đầu, tương tự giới hạn với trận đã kết thúc.
- `wwwroot/js/relay-scoreboard.js` và `css/relay-scoreboard.css` dùng chung ở portal/admin. Portal hiển thị ba thẻ xếp dọc, hai đội theo hàng trên mobile và cạnh nhau khi đủ rộng; nút tối thiểu 48px, bàn phím số, tổng bám khi cuộn, ghi chú/lịch sử thu gọn. Mỗi lần +/- tự lưu, nhập tay bấm Lưu điểm. Trạng thái lưu tại thẻ, không phủ loading hay thay DOM khi nhận điểm/realtime. Điểm sửa giữ nguyên trước realtime; timeout/409 đọc lại server, không tự gửi lại lần cộng. Phiên bản request lấy từ bảng đang hiển thị để refresh cache không ghi đè điểm mới. Màn trọng tài cũ chuyển trận relay về portal chung.
- Migration bắt buộc: `database/updates/20260912_add_relay_three_part_scores.sql` trước khi chạy server mới (model lịch sử cần các cột mới kể cả khi flag tắt). Script tạo bảng, thêm các cột nullable và cho RefereeUserId null; giữ lại FK/index chuẩn khi đổi nullability. Chỉ kiểm thử trên database LocalDB riêng trong thay đổi này, chưa chạy trên database cấu hình thật hoặc deploy.
- Test: `RelayThreePartScoringTests` kiểm tra tổng, lịch sử, snapshot, quyền/ngày, dữ liệu không hợp lệ, phân bổ trận cũ, admin, xung đột phiên bản, bảo toàn trận đã chấm khi nguồn thay đổi và SQL rollback/concurrency/migration thật. `JavaScript/relay-three-part-scoring.test.js` chạy Razor/JS thực với API giả lập trên Edge, kiểm tra mobile 320–1280px, tên dài, landscape, viewport hẹp do bàn phím, lưu/timeout/409/realtime, kết thúc và màn admin. Ảnh kiểm tra trong `artifacts/relay-three-part-scoring/`; đây là kiểm thử trình duyệt desktop giả lập viewport, chưa UAT trên điện thoại thật.

### Thành viên dự bị trong đăng ký (10/09/2026)

- Mỗi đội được có 0–4 dự bị. Lưu riêng trong `RelayTeamReserveMembers` (RegistrationId, Position 1–4 kiểu SQL int, UserId tùy chọn cho admin, tên/ảnh); không thêm vào `RelayTeam.Members`, TeamSize, cặp, Player1/Player2, snapshot trận hoặc seed sơ đồ. Kiểm tra đủ đội hình chính 4/6/8, capacity theo đội, lệ phí và chấm điểm giữ nguyên.
- Script `database/updates/20260910_add_relay_reserve_members.sql` cần áp dụng trước khi chạy server mới trên mỗi môi trường; chỉ thêm bảng/constraint/index. Sau kiểm thử SQL LocalDB, script đã được chạy theo yêu cầu người dùng trên `112.78.2.114` / `van17737_ngocanh` lúc 20:30 ngày 10/09/2026 (UTC+7). Đã xác minh bằng kết nối mới: đủ 5 cột đúng kiểu, 2 check constraint, 2 khóa ngoại và 3 index gồm khóa chính; bảng ban đầu có 0 dòng. Kết quả lưu ở `artifacts/relay-reserves-20260910/remote-migration.json` và `remote-verification.json`.
- Cùng script đã được chạy tiếp theo yêu cầu người dùng trên `112.78.2.90` / `han33198_hanaka` lúc 20:34 ngày 10/09/2026 (UTC+7). Kết nối mới xác minh bảng dự bị có 0 dòng và cấu trúc cột/constraint/khóa ngoại/index khớp bản trên `van17737_ngocanh`. Kết quả riêng lưu ở `artifacts/relay-reserves-20260910/hanaka-migration.json` và `hanaka-verification.json`; không thay đổi cấu hình kết nối ứng dụng.
- Dữ liệu mẫu giải 37: theo yêu cầu người dùng, đã chạy `database/seeds/20260910_seed_tournament_37_relay_reserves.sql` trên database đang phục vụ localhost là `112.78.2.114` / `van17737_ngocanh`, thêm 32 dự bị khách (UserId null), mỗi đội mẫu 01–08 có 4 người, dùng ảnh có sẵn `/uploads/avatars/demo/relay37-avatar-sprite-v1.webp`. Script chỉ nhắm 8 đăng ký mẫu, không ghi đè dự bị khác, chạy lại không thêm trùng và tăng version đội khi có dữ liệu mới. Hash trước/sau xác nhận 48 thành viên chính, đăng ký, trận và snapshot trận giữ nguyên; API và DOM trang user hiển thị đủ 8 nhóm/32 dự bị. Kết quả tại `artifacts/relay-reserves-20260910/t37-reserve-seed-result.json`, `t37-public-seed-verification.json` và ảnh `t37-live-reserves.png`.
- User POST relay thêm `reserveMembers: [{ position: 1, userId: 123 }]`, có thể bỏ qua/null/rỗng khi tạo. User chỉ chọn tài khoản đang hoạt động; admin giữ cách nhập User ID hoặc tên khách. Danh sách chính vẫn ở `members`/`relayMembers`. Không thêm quyền user tự sửa đội sau đăng ký hay chức năng thay người trong trận.
- Admin create/update thêm `relayReserveMembers`. Khi update: bỏ qua/null và không có cờ thì giữ dự bị; gửi danh sách sẽ thay thế. Multipart không biểu diễn mảng rỗng, nên UI gửi `relayReserveMembersIncluded=true` cùng các dòng; cờ true không có dòng nghĩa là xóa toàn bộ dự bị. Có kiểm tra version như roster hiện tại. Đường lưu roster cũ qua RelaySetup giữ dự bị và kiểm tra tránh trùng với roster mới.
- Một User ID chỉ xuất hiện một lần trong đội và một đội trong cùng giải, tính cả chính thức/dự bị. `RelayReserveMembers` kiểm tra tổng hai nhóm trong transaction Serializable của caller. Member-search và trạng thái đăng ký nhận diện tài khoản dự bị đã thuộc đội; không thêm lời mời, phê duyệt hay thông báo loại mới.
- Danh sách admin/public và trang đội đã đăng ký trả trường dự bị riêng; form có mục mở/thu gọn tùy chọn, tìm kiếm danh sách bao gồm tên/ID dự bị. Public đọc dự bị theo một truy vấn batch riêng để không nhân số hàng roster chính. App cũ tiếp tục gửi payload hiện tại; app muốn nhập/hiển thị dự bị cần bổ sung giao diện riêng.
- Kiểm thử: `RelayReserveRegistrationTests` bao phủ giới hạn/trùng/tài khoản/thiếu đội hình, trạng thái/search/public, bỏ qua/thay/xóa/version và giữ snapshot. SQL opt-in `HANAKA_RELAY_SQL_TESTS=1` dùng migration thật chạy lặp và vòng đời user/admin trên database tạm. Test trình duyệt kiểm tra form và hiển thị mobile/desktop.

### Chi tiết trùng thành viên khi admin đăng ký (12/09/2026)

- Form thêm/sửa đăng ký tiếp sức trả lỗi `ATHLETE_ALREADY_REGISTERED` kèm tất cả tài khoản trùng: tên hiện tại, User ID, tên đội, mã đăng ký và vị trí chính/dự bị; dữ liệu cũ chỉ có đội trưởng hoặc Player1/Player2 vẫn được nhận diện. Mỗi tài khoản/đăng ký xuất hiện một lần dù được lưu đồng thời ở roster, đội trưởng và Player1/Player2.
- `RelayReserveMembers.ValidateAssignmentsAsync` chỉ bổ sung thông tin chi tiết khi admin truyền `includeConflictDetails: true`. Kiểm tra vẫn nằm trong transaction của caller, giới hạn cùng giải và bỏ qua đội đang sửa. Admin dùng chung kiểm tra này thay cho kiểm tra đội hình chính chỉ báo người đầu tiên.
- `Views/Registrations/Index.cshtml` giữ xuống dòng và ngắt tên dài trong lỗi thêm/sửa; hiển thị bằng `textContent` và giữ dữ liệu đã nhập để admin chỉnh lại. Kiểm thử `AdminRelayRegistrationTests` bao phủ nhiều đội, chính/dự bị, dữ liệu cũ, sửa đội, khác giải và SQL LocalDB độc lập; browser test `admin-relay-user-lookup.test.js` kiểm tra thông báo nhiều dòng và nội dung tên không bị diễn giải thành HTML.

### Luật đã chốt

- Admin chọn đội chính 4, 6 hoặc 8 người theo từng giải, tương ứng 2, 3 hoặc 4 cặp trong đội hình đăng ký.
- Ở luồng user, người đang đăng nhập là đội trưởng và tự động giữ vị trí 1; đội trưởng nhập tên đội rồi tìm/chọn các tài khoản còn lại bằng tên, số điện thoại hoặc User ID.
- Tất cả thành viên phải là tài khoản đang hoạt động. Không có bước mời hoặc chờ thành viên đồng ý; đăng ký hợp lệ sẽ tạo đủ đội hình và khóa ngay trong cùng transaction.
- Một tài khoản không được lặp trong đội và không được thuộc đội relay khác của cùng giải. Thành viên nhận thông báo thông tin sau khi đội được tạo nhưng không phải xác nhận.
- Cặp, vị trí và thứ tự chỉ mô tả roster; runtime không theo dõi cặp con nào đang thi đấu hoặc ghép cặp con giữa hai đội.
- Trận là cuộc đấu giữa hai `TournamentRegistration` đội cha; tổng điểm trên `TournamentGroupMatch` bằng tổng ba phần lưu trong `RelayMatchScores`.
- Trọng tài cộng/trừ hoặc nhập điểm hai đội ở từng phần, ghi chú và lưu lịch sử qua cùng endpoint score; tổng được tính lại mỗi lần lưu.
- Ba phần chấm độc lập, không có bộ điều khiển chuyển lượt hay đồng hồ. `TargetScore` không tự hoàn tất trận.
- Trọng tài chủ động bật kết thúc trận và xác nhận; không hỗ trợ kết quả hòa, đội cha có điểm cao hơn là đội thắng.
- Lệ phí tính một lần theo đội. Một `TournamentRegistration` gắn với một `RelayTeam` là một nghĩa vụ thanh toán, không nhân theo số thành viên.
- Bracket template vẫn hoạt động như cũ; một vị trí/seed là một đội chính.
- Template có `ParticipantMode`: `STANDARD` cho đơn/đôi và `RELAY_TEAM` cho đội tiếp sức. `FormatType` vẫn chỉ mô tả topology loại trực tiếp/vòng bảng/nhánh thắng-thua/tùy chỉnh.
- Không tự chuyển các giải đơn/đôi hiện hữu sang relay.

### Cấu hình và tương thích dữ liệu

- `TeamSize` chỉ nhận 4, 6 hoặc 8; `PairCount` được suy ra bằng `TeamSize / 2`.
- `TargetScore`, `LegDurationSeconds` và `DeadlinePolicy` còn trong schema để tương thích database đã triển khai nhưng runtime chấm điểm không sử dụng.
- Luồng hiện tại không tạo mới `RelayMatchState`, `RelayLeg` hoặc `RelayMatchCommand`; các bảng này chỉ còn phục vụ đối chiếu dữ liệu legacy nếu từng có trận chạy theo cơ chế cũ.

### Phần đã có trong code

- Model và EF mapping relay trong `RelayModels.cs` và `PickleballDbContext.Relay.cs`.
- Bốn script SQL additive, constraint/index, trigger khóa roster/quy mô, bước chuyển timer sang chỉ-hiển-thị và phân loại bracket template tiếp sức.
- `RelayLineupService`: lưu draft, validate vị trí và khóa đội hình hoàn chỉnh.
- `RelayMatchEngine`, `RelayMatchStore`, `RelayMatchReader` và các bảng state/lượt/command được giữ làm code/data legacy, không còn đăng ký runtime hoặc được expose để chấm điểm.
- GET công khai `/api/tournaments/matches/{matchId}/relay` và API trọng tài `/api/referee/matches/{matchId}/relay` trả `410 Gone` khi feature relay đang bật; client phải dùng tỷ số trận chung.
- Admin preview API/trang để lưu settings, nhập roster cho registration đã tồn tại và khóa roster.
- `/Home/Tournaments` có nút tạo giải tiếp sức riêng; thao tác tạo lưu đồng thời giải và `RelayTournamentSettings`, nhận quy mô 4/6/8 cùng điểm đích, rồi chuyển admin sang trang chuẩn bị đội.
- Form tạo/sửa giải giới hạn `SingleLimit` và `DoubleLimit` trong khoảng `0..99.99` ngay trên UI; API kiểm tra lại cùng phạm vi trước khi ghi để phù hợp schema `decimal(4,2)`.
- `/Registrations/Index` nhận biết giải relay: form tạo/sửa sinh đúng 4/6/8 vị trí, lưu `TournamentRegistration`, `RelayTeam` và toàn bộ `RelayTeamMembers` trong cùng transaction. Đội đủ người hợp lệ được dùng ngay; admin vẫn được sửa, còn trận đã nhập điểm đọc đội hình từ `RelayMatchLineupSnapshots`.
- `RelayMatchLineupSnapshot.Side` giữ kiểu `int` trong C# nhưng ánh xạ qua `HasConversion<byte>()` sang SQL `tinyint`, đúng với script `20260908_remove_relay_lineup_lock_and_add_match_snapshots.sql`. Thiếu chuyển đổi này gây `InvalidCastException` (`Byte` sang `Int32`) khi trang trọng tài hoặc `RelayTeamReader` đọc snapshot đã lưu. Test SQL trong `RelayMatchLineupSnapshotTests` dùng chính script trên LocalDB riêng để kiểm tra lưu snapshot, projection của danh sách trận, đọc lại khi chấm điểm và giữ đội hình cũ sau khi sửa roster; không chỉ dựa vào InMemory hay schema do `EnsureCreated` sinh ra.
- Trang user `/PickleballWeb/Tournament/{id}/Register` nhận biết giải relay: đội trưởng nhập tên đội, tìm tài khoản đang hoạt động theo tên/số điện thoại/User ID, chọn đủ các vị trí còn lại và tạo đội hoàn chỉnh đã khóa ngay; không tạo lời mời ghép cặp hay yêu cầu thành viên duyệt.
- Trang công khai `/PickleballWeb/Tournament/{id}/Registrations` nhận biết giải relay và hiển thị theo đội cha: tên đội, trạng thái đủ người/khóa, toàn bộ roster nhóm theo cặp với ảnh, tên, User ID và trình đôi mới nhất. Tên/ảnh lấy từ hồ sơ tài khoản hiện tại và fallback về snapshot đăng ký; tên dài được xuống dòng. Từ 12/09/2026, chỉ thành viên có `userId > 0` mới hiển thị dòng User ID và trình đôi; thành viên khách (cả chính và dự bị) ẩn toàn bộ dòng này, giữ tên, ảnh và vị trí. Tài khoản có trình bằng 0 vẫn hiển thị thông tin. Giao diện relay không dùng hai cột Player1/Player2 hoặc tổng trình legacy và dùng chung hành động thanh toán của registration.
- `GET /api/tournament-registrations/tournaments/{tournamentId}/relay/member-search` trả kết quả tài khoản tối thiểu cần thiết, che số điện thoại và đánh dấu tài khoản đã thuộc đội; `POST .../{tournamentId}/relay` luôn lấy đội trưởng từ JWT thay vì tin dữ liệu client.
- API user tạo registration đội cha, `RelayTeam` và roster 4/6/8 người theo hai bước lưu trong một transaction: lưu đội hình chưa khóa để trigger kiểm tra được roster, sau đó khóa ngay. Hai thành viên đầu vẫn được mirror sang Player1/Player2 để tương thích dữ liệu cũ.
- API đăng ký relay kiểm tra lại số lượng/vị trí, tài khoản trùng trong đội và tài khoản đã thuộc đội khác trong cùng giải; hai thành viên đầu được mirror vào Player1/Player2 để tương thích nhưng tên đội không được dùng làm VĐV giả và `Points` không được tính sai từ hai người đầu.
- Danh sách đăng ký relay tách trạng thái “đã chốt/chưa chốt”, tính capacity theo số đăng ký đội, ẩn ghép đôi/đồng bộ trình legacy, cho xóa transactionally đội hình chưa chốt và chặn sửa/xóa đội hình đã chốt.
- Thẻ đội hình relay trên màn quản lý đăng ký hiển thị tên, ảnh đại diện snapshot, User ID và trình đôi mới nhất của từng thành viên có tài khoản; trình lấy từ `UserRatingHistories` và fallback về cache `Users.RatingDouble`.
- Payment admin hiển thị toàn bộ roster nhưng vẫn thu một lần theo registration; lịch vòng đấu/màn trận dùng tên `RelayTeam`; nguồn đội trực tiếp, bracket và giải thưởng chỉ nhận đội đủ người đã chốt.
- Khi xác nhận giải thưởng relay, achievement/rating notification được áp dụng cho toàn bộ thành viên có tài khoản trong `RelayTeamMembers`, không chỉ Player1/Player2.
- Admin cấu hình đội 4/6/8, điểm đích và kích hoạt thi đấu sau khi có ít nhất hai đội hình hoàn chỉnh đã khóa.
- `RefereeMatchesApiController` cho phép chấm relay qua `PUT /api/referee/matches/{matchId}/score`, dùng hai registration đội cha và chỉ hoàn tất khi request đặt `IsCompleted = true`.
- Trang cũ `/RefereePortal/RelayMatch/{id}` chuyển hướng về `/RefereePortal/Matches/{id}`; thẻ relay mở cùng bảng chấm điểm đơn/đôi.
- Relay dùng chung realtime event `tournament.match.score.updated`; event riêng `tournament.relay.match.updated` đã ngừng phát.
- Payment checkout dùng phí registration một lần cho cả relay team, không nhân theo `TeamSize`; bất kỳ user đã đăng nhập nào cũng có thể thanh toán hộ và mở lại checkout dùng chung, không bắt buộc thuộc roster hoặc là captain, đồng thời admin vẫn có đường hỗ trợ.
- Dữ liệu demo giải 37 có script enrich idempotent `database/seeds/20260907_enrich_tournament_37_relay_profiles.sql`: cập nhật 60 tài khoản seed thành tên tiếng Việt và avatar nhân vật tổng hợp riêng, đồng bộ snapshot roster/Player1/Player2 trong một transaction; ảnh sprite WebP nằm tại `wwwroot/uploads/avatars/demo/relay37-avatar-sprite-v1.webp` và không dùng danh tính của user thật.
- API danh sách đăng ký công khai tách riêng nhánh tiếp sức: metadata kèm bộ đếm + đội hình phẳng, tối đa 2 lệnh SQL thay vì chạy truy vấn đơn/đôi rồi nạp đội hình; dữ liệu được cache trong bộ nhớ 5 giây (cấu hình `PublicRegistrations:CacheSeconds`, giới hạn 0-30 giây), timeout SQL được thử lại một lần rồi trả HTTP 503 có mã `DATABASE_TIMEOUT`.
- Trang `/PickleballWeb/Tournament/{id}/Registrations` dùng metadata có sẵn trong response danh sách, không gọi trùng endpoint chi tiết giải. Index đọc công khai nằm trong `database/updates/20260907_optimize_public_relay_registrations.sql`.

### Danh sách tiếp sức tương thích app đã phát hành — 12/09/2026

- App hiện có chỉ đọc `player1/player2` và chọn giao diện một VĐV theo `tournament.tournamentTypeCode` trước `gameType`. `GET /api/public/tournaments/{id}/registrations` mặc định trả đội tiếp sức như một VĐV: `tournamentTypeCode = SINGLE`, `player1.name = tên đội` (fallback `Đội {regCode}`), `player1.level = tổng trình đôi đội hình chính`, `player1.verified = true`, `player1.isGuest = false`, `player1.userId/avatar = null`, `player2 = null`. `points` bằng tổng trình để hai vị trí hiển thị số trên app nhất quán.
- Tổng chỉ cộng `members` của đội hình chính 4/6/8 người, không cộng `reserveMembers`. Tái sử dụng trình đã được truy vấn từ lịch sử mới nhất (RatedAt, RatingHistoryId), fallback `Users.RatingDouble`, rồi 0 khi không có dữ liệu. Không thêm truy vấn từng thành viên.
- Đây là phép chiếu DTO trước khi cache, không ghi database hoặc thay đổi loại giải, tài khoản/xác thực thật, đăng ký, roster, lệ phí hay thanh toán. `isRelay`, `teamName`, `captainUserId`, `members`, `reserveMembers`, readiness, registration ID/mã/thời gian, trạng thái và bộ đếm vẫn được giữ. `gameType` tiếp tục phản ánh dữ liệu thật; `SINGLE` chỉ phục vụ cách hiển thị của app cũ tại endpoint danh sách.
- Web yêu cầu `?view=full` để nhận nguyên contract relay (`tournamentTypeCode = RELAY_TEAM`, hai player legacy, toàn bộ đội hình). Cả trang danh sách, loader chi tiết cũ và trang thanh toán trong App WebView dùng tham số này; loader chi tiết native đang hoạt động chỉ đọc metadata giải. Giải đơn/đôi không đổi theo view; không nhận diện app dựa trên User-Agent, cookie hoặc đăng nhập.
- Cache tách theo tournament ID, tab và view hiệu lực (`app-summary`/`full`), không sửa đối tượng đã cache. Giữ TTL hiện hành. Khi cần quay lại response cũ cho app, đặt `PublicRegistrations:LegacyRelaySummaryEnabled = false` (environment variable `PublicRegistrations__LegacyRelaySummaryEnabled=false`) rồi khởi động lại nếu môi trường không reload cấu hình. Mặc định bật khi thiếu key; feature flag relay hiện hữu vẫn được áp dụng. View hiệu lực trong cache key cũng đổi theo công tắc rollback.
- Kiểm chứng cục bộ: 246 test .NET đạt, 24 SQL integration test bỏ qua vì chưa bật điều kiện chạy; 31 test JavaScript đạt, không bỏ qua. `RelayAppRegistrationSummaryTests` kiểm tra 4/6/8 người, trình/dự bị/fallback, thanh toán và ID, nguyên trạng database, cache ở cả hai thứ tự request, rollback, tab, giải thường và HTTP thật qua Kestrel/InMemory với JSON camelCase/query binding. `relay-app-registration-summary.test.js` kiểm tra request và roster web, đồng thời chạy nguyên mã `RegistrationListScreen.js` trong môi trường mô phỏng native/hooks để kiểm tra giao diện một VĐV, nhãn, tìm kiếm, nhận diện registration của dự bị và URL thanh toán. Test mobile chỉ đọc repo app qua đường dẫn sibling hoặc `HANAKA_MOBILE_ROOT`; bỏ qua nhánh này trên máy không có source/dependency Babel của app.
- Phát hành cùng server và `wwwroot/pickleball-web/js/pages.js`; layout đã dùng `asp-append-version` cho script. Không cần migration mới hoặc phát hành app. Chưa UAT trên APK/IPA đã phát hành và chưa deploy lên server thật trong thay đổi này.

### Các cập nhật giao diện và tích hợp khác

- Thẻ đội trên danh sách tiếp sức (12/09/2026) dùng grid để số thứ tự 32px nằm cùng hàng với tên đội, metadata mã/ngày tự xuống dòng và trạng thái gọn bên dưới trên mobile. Ảnh VĐV 36px, khoảng cách và header cặp giảm; lưới cặp tự chọn số cột theo bề rộng thực tế của thẻ, mobile <=640px dùng một cột. Tên đội/VĐV dài được xuống dòng, không cắt thông tin. Thành viên khách tiếp tục ẩn User ID/trình; ảnh relay bị lỗi sau khi hết URL fallback chuyển sang chữ viết tắt. Kiểm tra trực tiếp trang localhost giải 37 ở 320/360/390/430/768/1280px và tên dài không tràn ngang; thẻ Team BẮC BLING ở 390px giảm từ khoảng 1055px còn 847px, header từ 112px còn 89px. Ba test JavaScript liên quan đạt (gồm 46 kiểm tra tương tác trình duyệt). Ảnh và số đo nằm trong `artifacts/relay-card-compact-20260912/`.

- Ảnh bìa trang chi tiết `/PickleballWeb/Tournament/{id}` hiển thị toàn bộ theo tỷ lệ gốc (`height: auto`, `object-fit: contain`), thay cho khung cao 150 px cắt ảnh; khung dự phòng khi không có ảnh vẫn cao 150 px. Đã kiểm tra trực tiếp trang giải 37 trên localhost bằng Edge ngày 10/09/2026 ở độ rộng 320, 390, 768 và 1440 px: ảnh gốc 1024×1535 hiển thị đúng tỷ lệ, không tràn ngang.
- Thông tin dưới ảnh bìa (10/09/2026) dùng nhãn/giá trị trong `dl`, lưới hai cột với địa điểm và đơn vị tổ chức chiếm cả hàng. Các số liệu được tách vào khung “Quy mô tham gia”, nội dung và bốn liên kết công khai có khung thống nhất. Liên kết nằm trong lưới 2×2 cùng kích thước, có viền focus bàn phím; giữ nguyên giá trị từ API, URL điều hướng và cách hiển thị toàn bộ ảnh bìa. Kiểm tra Edge trên trang giải 37 ở 320–1440 px và nội dung dài giả lập đạt 8 trường hợp; 17/17 test JavaScript tại thời điểm đó đạt.
- Trang chi tiết giải (11/09/2026) bỏ nhãn trạng thái cạnh tên giải; ẩn trường thiếu dữ liệu, chuỗi trống/dấu gạch và giới hạn trình đơn/đôi bằng 0. Nhãn “Cặp tối đa” là `doubleLimit`; các bộ đếm thực sự bằng 0 vẫn hiển thị, bộ đếm không có dữ liệu và toàn bộ khung rỗng được ẩn. Hai liên kết “Danh sách đăng ký”, “Lịch thi đấu” có bản nổi cố định dưới màn hình, xếp hai hàng theo thứ tự này; ẩn khi cả hai liên kết gốc xuất hiện trong vùng nhìn và hiện lại khi cuộn qua. `IntersectionObserver`, sự kiện cuộn/resize và `visualViewport` cập nhật trạng thái qua animation frame; khoảng trống cuối trang được giữ ổn định theo chiều cao thanh nổi và safe area để tránh che nội dung/nhấp nháy, có dọn observer/listener khi khởi tạo lại. Kiểm tra trực tiếp giải 37 trên Edge ở 320, 390, 768, 1440 px đạt; `tournament-detail-actions.test.js` đạt 52 kiểm tra trình duyệt gồm cuộn trước/tới/sau nhóm gốc, đổi kích thước, render lại và fallback thiếu IntersectionObserver. Bộ test dùng đồng hồ thực qua CDP (`helpers/edge-browser.js`) để kiểm tra cuộn/animation frame; toàn bộ 28 test JavaScript đạt. Ảnh kiểm chứng nằm trong `artifacts/tournament-detail-20260911/` (gitignored); không ghi dữ liệu giải.
- Thẻ trận lịch thi đấu công khai (10/09/2026) gom số thứ tự, ID, giờ và sân vào header tự xuống dòng khi thiếu chỗ; bo góc thẻ 6 px, nhãn 4 px và giảm padding/khoảng cách. Tên đội và tỷ số nằm trong cùng hàng grid; tên dài có dấu ba chấm và tooltip. Giữ các thuộc tính `data-schedule-*` cùng hàm cập nhật tỷ số realtime và các liên kết video/diễn biến. Edge xác nhận thẻ #763 giảm từ khoảng 189 xuống 137 px trên màn hình 390 px, căn đúng tỷ số, không tràn ở 320–1440 px và khi tên đội/sân dài; 17/17 test JavaScript hiện có đạt.
- `RelayBracketAdapter`: lọc đội đã khóa, enrich seed và lưu/khôi phục snapshot roster theo bracket application.
- Thư viện bracket cho phép tạo, lọc và nhân bản template tiếp sức; editor hiển thị rõ mỗi seed là một đội và không đưa quy mô đội hay điểm đích vào graph dùng chung.
- Luồng áp dụng chỉ đưa template `RELAY_TEAM` vào giải đã có relay settings và chặn áp dụng chéo với template `STANDARD`; relay không tạo đội ảo thiếu roster, chỉ dùng đội thật đã chốt hoặc BYE.
- `RelayTeamReader`: chiếu tên đội/roster vào API lịch và chi tiết trận mà vẫn giữ field Player1/Player2 legacy.
- `RelayLegacyWriteGuard`: vẫn chặn sửa/xóa/reset match có state legacy và thay đổi registration relay không an toàn, nhưng không chặn đường score chung.

### Phần chưa hoàn tất

- Propagation sau commit có retry và đầy đủ guard cho mọi đường ghi round/group/source/BYE/registration.
- Standings/ranking cho toàn đội; xử lý BYE, bỏ cuộc, thiếu người và tiêu chí phụ.
- Hiển thị tên đội/roster đầy đủ ở standings, video và các màn public còn lại ngoài trang danh sách đăng ký đã hỗ trợ relay.
- UAT server thật, thiết bị mobile, dữ liệu demo và checklist release.
- Bốn SQL relay đã được áp dụng lên database đang cấu hình ngày 06/09/2026; feature flag đã bật trong `appsettings.json`, chưa UAT và chưa phát hành.

### Feature flag và rào an toàn

- `Relay:AdminPreviewEnabled` hiện là `true` trong `appsettings.json` sau khi schema đã được triển khai; environment variable là `Relay__AdminPreviewEnabled`.
- Khi flag tắt, admin/public relay endpoint trả 404 trước khi truy vấn bảng relay.
- Flag cho phép truy cập schema relay, trang chuẩn bị, projection và guard; API state/command thi đấu cũ trả `410 Gone` khi flag bật.
- `RelayTournamentSettings.IsEnabled` chỉ được bật qua command admin `POST .../relay/activate` sau khi cấu hình hợp lệ và tất cả đội hình đã được khóa.
- Không bật `IsEnabled` trực tiếp trong DB để đi vòng qua validation kích hoạt.
- `RelayMatchStore` không còn được đăng ký DI hoặc expose qua controller; quyền chấm relay đi qua cùng API phân công trọng tài như đơn/đôi.

### Quyết định nghiệp vụ còn mở

1. Quy tắc giao bóng, sửa điểm và sửa kết quả trận đã hoàn tất?
2. Quy trình thay người sau khi đội user đã được tạo và khóa xử lý thế nào?
3. Thiếu người, chấn thương, giới tính và giới hạn rating xử lý thế nào?
4. Standings, hiệu số, tiêu chí phụ, bỏ cuộc và BYE xử lý thế nào?

Không tự biến đề xuất kỹ thuật thành luật nghiệp vụ khi chưa có câu trả lời.

## 7. Test và bằng chứng hiện có

Project test tập trung vào:

- Bracket validation, generator/seeding, draft/publish và application workflow.
- Virtual team visibility.
- Public WebSocket và public realtime hub.
- Cookie API challenge/forbidden behavior.
- Admin round scheduling.
- Relay engine, admin feature flag, compatibility và SQL integration/concurrency.

Kiểm chứng trực tiếp ngày 06/09/2026 sau khi tích hợp đăng ký đội tiếp sức 4/6/8 người vào màn quản lý admin:

- 158 test server.
- 158 đạt.
- Trong đó có 16 SQL tests dùng LocalDB/database riêng.
- Build và test chạy native `net10.0`.

Kiểm chứng cục bộ ngày 06/09/2026 cho thay đổi thẻ thành viên relay: solution build thành công trên `net10.0`; 143/143 test không phụ thuộc SQL đạt, 16 SQL integration test được bỏ qua vì `HANAKA_RELAY_SQL_TESTS` không được bật, tổng cộng phát hiện 159 test.

Kiểm chứng cục bộ ngày 07/09/2026 cho luồng user tự đăng ký và danh sách công khai đội relay: solution build thành công trên `net10.0`; 10/10 test mục tiêu đạt, gồm các quy mô 4/6/8, tìm thành viên, chặn trùng/dữ liệu legacy/không hợp lệ, user ngoài roster được thanh toán hộ và dùng chung checkout, projection đầy đủ roster public và test chạy với schema cùng trigger SQL Server thật. Giao diện danh sách đã được render bằng Edge headless với đội 6 người, đủ 3 cặp và 6 thẻ thành viên. Toàn bộ suite khi bật `HANAKA_RELAY_SQL_TESTS=1` đạt 171/171, không bỏ qua test.

Kiểm chứng cục bộ ngày 07/09/2026 cho API loading toàn web: solution build thành công trên `net10.0`; suite không bật SQL đạt 154 test, bỏ qua 17 SQL integration test. Edge headless xác nhận vòng đời request đồng thời cho `fetch`, request nền `silent`, Axios interceptor và render overlay; môi trường kiểm tra không có Node.js nên file JavaScript unit test mới chưa được chạy trực tiếp bằng Node.

Tối ưu độ ổn định loading ngày 07/09/2026: mỗi request chỉ sở hữu một chế độ `button`, `section`, `global` hoặc `silent`; request phát sinh trực tiếp từ nút mặc định dùng spinner phủ tuyệt đối trong kích thước nút đã khóa nên không làm dịch chuyển nội dung. Global overlay trì hoãn 320ms, section overlay trì hoãn 180ms, mobile không dùng backdrop blur. Các màn hình Pickleball Web đã có loading cục bộ dùng request đọc `silent`; trang đăng ký giải có skeleton ổn định và cập nhật registration/register từ realtime bằng render im lặng thay vì reload toàn trang. Build đạt, suite .NET đạt 154 test và bỏ qua 17 SQL integration test; JavaScript contract test đã được bổ sung nhưng chưa chạy trực tiếp vì máy không có Node.js.

Đây là log lịch sử, không phải kết quả vừa chạy sau mỗi thay đổi. Trước khi phát hành:

1. Cài runtime .NET 10 phù hợp.
2. Chạy full suite native `net10.0`.
3. Chạy SQL tests trên LocalDB/database thử nghiệm riêng.
4. Chạy UAT trên bản sao schema/dữ liệu gần production.
5. Không cho test đọc hoặc migrate connection string production trong `appsettings.json`.

Khoảng trống test đáng chú ý: auth/OTP, club/direct chat, moderation, payment webhook và phần lớn controller CRUD chưa có độ bao phủ tương xứng với kích thước code.

## 8. Rủi ro và technical debt

### Ưu tiên bảo mật

- `appsettings.json` có lịch sử chứa DB, SMTP và OTP credential plaintext. Xoay toàn bộ credential liên quan, xóa khỏi history nếu repository từng được chia sẻ và dùng secret manager/environment variables.
- Admin credential hard-code trong `HomeController` phải được thay bằng identity/user DB và password hash trước production.
- JWT access token sống quá lâu và chưa thể hiện refresh-token lifecycle đầy đủ.
- CORS `AllowAll` cần giới hạn origin theo môi trường.
- Build log hiện cảnh báo vulnerability mức moderate cho phiên bản MailKit và MimeKit đang dùng; cần nâng phiên bản sau khi kiểm tra tương thích.
- Không log token, OTP, webhook secret, connection string hoặc payload nhạy cảm.

### Kiến trúc và bảo trì

- Một số controller/service trên 1.000 dòng; thay đổi nhỏ dễ tác động chéo.
- Transaction boundary và realtime side effect chưa được chuẩn hóa giữa các module.
- Manual SQL cần registry/version table hoặc quy trình deployment rõ ràng để biết script nào đã chạy.
- Subscription WebSocket chỉ ở memory nên chưa sẵn sàng scale nhiều instance.
- Public/admin/mobile/referee cùng nằm trong một app; thay đổi auth, route, DTO hoặc middleware có blast radius lớn.
- Generated/temp build directories nằm trong cây project gây nhiễu khi tìm kiếm; không xóa khi chưa xác nhận chúng không chứa dữ liệu cần giữ.

### Cảnh báo tính nhất quán

- Luôn phân biệt rating history chuẩn với field rating cache trong `Users`.
- Luôn phân biệt registration snapshot với thông tin user hiện tại.
- Luôn giữ backward compatibility của Player1/Player2 khi thêm projection relay.
- Mọi thay đổi score cần xét score history, winner, completion reason, propagation và realtime.
- Mọi thay đổi registration cần xét pair request, payment, seed/application hash, virtual team và relay roster.

## 9. Thứ tự đọc khi nhận task mới

1. `README.md` và file này.
2. `Program.cs` để xác định DI, auth và route.
3. Partial `PickleballDbContext` liên quan.
4. Controller nhận request.
5. Service được controller gọi.
6. DTO/entity và script SQL liên quan.
7. Test hiện hữu cùng module.
8. JS/Razor gọi endpoint nếu task ảnh hưởng giao diện.

Bản đồ nhanh:

- Auth/user → `AuthsController`, `WebAuthApiController`, `AppAuthService`, `UsersController`, `UserRatingService`.
- Club/chat → `ClubsController`, `DirectChatsController`, `RealtimeHub`, `WebSocketHandler`.
- Registration/payment → `TournamentRegistrationUserController`, `AdminRegistrationsController`, `TournamentRegistrationPaymentsController`, `TournamentRegistrationPaymentService`.
- Runtime tournament → `AdminTournamentRoundsController`, `AdminTournamentGroupMatchesController`, `TournamentClientController`, `TournamentStandingsService`, `TournamentBracketPropagationService`.
- Bracket library → `AdminBracketTemplatesController`, `BracketTemplateService`, `BracketTemplateValidationService`, `TournamentBracketApplicationService`.
- Referee → `RefereeAuthApiController`, `RefereeMatchesApiController`, `RefereePortalController`.
- Relay → `Models/RelayModels.cs`, partial DbContext relay, `Service/Relay`, relay controllers và bốn SQL update ngày 06/09/2026.

## 10. Quy tắc cập nhật ngữ cảnh

- Chỉ duy trì hai file Markdown: `README.md` và `HanakaServer/context.md`.
- Không ghi credential hoặc dữ liệu cá nhân vào tài liệu.
- Không ghi “đã hoàn thành” nếu chưa có code và bằng chứng kiểm tra tương ứng.
- Phân biệt rõ: đã implement, đã test mock, đã test integration, đã UAT và đã deploy.
- Khi thêm endpoint, entity, migration, feature flag hoặc thay luật nghiệp vụ, cập nhật file này trong cùng thay đổi.
- Khi một quyết định relay được chốt, chuyển nó từ “còn mở” sang “luật đã chốt” và cập nhật code/test liên quan.
- Không dựa vào log trong `artifacts` như trạng thái hiện tại nếu code đã thay đổi sau thời điểm log.
