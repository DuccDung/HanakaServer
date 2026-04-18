# HanakaServer Context

## 1. Tong quan du an

- Du an la mot ung dung ASP.NET Core Web (`net9.0`) gom nhieu vai tro trong cung 1 project:
  - JSON API cho mobile/client
  - Admin web dung Razor MVC
  - Referee portal rieng
  - Public landing page
  - WebSocket realtime cho chat/thong bao
- Entry point nam o `Program.cs`.
- Data layer dung `EF Core + SQL Server` qua `Data/PickleballDbContext.cs`.
- Khong thay thu muc `Migrations`, nen kha nang cao day la mo hinh database-first/scaffolded.

## 2. Cau truc thu muc chinh

- `Controllers/`: chua gan nhu toan bo business flow chinh.
- `Data/`: `PickleballDbContext`.
- `Models/`: entity model map voi DB.
- `Dtos/`: request/response DTO.
- `Service/`: email, OTP, realtime WebSocket.
- `Views/`: Razor view cho admin/public/referee.
- `wwwroot/`: static assets, uploads, css/js, giao dien public.

## 3. Startup, auth, route

### Startup

- `Program.cs` dang ky:
  - `AddControllersWithViews()`
  - `PickleballDbContext` voi `UseSqlServer(...)`
  - CORS policy `AllowAll`
  - Cookie auth + JWT auth
  - Authorization policy `RefereeOnly`, `AdminOnly`
  - WebSocket endpoint `/ws`

### Auth

- Cookie auth la mac dinh:
  - dung cho admin MVC
  - dung cho referee portal
- JWT Bearer:
  - dung cho mobile/client API
  - dung cho WebSocket, token co the di qua query `access_token`

### Route chinh

- `""` => `PickleballWeb/Index` (landing page public)
- `"{controller=Home}/{action=Login}/{id?}"` => MVC admin mac dinh
- `"RefereePortal/{action=Login}/{id?}"` => portal trong tai
- `"/ws"` => WebSocket realtime

## 4. Cau hinh quan trong

File: `appsettings.json`

- `ConnectionStrings:PickleballDb`
- `Jwt`
- `PublicBaseUrl`
- `Otp`
- `Email`
- `Support`
- `Smtp`

Luu y:

- Hien tai `appsettings.json` dang chua connection string va SMTP credential dang plaintext.
- Neu tiep tuc phat trien/nghiem tuc hoa moi truong, nen dua cac secret nay sang environment variables, user secrets hoac secret manager.

## 5. Kien truc nghiep vu tong the

Du an dang theo huong "controller-heavy":

- Controller khong chi nhan request ma con chua kha nhieu business logic.
- `Service/` hien tai chi giai quyet mot so concern cu the:
  - OTP/email
  - realtime WebSocket
- Chua thay layer application/service domain tach biet ro rang.

Dieu nay co nghia la khi sua tinh nang, phan lon thoi gian can doc `Controllers/*.cs` truoc.

## 6. Module chinh

### 6.1 Auth va nguoi dung

File quan trong:

- `Controllers/AuthsController.cs`
- `Controllers/UsersController.cs`
- `Service/OtpEmailService.cs`
- `Service/UserOtpService.cs`

Chuc nang:

- Dang ky tai khoan
- Gui OTP qua email
- Xac thuc OTP
- Dang nhap tra JWT
- Xem/sua profile
- Upload avatar
- Doi mat khau
- Xoa tai khoan (anonymize + vo hieu hoa)
- Tu cham diem trinh
- Xem lich su diem trinh
- Xem achievements

Luu y quan trong:

- Diem trinh chinh dang doc tu `UserRatingHistories`, khong con xem `Users.RatingSingle/RatingDouble` la nguon du lieu chuan.
- Tuy nhien, code van sync nguoc gia tri rating vao bang `Users` de giu tuong thich voi code cu.
- Khi user sua profile/rating, he thong sync sang ban shadow cua `Coach` va `Referee` neu co.

### 6.2 Clubs va chat realtime

File quan trong:

- `Controllers/ClubsController.cs`
- `Service/RealtimeHub.cs`
- `Service/WebSocketHandler.cs`

Chuc nang:

- Tao CLB
- Upload cover
- Join/approve/remove member
- Bat/tat challenge mode
- Lay danh sach CLB, overview, members, pending members
- Chat room theo CLB
- Upload media cho chat
- Xoa message cua chinh minh
- Push realtime message/typing/notification

Realtime flow:

- Client ket noi `/ws` bang JWT.
- `RealtimeHub` luu map:
  - `userId -> many sockets`
  - `socketId -> subscribed clubIds`
- `WebSocketHandler` xu ly message tu client:
  - `ping`
  - `club.subscribe`
  - `club.unsubscribe`
  - `club.typing`
- Server push:
  - `club.message.created`
  - `club.message.deleted`
  - `club.typing`
  - `club.notification`
  - `session.revoked`

Luu y:

- REST API van la noi luu source of truth vao DB.
- WebSocket chi dung de push state realtime sau khi luu xong.
- Chat co lien ket voi moderation va block user.

### 6.3 Tournament

File quan trong:

- `Controllers/PublicTournamentsController.cs`
- `Controllers/TournamentClientController.cs`
- `Controllers/AdminTournamentsApiController.cs`
- `Controllers/AdminRegistrationsController.cs`
- `Controllers/AdminTournamentRoundsController.cs`
- `Controllers/AdminTournamentRoundGroupsController.cs`
- `Controllers/AdminTournamentGroupMatchesController.cs`
- `Controllers/AdminTournamentPrizesController.cs`

Chuc nang:

- Public list/detail tournament
- Public registrations
- Client lay rounds -> groups -> matches
- Client lay standings cua tung round/group
- Client lay tournament rule
- Admin CRUD tournament
- Admin quan ly registration
- Admin quan ly round map, group, match
- Admin quan ly prize setup/confirm

Mo hinh tournament co nhieu lop:

- `Tournament`
- `TournamentRegistration`
- `TournamentRound`
- `TournamentRoundMap`
- `TournamentRoundGroup`
- `TournamentGroupMatch`
- `TournamentPrize`
- `TournamentMatchScoreHistory`

Hieu nhanh:

- `TournamentRoundMap` la map mot round cua giai.
- `TournamentRoundGroup` la group/bang nam trong round map.
- `TournamentGroupMatch` la tran dau cu the giua 2 registration.
- `TournamentRegistration` la doi dang ky / cap doi / nguoi choi.

### 6.4 Referee

File quan trong:

- `Controllers/RefereeAuthApiController.cs`
- `Controllers/RefereeMatchesApiController.cs`
- `Controllers/RefereePortalController.cs`
- `Views/RefereePortal/Matches.cshtml`

Chuc nang:

- Login referee bang cookie auth
- Lay danh sach tran duoc phan cong
- Cham diem tran dau
- Luu lich su cham diem vao `TournamentMatchScoreHistories`

Luu y:

- Referee portal la mot man hinh MVC/Razor + JS, khong phai SPA rieng.
- Referee chi duoc cham diem khi da den gio thi dau.

### 6.5 Admin MVC

File quan trong:

- `Controllers/HomeController.cs`
- `Controllers/DashboardController.cs`
- `Views/Home/*.cshtml`

Vai tro:

- Admin login
- Dashboard thong ke
- Cac trang quan tri tournament, banners, courts, links, registrations, rounds...

Luu y quan trong:

- `HomeController` dang hard-code admin account:
  - Email: `admin@hanaka.com`
  - Password: `123456`
- Day la diem can luu y rat manh ve bao mat va kha nang mo rong.

### 6.6 Moderation

File quan trong:

- `Controllers/ModerationController.cs`
- `Controllers/AdminModerationController.cs`

Chuc nang:

- User submit report
- User block nguoi khac
- User xem reports/blocks cua minh
- Admin xem queue moderation
- Admin hide message
- Admin eject/reinstate user
- Neu eject user thi realtime socket co the bi disconnect

Moderation co lien ket truc tiep voi:

- `ModerationReports`
- `UserBlocks`
- `ClubMessages`
- `RealtimeHub`

## 7. Model du lieu quan trong

### User side

- `User`
- `Role`
- `UserRole`
- `UserOtp`
- `UserRatingHistory`
- `UserAchievement`
- `UserBlock`

### Club side

- `Club`
- `ClubMember`
- `ClubMessage`

### Tournament side

- `Tournament`
- `TournamentRegistration`
- `TournamentRound`
- `TournamentRoundMap`
- `TournamentRoundGroup`
- `TournamentGroupMatch`
- `TournamentPrize`
- `TournamentMatchScoreHistory`

### Other domain

- `Coach`
- `Referee`
- `Court`
- `CourtImage`
- `Banner`
- `Link`
- `Exchange`
- `ModerationReport`

## 8. Cac quy uoc du lieu/logic dang ton tai

### Absolute vs relative URL

- DB thuong luu relative path, vi du `/uploads/...`
- API response thuong convert thanh absolute bang `PublicBaseUrl`

### Shadow profile

- `Coach` va `Referee` dang co ve duoc dong bo tu `User`
- Nhiem vu dong bo xuat hien trong `UsersController`
- `ExternalId` cua `Coach`/`Referee` duoc dung de lien ket voi `UserId` dang string

### Upload file

- Avatar: `wwwroot/uploads/avatars`
- Club cover: `wwwroot/uploads/clubs`
- Club message media: `wwwroot/uploads/club-messages`
- Tournament banner: `wwwroot/uploads/tournaments`

### Rating

- Source of truth moi: `UserRatingHistories`
- Bang `Users` van giu gia tri cache/legacy

## 9. Public UI va web UI

### Public landing page

- `Views/PickleballWeb/Index.cshtml`
- Giao dien la Razor + JS/CSS, lay du lieu public tu backend
- Muc tieu la giong giao dien mobile app

### Admin UI

- Razor view + JS tren trang
- Nhieu man hinh goi truc tiep cac API `/api/admin/...`

### Referee UI

- Razor page rieng, toan man hinh, tap trung cho viec cham diem

## 10. Diem can chu y khi tiep tuc doc/sua code

1. Neu can sua nghiep vu, doc controller truoc, vi logic dang nam o day la chinh.
2. Neu can sua schema/quan he, doc `PickleballDbContext.cs` truoc vi file nay map rat nhieu constraint/index/relationship.
3. Neu can sua chat realtime, xem ca `ClubsController`, `RealtimeHub`, `WebSocketHandler`.
4. Neu can sua tournament, thuong phai theo day:
   - registration
   - round map
   - group
   - match
   - prize / standings / referee scoring
5. Neu can sua profile user, luu y logic sync sang `TournamentRegistration`, `Coach`, `Referee`.
6. Neu can sua auth, nho rang he thong dang co 2 kenh auth khac nhau: Cookie va JWT.

## 11. Cac van de / debt ky thuat da nhan ra

### Bao mat

- Secret dang nam trong `appsettings.json`
- Admin account hard-code trong source

### Architecture

- Business logic tap trung nhieu trong controller
- Chua thay service layer/domain layer ro rang

### Consistency

- Logic default rating co dau hieu khong dong nhat:
  - `AuthsController` dang co default nam la `2.3`
  - `UsersController` dang co default nam la `2.6`

### Maintainability

- Project gom qua nhieu vai tro trong cung mot app:
  - public site
  - mobile API
  - admin MVC
  - referee portal
  - realtime gateway
- Ve sau co the can tach ro hon theo module hoac boundary.

## 12. Neu can onboard nhanh trong lan sau

Thu tu doc de hieu nhanh nhat:

1. `context.md`
2. `Program.cs`
3. `Data/PickleballDbContext.cs`
4. Controller theo module dang can sua:
   - auth/user => `AuthsController`, `UsersController`
   - club/chat => `ClubsController`, `RealtimeHub`, `WebSocketHandler`
   - tournament => `PublicTournamentsController`, `TournamentClientController`, `Admin*Tournament*`
   - referee => `RefereeAuthApiController`, `RefereeMatchesApiController`
   - moderation => `ModerationController`, `AdminModerationController`

## 13. Tom tat 1 cau

`HanakaServer` la mot monolith ASP.NET Core phuc vu ca mobile API, admin web, referee portal va realtime club chat, trong do phan lon nghiep vu nam truc tiep trong controllers va data duoc quan ly bang EF Core tren SQL Server.
