# Nghiep Vu Tao Giai Dau - `/Home/Tournaments`

Tai lieu nay ghi lai nghiep vu tao/chinh sua giai dau tu trang admin `https://localhost:7156/Home/Tournaments`.

Nguon da doc:

- `HanakaServer/Controllers/HomeController.cs`
- `HanakaServer/Views/Home/Tournaments.cshtml`
- `HanakaServer/Controllers/AdminTournamentsApiController.cs`
- `HanakaServer/Dtos/TournamentDtos.cs`
- `HanakaServer/Models/Tournament.cs`
- `HanakaServer/Data/PickleballDbContext.cs`
- `HanakaServer/Helpers/TournamentTypeHelper.cs`
- cac controller lien quan den public tournament, registration, round/group/match

Ghi chu: khong dieu khien duoc browser truc tiep trong phien nay vi browser plugin khong expose Node REPL tool can thiet. Phan doc nghiep vu duoc doi chieu tu source cua dung trang va API ma trang goi.

## 1. Tong Quan Luong

Trang `/Home/Tournaments` la man hinh admin quan ly danh sach giai dau.

Luot tao giai dau:

1. Admin vao `/Home/Tournaments`.
2. Bam nut `Tao giai dau`.
3. Modal `modalTournament` mo form `formTournament`.
4. Admin nhap thong tin giai va chon banner neu co.
5. JavaScript build `FormData`.
6. Neu mode la create, frontend goi:

```http
POST /api/admin/tournaments
Content-Type: multipart/form-data
```

7. Backend validate co ban, upload banner, tao entity `Tournament`.
8. Backend `SaveChanges`.
9. Frontend reload lai danh sach va dong modal.

Tao giai dau chi tao 1 dong trong bang `Tournaments`. He thong khong tu dong tao registrations, round maps, groups, matches, bracket hay prizes.

## 2. Quyen Truy Cap

Trang MVC:

- `HomeController.Tournaments()` co `[Authorize(Roles = "Admin")]`.
- Admin login dang hard-code trong `HomeController`:
  - email: `admin@hanaka.com`
  - password: `123456`
- Login thanh cong tao cookie auth role `Admin`.

API:

- `AdminTournamentsApiController` co `[Authorize(Roles = "Admin")]` o class.
- `POST /api/admin/tournaments`, `PUT`, `DELETE`, `GET detail` can role `Admin`.
- Rieng `GET /api/admin/tournaments` co `[AllowAnonymous]`, nen danh sach admin API hien co the bi doc ma khong can login neu request truc tiep.

## 3. Cac Trang Va API Lien Quan

Trang admin chinh:

- `/Home/Tournaments`

API ma trang goi:

- `GET /api/admin/tournaments?status=ALL&page=1&pageSize=10`
- `GET /api/admin/tournaments/{id}`
- `POST /api/admin/tournaments`
- `PUT /api/admin/tournaments/{id}`
- `DELETE /api/admin/tournaments/{id}`

Nut thao tac sau khi co giai:

- Dang ky: `/Registrations/Index?tournamentId={id}`
- Vong dau: `/TournamentRounds/Index?tournamentId={id}`
- So do admin: `/TournamentAdminBracket/Index?tournamentId={id}&returnUrl=...`
- Thiet lap giai thuong: `/TournamentPrizeSetup/Index?tournamentId={id}`

## 4. Form Tao Giai Dau

Form nam trong modal `#modalTournament`.

Truong bat buoc o UI:

- `title`: Tieu de
- `tournamentType`: Noi dung thi dau

Truong co san tren UI:

- `status`: `DRAFT`, `OPEN`, `CLOSED`
- `expectedTeams`: so doi du kien
- `areaText`: khu vuc
- `startTimeDate` + `startTimeTime`: thoi gian bat dau
- `registerDeadlineDate` + `registerDeadlineTime`: han dang ky
- `locationText`: dia diem
- `singleLimit`: gioi han diem don
- `doubleLimit`: gioi han diem doi
- `organizer`: don vi to chuc
- `creatorName`: nguoi tao giai
- `zaloLink`: link nhom Zalo
- `formatText`: the thuc thi dau mo rong
- `playoffType`: loai vong loai
- `content`: ghi chu ngan/mo ta phu
- `tournamentRule`: the le giai bang Quill editor
- `bannerFile`: anh banner

Gia tri mac dinh dang thay:

- status: `DRAFT`
- expectedTeams: `16`
- singleLimit: `0`
- doubleLimit: `0`
- tournamentType: rong, admin phai chon

## 5. Mapping Noi Dung Thi Dau

UI khong gui truc tiep `tournamentType`. UI parse thanh cap `gameType` + `genderCategory`.

Mapping frontend:

| UI value | Label | gameType | genderCategory |
| --- | --- | --- | --- |
| `DOUBLE_MEN` | Doi Nam | `DOUBLE` | `MEN` |
| `DOUBLE_WOMEN` | Doi Nu | `DOUBLE` | `WOMEN` |
| `SINGLE_MEN` | Don Nam | `SINGLE` | `MEN` |
| `SINGLE_WOMEN` | Don Nu | `SINGLE` | `WOMEN` |
| `DOUBLE_MIXED` | Doi Nam Nu | `DOUBLE` | `MIXED` |
| `DOUBLE_OPEN` | Hon Hop | `DOUBLE` | `OPEN` |
| `SINGLE_OPEN` | Don Mo / Du lieu cu | `SINGLE` | `OPEN` |

`SINGLE_OPEN` co trong code nhung option tren UI dang `hidden`.

Backend `ResolveTournamentType`:

- Chap nhan `GameType`: `SINGLE`, `DOUBLE`.
- Ho tro legacy: neu `GameType = MIXED` thi tu doi thanh `DOUBLE + MIXED`.
- Chap nhan `GenderCategory`: `OPEN`, `MEN`, `WOMEN`, `MIXED`.
- Chan `SINGLE + MIXED`.

DbContext co check constraint:

- `GenderCategory` chi duoc `OPEN`, `MEN`, `WOMEN`, `MIXED`.
- `SINGLE` chi di voi `OPEN`, `MEN`, `WOMEN`.
- `DOUBLE` di voi `OPEN`, `MEN`, `WOMEN`, `MIXED`.

## 6. Xu Ly Ngay Gio

UI tach ngay va gio thanh 2 input cho moi field:

- `startTimeDate` + `startTimeTime`
- `registerDeadlineDate` + `registerDeadlineTime`

Gio nhap dang 24h:

- Chap nhan dang `08:30`, `17:45`.
- Khi go, UI loc thanh so va chen dau `:`.
- Khi blur, UI canonical ve `HH:mm`.
- Neu chi nhap ngay hoac chi nhap gio, UI bao loi va khong submit.

Payload gui len backend:

```text
yyyy-MM-ddTHH:mm:00
```

Vi du:

```text
2026-06-01T08:30:00
```

Backend nhan bang `DateTime? StartTime` va `DateTime? RegisterDeadline`.

Diem can chu y:

- UI noi la gio Viet Nam nhung payload khong kem timezone.
- Backend khong convert timezone rieng.
- `CreatedAt` dung `DateTime.UtcNow`, con `StartTime/RegisterDeadline` phu thuoc model binding cua chuoi local datetime.
- Backend chua validate `RegisterDeadline <= StartTime`.
- Backend chua validate deadline phai o tuong lai khi tao giai.

## 7. Payload Tao Giai

Frontend `buildFormData()` append cac field:

```text
title
status
gameType
genderCategory
expectedTeams
startTime
registerDeadline
locationText
areaText
singleLimit
doubleLimit
content
tournamentRule
organizer
creatorName
zaloLink
formatText
playoffType
bannerFile
```

DTO backend: `CreateTournamentRequest`.

Kieu du lieu chinh:

- `Title`: string, required logic
- `GameType`: string, required logic
- `GenderCategory`: string optional, default xu ly la `OPEN`
- `Status`: string optional, default `DRAFT`
- `ExpectedTeams`: int?
- `StartTime`, `RegisterDeadline`: DateTime?
- `SingleLimit`, `DoubleLimit`: decimal?
- `BannerFile`: IFormFile?

## 8. Validate Khi Tao

Backend `Create()` dang validate:

- `Title` khong rong.
- `GameType` khong rong.
- `GameType/GenderCategory` hop le theo `ResolveTournamentType`.
- Neu co banner:
  - file extension phai la `.jpg`, `.jpeg`, `.png`, `.webp`.
  - request size limit: 10 MB.

Backend chua validate:

- `Status` co thuoc `DRAFT/OPEN/CLOSED` khong.
- `ExpectedTeams` co am khong.
- `SingleLimit`, `DoubleLimit` co am khong.
- `StartTime` co sau hien tai khong.
- `RegisterDeadline` co truoc `StartTime` khong.
- `RegisterDeadline` co sau hien tai khong khi status la `OPEN`.
- `zaloLink` co dung URL khong.
- `content/tournamentRule` co HTML an toan khong.
- banner MIME type, chi check extension.

## 9. Luu Database Khi Tao

Backend tao `Tournament` voi cac field:

```csharp
Status = status
Title = req.Title.Trim()
Remove = false
BannerUrl = bannerRelativeUrl
StartTime = req.StartTime
RegisterDeadline = req.RegisterDeadline
GameType = tournamentType.GameType
GenderCategory = tournamentType.GenderCategory
ExpectedTeams = req.ExpectedTeams ?? 0
LocationText = TrimToNull(req.LocationText)
AreaText = TrimToNull(req.AreaText)
SingleLimit = req.SingleLimit ?? 0
DoubleLimit = req.DoubleLimit ?? 0
Content = req.Content
TournamentRule = req.TournamentRule
ZaloLink = TrimToNull(req.ZaloLink)
CreatedAt = DateTime.UtcNow
Organizer = TrimToNull(req.Organizer)
CreatorName = TrimToNull(req.CreatorName)
FormatText = TrimToNull(req.FormatText)
PlayoffType = TrimToNull(req.PlayoffType)
StatusText = TrimToNull(req.StatusText)
StateText = TrimToNull(req.StateText)
```

File banner:

- Luu vao `wwwroot/uploads/tournaments`.
- Ten file create dang:

```text
{Guid:N}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}
```

- DB chi luu relative URL:

```text
/uploads/tournaments/{fileName}
```

Response DTO tra ve absolute banner URL bang:

```text
PublicBaseUrl + BannerUrl
```

## 10. Hien Thi Danh Sach Sau Khi Tao

Frontend goi lai `loadTournaments(true)`.

Danh sach:

- Goi `GET /api/admin/tournaments`.
- Sap xep backend: `CreatedAt DESC`.
- Phan trang: page/pageSize, max pageSize 200.
- Loc status neu status khac rong va khac `ALL`.
- Chi lay giai `Remove = false`.

Cot hien thi:

- banner
- title + khu vuc/dia diem + don vi to chuc/nguoi tao + format/playoff + zalo + preview the le
- status badge
- startTime
- registerDeadline
- noi dung thi dau label/code
- expectedTeams
- singleLimit
- doubleLimit
- cac nut thao tac

## 11. Sua Giai Dau

Mode edit dung cung modal.

Luot edit:

1. Bam nut edit.
2. Goi `GET /api/admin/tournaments/{id}`.
3. Fill form.
4. Submit goi `PUT /api/admin/tournaments/{id}` multipart.
5. Backend update field nao co gui.
6. Neu upload banner moi thi ghi banner moi va set lai `BannerUrl`.
7. Neu khong upload banner moi thi normalize `BannerUrl` ve relative.

Diem can chu y:

- Update title chi thuc hien neu title khong rong. Nghia la khong clear duoc title.
- Update status chi thuc hien neu status khong rong.
- Cac field string mo rong co the clear bang string rong vi backend check `!= null` roi `TrimToNull`.
- Neu update StartTime/RegisterDeadline khong gui field thi giu gia tri cu.
- Neu UI de trong ngay/gio thi `buildVietnamDateTimePayload` tra `""`, frontend khong append field, backend giu gia tri cu khi edit. Vi vay UI hien tai khong clear duoc StartTime/RegisterDeadline cua giai da co gia tri.

## 12. Xoa Mem

Nut xoa goi:

```http
DELETE /api/admin/tournaments/{id}
```

Backend:

- Tim tournament `Remove = false`.
- Set `Remove = true`.
- Khong xoa cascade registrations/rounds/groups/matches/prizes.

Tac dong:

- Admin list an giai vi query `!Remove`.
- Public/mobile cung an giai vi query `!Remove`.
- Du lieu con van nam trong DB.

## 13. Tac Dong Den Public/Mobile

Public API an giai `DRAFT` va giai da xoa mem:

- Detail: `GET /api/public/tournaments/{id}` chi lay `!Remove && Status != "DRAFT"`.
- List: `GET /api/public/tournaments` chi lay `!Remove && Status != "DRAFT"`.
- Registrations public cung chi lay tournament `!Remove && Status != "DRAFT"`.

Nghia la:

- `DRAFT`: chi admin thay, public/mobile khong thay.
- `OPEN`: public/mobile thay va co the dang ky neu qua API user registration.
- `CLOSED`: public/mobile van thay, nhung dang ky user bi chan.

`TournamentRule`, `ZaloLink`, `Organizer`, `CreatorName`, `FormatText`, `PlayoffType`, `SingleLimit`, `DoubleLimit`, `ExpectedTeams` duoc dua ra public DTO.

## 14. Tac Dong Den Dang Ky Giai

User JWT registration flow nam trong `TournamentRegistrationUserController`.

Dieu kien chung:

- Tournament phai ton tai, `!Remove`, `Status != "DRAFT"`.
- Khi tao registration that su, status phai la `OPEN`.
- Neu `RegisterDeadline` co gia tri va nho hon `DateTime.Now` thi khong cho dang ky.
- User khong duoc da co registration trong tournament.
- User khong duoc co pending pair request lien quan.

Capacity:

- `ExpectedTeams` la so team success toi da.
- `capacityLeft = ExpectedTeams - successCount`.
- Single registration can capacity > 0.
- Double waiting registration co the tao khong can capacity (`requireCapacity: false`).
- Khi ghep thanh doi success thi can capacity > 0.

Gioi han diem:

- Giai don dung `SingleLimit`.
- Giai doi dung `DoubleLimit`.
- Neu limit > 0 thi diem user/tong diem doi vuot limit se bi chan.
- Neu limit = 0 thi coi nhu khong gioi han.

Anh huong cua `GameType/GenderCategory`:

- `TournamentTypeHelper.IsDoubleLike` xem `DOUBLE` la giai doi.
- `DOUBLE_MIXED`, `DOUBLE_MEN`, `DOUBLE_WOMEN`, `DOUBLE_OPEN` deu di theo flow doi.
- `SINGLE_MEN`, `SINGLE_WOMEN`, `SINGLE_OPEN` di theo flow don.

## 15. Tac Dong Den Admin Registration

Admin registration API nam trong `AdminRegistrationsController`.

Khac voi user flow:

- Admin co the tao registration cho tournament theo `tournamentId`.
- Admin API doc tournament khong loc `Remove` va khong chan `DRAFT` trong doan tao registration da doc.
- Admin check capacity khi tao registration `Success`.
- Voi double waiting, admin co the tao waiting neu chua du cap.
- `RegCode` sinh theo:

```text
{tournamentId}-{nextIndex:0000}
```

Points:

- Single: `Player1Level`
- Double: `Player1Level + Player2Level`

Can chu y:

- Admin create registration hien chi chap nhan gameType `SINGLE` hoac `DOUBLE`.
- `DOUBLE_MIXED` van OK vi tournament luu `GameType = DOUBLE`, `GenderCategory = MIXED`.

## 16. Tac Dong Den Round/Group/Match

Sau khi tao giai, admin phai tu tao tiep:

1. Round map qua `/TournamentRounds/Index?tournamentId={id}`.
2. Group trong round map.
3. Match trong group.
4. Score/winner.
5. Prize setup neu can.

API round:

- `POST /api/admin/tournaments/{tournamentId}/round-maps`
- Yeu cau `RoundKey`, `RoundLabel`, `SortOrder`.
- Check tournament ton tai nhung khong check `Remove`/`Status`.
- `RoundKey` unique trong tournament.

API group:

- `POST /api/admin/round-maps/{roundMapId}/groups`
- Check group name khong rong va unique trong round map.

API match:

- `POST /api/admin/groups/{groupId}/matches`
- Match gan voi group, round map va tournament.
- Team registration phai thuoc tournament cua round map va phai `Success`.

Tao tournament khong sinh san cac round/group/match mac dinh.

## 17. Data Model Chinh

Entity `Tournament` co cac field nghiep vu chinh:

- `TournamentId`
- `ExternalId`
- `Status`
- `Title`
- `BannerUrl`
- `StartTime`
- `RegisterDeadline`
- `FormatText`
- `PlayoffType`
- `GameType`
- `GenderCategory`
- `SingleLimit`
- `DoubleLimit`
- `LocationText`
- `AreaText`
- `ExpectedTeams`
- `MatchesCount`
- `StatusText`
- `StateText`
- `Organizer`
- `CreatorName`
- `RegisteredCount`
- `PairedCount`
- `Remove`
- `ZaloLink`
- `Content`
- `CreatedAt`
- `TournamentRule`

Navigation:

- `TournamentRegistrations`
- `TournamentPairRequests`
- `TournamentPrizes`
- `UserAchievements`

## 18. Cac Quy Tac Nghiep Vu Dang Duoc Hieu Tu Code

- `Status = DRAFT`: ban nhap, khong hien public/mobile, user khong dang ky duoc.
- `Status = OPEN`: mo dang ky, user co the dang ky neu con han va hop le.
- `Status = CLOSED`: khong cho user dang ky, nhung van hien public/mobile.
- `Remove = true`: xoa mem, an khoi admin list va public/mobile.
- `ExpectedTeams`: so team thanh cong toi da, khong phai so registration waiting.
- `SingleLimit = 0` hoac `DoubleLimit = 0`: khong gioi han diem.
- `GameType + GenderCategory` la cap du lieu chuan hien tai, khong nen dung `GameType = MIXED` nua tru client cu.
- Banner nen luu relative path trong DB, response moi convert sang absolute URL.

## 19. Cac Diem Rui Ro / Can Sua Neu Nghiem Tuc Hoa

1. `GET /api/admin/tournaments` dang `[AllowAnonymous]` du controller la admin API.
2. Backend chua validate status whitelist khi create/update.
3. Backend chua validate expectedTeams/limits khong am.
4. Backend chua validate quan he thoi gian: deadline truoc start time, deadline con han khi mo dang ky.
5. UI edit khong clear duoc StartTime/RegisterDeadline da co.
6. Gio Viet Nam duoc UI mo ta nhung payload khong co timezone, backend khong normalize ro rang.
7. Banner chi check extension, chua check MIME/content.
8. Quill HTML duoc luu va render preview bang innerHTML; can dam bao sanitize neu noi dung co the den tu nguon khong tin cay.
9. Create tournament khong tao audit/log nguoi admin thuc hien. `CreatorName` chi la text admin nhap.
10. Admin account hard-code trong `HomeController`.
11. `StatusText`, `StateText` co trong DTO/entity nhung UI create khong gui, nen thuong null.
12. `MatchesCount`, `RegisteredCount`, `PairedCount` la field entity nhung nhieu public API tinh lai count tu registrations/matches; can ro rang day la cache hay legacy.

## 20. Checklist Khi Tao Giai Moi

1. Tao tournament o trang `/Home/Tournaments`.
2. Chon dung `Noi dung thi dau`; day quyet dinh single/double va gioi tinh.
3. Neu chua muon public thay, de `DRAFT`.
4. Neu muon mobile/web thay va cho dang ky, chon `OPEN`, dat `RegisterDeadline` hop ly.
5. Set `ExpectedTeams` dung so team success toi da.
6. Set `SingleLimit`/`DoubleLimit`; de `0` neu khong gioi han.
7. Upload banner neu can hien thi dep tren public/mobile.
8. Sau khi tao, vao dang ky/vong dau de tiep tuc setup:
   - registrations
   - round maps
   - groups
   - matches
   - prizes

## 21. Tom Tat Mot Cau

Tao giai dau tren `/Home/Tournaments` la buoc khai bao metadata va rule cua tournament; no quyet dinh viec public/mobile co thay giai hay khong, user co duoc dang ky hay khong, registration tinh single/double ra sao, nhung khong tu sinh cac buoc van hanh giai nhu danh sach VDV, bang dau, lich dau, bracket hay giai thuong.
