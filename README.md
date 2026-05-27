# HanakaServer

HanakaServer la backend monolith cho Hanaka Sport/Pickleball. Project phuc vu dong thoi public web, mobile API, admin web, referee portal va realtime WebSocket.

## Stack

- ASP.NET Core Web MVC/API, target `net9.0`
- Razor Views cho admin, public web va referee portal
- Entity Framework Core + SQL Server
- Cookie authentication cho web/admin/referee portal
- JWT Bearer authentication cho mobile API va WebSocket
- WebSocket realtime cho chat/thong bao
- MailKit/MimeKit + SMTP cho email/OTP
- Static assets trong `wwwroot`, giao dien admin dua tren Bootstrap/SB Admin 2

## Cau Truc Chinh

- `HanakaServer/Program.cs`: cau hinh DI, auth, route, CORS, WebSocket.
- `HanakaServer/Data/PickleballDbContext.cs`: EF Core DbContext va mapping database.
- `HanakaServer/Models`: entity model map voi SQL Server.
- `HanakaServer/Dtos`: request/response DTO.
- `HanakaServer/Controllers`: API va MVC controller, phan lon business logic dang nam o day.
- `HanakaServer/Service`: auth flow, OTP/email, realtime, notification, hosted service.
- `HanakaServer/Views`: Razor views cho admin/public/referee.
- `HanakaServer/wwwroot`: css/js/vendor, uploads va static files.
- `HanakaServer/context.md`: ghi chu doc code chi tiet hon ve nghiep vu va technical debt.

## Cau Hinh Quan Trong

File chinh: `HanakaServer/appsettings.json`

- `ConnectionStrings:PickleballDb`: SQL Server connection string.
- `Jwt`: issuer/audience/key/thoi gian song access token.
- `PublicBaseUrl`: base URL dung de build absolute URL cho anh/file public.
- `Otp`: thoi gian het han OTP.
- `Email`, `Support`, `Smtp`: cau hinh email ho tro va gui OTP.

Luu y bao mat: file `appsettings.json` hien co connection string va SMTP credential dang de plaintext. Khi deploy that su, nen dua cac gia tri nay sang environment variables, user secrets, secret manager hoac cau hinh rieng khong commit.

## Chay Local

Yeu cau:

- .NET SDK 9.x
- SQL Server co database tuong ung voi schema hien tai
- Connection string hop le trong `appsettings.json` hoac environment variable

Lenh chay:

```powershell
dotnet restore
dotnet run --project .\HanakaServer\HanakaServer.csproj
```

Neu gap loi:

```text
System.Net.Sockets.SocketException: The requested address is not valid in its context
```

kiem tra `HanakaServer/Properties/launchSettings.json`. Project dang bind vao IP LAN co dinh, vi du `192.168.1.101`. Neu may hien tai khong co IP nay, doi ve:

```json
"applicationUrl": "http://localhost:5062"
```

hoac dung `0.0.0.0` neu can cho may khac trong LAN truy cap.

## Routing Va Entry Points

Trong `Program.cs`:

- `/` -> `PickleballWebController.Index`, public web.
- `/{controller=Home}/{action=Login}/{id?}` -> route MVC mac dinh cho admin.
- `/RefereePortal/{action=Login}/{id?}` -> referee portal.
- `/ws-public` -> public WebSocket.
- `/ws` -> authenticated WebSocket, dung JWT.

Auth:

- Cookie mac dinh: web/admin/referee portal, cookie name `Hanaka.Auth`.
- JWT Bearer: mobile API va WebSocket; token co the gui bang `Authorization: Bearer ...`, query `access_token` cho WebSocket, hoac cookie web auth rieng.
- Authorization policies:
  - `AdminOnly`: role `Admin`
  - `RefereeOnly`: role `REFEREE` hoac `Admin`

## Module Nghiep Vu

### Auth Va User

File lien quan:

- `Controllers/AuthsController.cs`
- `Controllers/WebAuthApiController.cs`
- `Controllers/UsersController.cs`
- `Service/AppAuthService.cs`
- `Service/UserOtpService.cs`
- `Service/OtpEmailService.cs`

Chuc nang:

- Dang ky, OTP, quen mat khau, dang nhap JWT.
- Web auth API cho public web.
- Lay/sua profile, upload avatar, doi mat khau, self-rating, rating history, achievements.
- Dong bo mot phan thong tin user sang coach/referee shadow profile.

### Club Va Realtime Chat

File lien quan:

- `Controllers/ClubsController.cs`
- `Service/RealtimeHub.cs`
- `Service/WebSocketHandler.cs`

Chuc nang:

- Tao CLB, cover, join/approve/remove member.
- Challenge mode, danh sach CLB, members, pending members.
- Chat room theo CLB, upload media, delete message.
- Push realtime qua WebSocket: message created/deleted, typing, notification, session revoked.

### Tournament

File lien quan:

- `Controllers/PublicTournamentsController.cs`
- `Controllers/TournamentClientController.cs`
- `Controllers/TournamentRegistrationUserController.cs`
- `Controllers/AdminTournamentsApiController.cs`
- `Controllers/AdminRegistrationsController.cs`
- `Controllers/AdminTournamentRoundsController.cs`
- `Controllers/AdminTournamentRoundGroupsController.cs`
- `Controllers/AdminTournamentGroupMatchesController.cs`
- `Controllers/AdminTournamentPrizesController.cs`

Model chinh:

- `Tournament`
- `TournamentRegistration`
- `TournamentPairRequest`
- `TournamentRound`
- `TournamentRoundMap`
- `TournamentRoundGroup`
- `TournamentGroupMatch`
- `TournamentPrize`
- `TournamentMatchScoreHistory`

Chuc nang:

- Public list/detail tournament, registrations, rule, schedule, standings.
- User JWT dang ky giai single/waiting, tim partner, gui/accept/reject/cancel pair request.
- Admin CRUD tournament, registration, round map, group, match, score, prize.
- Luu lich su diem tran dau.

### Referee

File lien quan:

- `Controllers/RefereeAuthApiController.cs`
- `Controllers/RefereeMatchesApiController.cs`
- `Controllers/RefereePortalController.cs`
- `Views/RefereePortal/Matches.cshtml`

Chuc nang:

- Login/logout referee.
- Lay danh sach tran duoc phan cong.
- Cap nhat diem tran dau va winner.
- Role chap nhan: `REFEREE` hoac `Admin`.

### Admin

File lien quan:

- `Controllers/HomeController.cs`
- `Controllers/DashboardController.cs`
- `Controllers/Admin*Controller.cs`
- `Views/Home/*.cshtml`

Chuc nang:

- Admin login, dashboard.
- Quan ly users, tournaments, registrations, rounds, groups, matches, prizes.
- Quan ly banners, courts, coaches, referees, links.
- Moderation queue.

Luu y: `HomeController` co hard-code admin account trong source. Nen thay bang user DB/identity flow truoc khi van hanh nghiem tuc.

### Public Web

File lien quan:

- `Controllers/Web/PickleballWebController.cs`
- `Controllers/Web/PickleballWebAuthController.cs`
- `Views/PickleballWeb`
- `wwwroot/pickleball-web`

Chuc nang:

- Home public, club, coach, court, referee, exchange, tournament, match.
- Web login/register/forgot password/account.
- Giao dien Razor + JS/CSS, goi lai API backend.

### Moderation

File lien quan:

- `Controllers/ModerationController.cs`
- `Controllers/AdminModerationController.cs`

Chuc nang:

- User report/block/unblock.
- Admin review report, hide message, eject/reinstate user.
- Lien ket voi chat realtime de day thong bao/ngat session khi can.

## API Route Nhanh

Public/mobile:

- `POST /api/Auths/register`
- `POST /api/Auths/login`
- `GET /api/Users/me`
- `GET /api/public/tournaments`
- `GET /api/public/tournaments/{id}`
- `GET /api/public/tournaments/{id}/registrations`
- `GET /api/tournaments/{id}/rounds-with-matches`
- `GET /api/tournaments/{id}/rule`
- `GET /api/Clubs`
- `GET /api/Clubs/{id}`
- `GET /api/coaches`
- `GET /api/referees`
- `GET /api/public/courts`
- `GET /api/public/banners`

Tournament registration user flow:

- `GET /api/tournament-registrations/tournaments/{tournamentId}/me`
- `GET /api/tournament-registrations/tournaments/{tournamentId}/partner-search`
- `POST /api/tournament-registrations/tournaments/{tournamentId}/single`
- `POST /api/tournament-registrations/tournaments/{tournamentId}/waiting`
- `POST /api/tournament-registrations/tournaments/{tournamentId}/pair-requests`
- `POST /api/tournament-registrations/pair-requests/{pairRequestId}/accept`
- `POST /api/tournament-registrations/pair-requests/{pairRequestId}/reject`
- `POST /api/tournament-registrations/pair-requests/{pairRequestId}/cancel`

Admin:

- `GET/POST/PUT/DELETE /api/admin/tournaments`
- `GET/POST/PUT/DELETE /api/admin/tournaments/{tournamentId}/registrations`
- `GET/POST/PUT/DELETE /api/admin/tournaments/{tournamentId}/round-maps`
- `GET/POST/PUT/DELETE /api/admin/round-maps/{roundMapId}/groups`
- `GET/POST/PUT/DELETE /api/admin/groups/{groupId}/matches`
- `GET/POST/PUT/DELETE /api/admin/users`
- `GET/POST/PUT/DELETE /api/admin/banners`
- `GET/POST/PUT/DELETE /api/admin/courts`
- `GET/POST/PUT/DELETE /api/admin/coaches`
- `GET/POST/PUT/DELETE /api/admin/referees`

Referee:

- `POST /api/referee-auth/login`
- `GET /api/referee-auth/me`
- `GET /api/referee/matches`
- `PUT /api/referee/matches/{matchId}/score`

## Upload Paths

- Avatar: `wwwroot/uploads/avatars`
- Club cover: `wwwroot/uploads/clubs`
- Club message media: `wwwroot/uploads/club-messages`
- Tournament banner: `wwwroot/uploads/tournaments`

## Development Notes

- Khong thay thu muc EF migrations trong repo; DbContext co ve database-first/scaffolded.
- Business logic dang tap trung nhieu trong controller. Khi sua nghiep vu, doc controller theo module truoc.
- `UserRatingHistories` duoc xem nhu source of truth moi cho rating, trong khi `Users.RatingSingle/RatingDouble` van ton tai nhu cache/legacy.
- Nhieu API tra URL anh bang cach ghep relative path voi `PublicBaseUrl`.
- Project co nhieu vai tro trong mot app, nen can can than khi sua auth/routing vi co the anh huong ca admin, public web, mobile va referee.
- Working tree hien co the co thay doi san trong source; truoc khi refactor lon nen kiem tra `git status`.

## Tai Lieu Noi Bo

Doc them `HanakaServer/context.md` de xem ghi chu chi tiet ve:

- luong tournament/registration
- club chat realtime
- referee scoring
- moderation
- cac technical debt va diem can canh bao
