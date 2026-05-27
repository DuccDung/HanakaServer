# Task Trien Khai Nghiep Vu Bracket/Playoff Mapping Tran Dau

Muc tieu: cho phep admin setup truoc cac tran vong sau theo nguon doi, vi du:

- Winner tran A gap Winner tran B.
- Loser tran A gap Loser tran B.
- Hang 1 bang A gap hang 2 bang B.
- Vong bang xoay vong tinh BXH roi map doi vao vong trong.

Trang/luong hien tai dang anh huong:

- Tao giai: `/Home/Tournaments`
- Tao round: `/TournamentRounds/Index?tournamentId={id}`
- Tao group: `/TournamentRoundGroups/Index?roundMapId={id}`
- Tao match: `/TournamentGroupMatches/Index?groupId={id}`
- Admin match API: `AdminTournamentGroupMatchesController`
- Public/client schedule/standings: `TournamentClientController`, `PublicTournamentsController`
- Referee score: `RefereeMatchesApiController`

## 0. Hien Trang Can Nho

Model hien tai `TournamentGroupMatch` chi luu doi that:

```csharp
public long Team1RegistrationId { get; set; }
public long Team2RegistrationId { get; set; }
public long? WinnerRegistrationId { get; set; }
```

He qua:

- Khong tao duoc tran "TBD vs TBD".
- Khong tao duoc tran "Winner Match A vs Winner Match B" truoc khi A/B ket thuc.
- Khong tao duoc tran "Top 1 bang A vs Top 2 bang B" truoc khi vong bang xong.
- Khi nhap diem tran truoc, chua co co che tu dong day winner/loser vao tran sau.

Huong moi: moi slot doi trong tran co the la:

- doi cu the (`REGISTRATION`)
- winner cua tran khac (`WINNER_MATCH`)
- loser cua tran khac (`LOSER_MATCH`)
- hang N cua bang/vong bang (`GROUP_RANK`)

## 1. Pham Vi Trien Khai

### 1.1 MVP Bat Buoc

- Cho phep tao tran voi Team1/Team2 chua co doi that.
- Cho phep Team slot lay nguon tu winner/loser cua tran truoc.
- Cho phep Team slot lay nguon tu hang N cua mot group.
- Khi tran truoc hoan tat, tu map winner/loser vao cac tran sau.
- Khi group vong bang hoan tat, tinh standings va map hang N vao cac tran sau.
- UI admin tao/sua tran ho tro chon source type.
- Public/referee hien thi duoc `TBD` hoac mo ta source neu doi chua resolve.

### 1.2 Khong Lam Trong MVP

- Khong can auto generate toan bo bracket tu so doi.
- Khong can drag/drop bracket UI.
- Khong can double elimination phuc tap.
- Khong can seed/random boc tham tu dong.
- Khong can lich thi dau toi uu theo san/gio.

## 2. Thiet Ke Du Lieu

### 2.1 Them Enum Dang String

Dung string de de debug va khop style hien tai.

```text
REGISTRATION
WINNER_MATCH
LOSER_MATCH
GROUP_RANK
BYE
```

Y nghia:

- `REGISTRATION`: slot da chon doi cu the.
- `WINNER_MATCH`: slot lay winner cua 1 match truoc.
- `LOSER_MATCH`: slot lay loser cua 1 match truoc.
- `GROUP_RANK`: slot lay hang N trong 1 group.
- `BYE`: slot duoc mien dau, co the de sau neu can.

Trong MVP co the chua expose `BYE` tren UI, nhung nen de san neu lam bracket le doi.

### 2.2 Sua Bang `TournamentGroupMatches`

Hien tai `Team1RegistrationId`, `Team2RegistrationId` dang required. Can doi thanh nullable.

Them cac cot:

```sql
ALTER TABLE dbo.TournamentGroupMatches
ALTER COLUMN Team1RegistrationId BIGINT NULL;

ALTER TABLE dbo.TournamentGroupMatches
ALTER COLUMN Team2RegistrationId BIGINT NULL;

ALTER TABLE dbo.TournamentGroupMatches
ADD Team1SourceType VARCHAR(30) NOT NULL
    CONSTRAINT DF_TGM_Team1SourceType DEFAULT ('REGISTRATION');

ALTER TABLE dbo.TournamentGroupMatches
ADD Team1SourceMatchId BIGINT NULL;

ALTER TABLE dbo.TournamentGroupMatches
ADD Team1SourceGroupId BIGINT NULL;

ALTER TABLE dbo.TournamentGroupMatches
ADD Team1SourceRank INT NULL;

ALTER TABLE dbo.TournamentGroupMatches
ADD Team2SourceType VARCHAR(30) NOT NULL
    CONSTRAINT DF_TGM_Team2SourceType DEFAULT ('REGISTRATION');

ALTER TABLE dbo.TournamentGroupMatches
ADD Team2SourceMatchId BIGINT NULL;

ALTER TABLE dbo.TournamentGroupMatches
ADD Team2SourceGroupId BIGINT NULL;

ALTER TABLE dbo.TournamentGroupMatches
ADD Team2SourceRank INT NULL;
```

Them constraint:

```sql
ALTER TABLE dbo.TournamentGroupMatches
ADD CONSTRAINT CK_TGM_Team1SourceType
CHECK (Team1SourceType IN ('REGISTRATION','WINNER_MATCH','LOSER_MATCH','GROUP_RANK','BYE'));

ALTER TABLE dbo.TournamentGroupMatches
ADD CONSTRAINT CK_TGM_Team2SourceType
CHECK (Team2SourceType IN ('REGISTRATION','WINNER_MATCH','LOSER_MATCH','GROUP_RANK','BYE'));
```

Sua constraint doi khac nhau:

```sql
-- Neu constraint cu CK_TGM_TeamsDifferent dang ton tai thi drop truoc.
ALTER TABLE dbo.TournamentGroupMatches
DROP CONSTRAINT CK_TGM_TeamsDifferent;

ALTER TABLE dbo.TournamentGroupMatches
ADD CONSTRAINT CK_TGM_TeamsDifferent
CHECK (
    Team1RegistrationId IS NULL
    OR Team2RegistrationId IS NULL
    OR Team1RegistrationId <> Team2RegistrationId
);
```

Them FK optional:

```sql
ALTER TABLE dbo.TournamentGroupMatches
ADD CONSTRAINT FK_TGM_Team1SourceMatch
FOREIGN KEY (Team1SourceMatchId) REFERENCES dbo.TournamentGroupMatches(MatchId);

ALTER TABLE dbo.TournamentGroupMatches
ADD CONSTRAINT FK_TGM_Team2SourceMatch
FOREIGN KEY (Team2SourceMatchId) REFERENCES dbo.TournamentGroupMatches(MatchId);

ALTER TABLE dbo.TournamentGroupMatches
ADD CONSTRAINT FK_TGM_Team1SourceGroup
FOREIGN KEY (Team1SourceGroupId) REFERENCES dbo.TournamentRoundGroups(TournamentRoundGroupId);

ALTER TABLE dbo.TournamentGroupMatches
ADD CONSTRAINT FK_TGM_Team2SourceGroup
FOREIGN KEY (Team2SourceGroupId) REFERENCES dbo.TournamentRoundGroups(TournamentRoundGroupId);
```

Them index:

```sql
CREATE INDEX IX_TGM_Team1SourceMatch
ON dbo.TournamentGroupMatches(Team1SourceMatchId, Team1SourceType);

CREATE INDEX IX_TGM_Team2SourceMatch
ON dbo.TournamentGroupMatches(Team2SourceMatchId, Team2SourceType);

CREATE INDEX IX_TGM_Team1SourceGroup
ON dbo.TournamentGroupMatches(Team1SourceGroupId, Team1SourceType, Team1SourceRank);

CREATE INDEX IX_TGM_Team2SourceGroup
ON dbo.TournamentGroupMatches(Team2SourceGroupId, Team2SourceType, Team2SourceRank);
```

### 2.3 Data Migration Cho Tran Cu

Voi du lieu cu, cac tran da co `Team1RegistrationId` va `Team2RegistrationId` thi set source type la `REGISTRATION`.

```sql
UPDATE dbo.TournamentGroupMatches
SET
    Team1SourceType = 'REGISTRATION',
    Team2SourceType = 'REGISTRATION'
WHERE Team1SourceType IS NULL OR Team2SourceType IS NULL;
```

Neu DB dang co computed columns `TeamMin`, `TeamMax` dua tren `Team1RegistrationId/Team2RegistrationId`, can kiem tra lai vi khi nullable, computed column co the sai/khong hop le.

Huong sua:

```sql
-- Neu unique index dang dua vao TeamMin/TeamMax de chan cap trung,
-- phai cho phep chi enforce khi ca 2 doi deu co.
CREATE UNIQUE INDEX UX_TGM_Group_TeamPair
ON dbo.TournamentGroupMatches(TournamentRoundGroupId, TeamMin, TeamMax)
WHERE Team1RegistrationId IS NOT NULL AND Team2RegistrationId IS NOT NULL;
```

Neu computed column hien tai khong chap nhan null, can sua expression:

```sql
CASE
  WHEN Team1RegistrationId IS NULL OR Team2RegistrationId IS NULL THEN NULL
  WHEN Team1RegistrationId < Team2RegistrationId THEN Team1RegistrationId
  ELSE Team2RegistrationId
END
```

## 3. Cap Nhat Model Va DbContext

### 3.1 `Models/TournamentGroupMatch.cs`

Sua:

```csharp
public long? Team1RegistrationId { get; set; }
public long? Team2RegistrationId { get; set; }
```

Them:

```csharp
public string Team1SourceType { get; set; } = "REGISTRATION";
public long? Team1SourceMatchId { get; set; }
public long? Team1SourceGroupId { get; set; }
public int? Team1SourceRank { get; set; }

public string Team2SourceType { get; set; } = "REGISTRATION";
public long? Team2SourceMatchId { get; set; }
public long? Team2SourceGroupId { get; set; }
public int? Team2SourceRank { get; set; }
```

Navigation optional:

```csharp
public virtual TournamentGroupMatch? Team1SourceMatch { get; set; }
public virtual TournamentGroupMatch? Team2SourceMatch { get; set; }
public virtual TournamentRoundGroup? Team1SourceGroup { get; set; }
public virtual TournamentRoundGroup? Team2SourceGroup { get; set; }
```

Sua navigation doi that thanh nullable:

```csharp
public virtual TournamentRegistration? Team1Registration { get; set; }
public virtual TournamentRegistration? Team2Registration { get; set; }
```

### 3.2 `PickleballDbContext.cs`

Cap nhat mapping:

- `Team1RegistrationId` optional.
- `Team2RegistrationId` optional.
- Source type max length 30, unicode false, default `REGISTRATION`.
- FK source match/group optional, delete restrict.
- Constraint teams different voi nullable.
- Index source match/group.

Can kiem tra cac join/query hien tai vi nhieu doan dang inner join `Team1Registration/Team2Registration`.

## 4. DTO/API Contract Moi

### 4.1 Tao/Sua Match DTO

Sua `CreateMatchDto`:

```csharp
public class MatchSlotDto
{
    public string SourceType { get; set; } = "REGISTRATION";
    public long? RegistrationId { get; set; }
    public long? SourceMatchId { get; set; }
    public long? SourceGroupId { get; set; }
    public int? SourceRank { get; set; }
}

public class CreateMatchDto
{
    public MatchSlotDto Team1 { get; set; } = new();
    public MatchSlotDto Team2 { get; set; } = new();
    public DateTime? StartAt { get; set; }
    public string? AddressText { get; set; }
    public string? CourtText { get; set; }
    public string? VideoUrl { get; set; }
    public long? RefereeUserId { get; set; }
}
```

Sua `UpdateMatchDto` tuong tu:

```csharp
public MatchSlotDto? Team1 { get; set; }
public MatchSlotDto? Team2 { get; set; }
```

De backward compatible voi UI cu, co the tam thoi giu:

```csharp
public long? Team1RegistrationId { get; set; }
public long? Team2RegistrationId { get; set; }
```

Neu client gui field cu thi map thanh:

```text
SourceType = REGISTRATION
RegistrationId = old field
```

### 4.2 Response Match Item

List match can tra them:

```json
{
  "team1RegistrationId": 123,
  "team1Text": "Nguyen A & Tran B",
  "team1SourceType": "WINNER_MATCH",
  "team1SourceMatchId": 10,
  "team1SourceGroupId": null,
  "team1SourceRank": null,
  "team1SourceText": "Thang tran #10",
  "team1Resolved": true
}
```

Neu chua resolve:

```json
{
  "team1RegistrationId": null,
  "team1Text": "TBD",
  "team1SourceText": "Cho thang tran #10",
  "team1Resolved": false
}
```

## 5. Validate Slot

Tao helper:

```csharp
private async Task<(bool Ok, string? Message, MatchSlotResolved Slot)> ValidateSlotAsync(
    long tournamentId,
    long currentMatchId,
    MatchSlotDto dto,
    int slotNumber,
    CancellationToken ct)
```

Rules:

### 5.1 `REGISTRATION`

- `RegistrationId` required.
- Registration phai thuoc tournament.
- Registration phai `Success = true`.
- Set `TeamXRegistrationId = RegistrationId`.
- Clear source match/group/rank.

### 5.2 `WINNER_MATCH`

- `SourceMatchId` required.
- Source match phai thuoc cung tournament.
- Source match khong duoc la current match.
- Source match nen nam o round truoc, hoac it nhat khong tao cycle.
- Neu source match da completed va co winner thi resolve ngay:
  - `TeamXRegistrationId = source.WinnerRegistrationId`
- Neu chua completed:
  - `TeamXRegistrationId = null`
- Clear source group/rank.

### 5.3 `LOSER_MATCH`

- `SourceMatchId` required.
- Source match phai thuoc cung tournament.
- Source match khong duoc la current match.
- Neu source match da completed:
  - loser = doi con lai cua source match.
  - chi resolve neu source match co du 2 doi.
- Neu chua completed:
  - `TeamXRegistrationId = null`.
- Clear source group/rank.

### 5.4 `GROUP_RANK`

- `SourceGroupId` required.
- `SourceRank` required va >= 1.
- Group phai thuoc tournament.
- Tinh standings cua group.
- Neu group da du dieu kien complete:
  - Lay row rank N.
  - `TeamXRegistrationId = row.RegistrationId`.
- Neu group chua complete:
  - `TeamXRegistrationId = null`.
- Clear source match.

### 5.5 Chung Cho Hai Slot

- Neu ca 2 slot da resolve, registration id khong duoc trung.
- Neu match da completed thi khong cho doi source/team, chi cho sua lich/san/trong tai/video.
- Khong cho tao dependency cycle:
  - Match A source tu Match B.
  - Match B source nguoc tu Match A.
- Khong cho source match nam sau current match neu co `RoundMap.SortOrder`.

## 6. Service Tu Dong Map Doi

Tao service moi:

```text
HanakaServer/Service/TournamentBracketPropagationService.cs
```

Interface:

```csharp
public interface ITournamentBracketPropagationService
{
    Task PropagateFromMatchAsync(long matchId, CancellationToken ct = default);
    Task PropagateFromGroupAsync(long groupId, CancellationToken ct = default);
    Task RecalculateMatchSlotsAsync(long matchId, CancellationToken ct = default);
}
```

Dang ky DI trong `Program.cs`:

```csharp
builder.Services.AddScoped<ITournamentBracketPropagationService, TournamentBracketPropagationService>();
```

### 6.1 `PropagateFromMatchAsync`

Input: match vua cap nhat diem.

Steps:

1. Load source match.
2. Neu source match chua completed hoac chua co winner:
   - khong propagate winner/loser.
3. Tinh winner id:
   - `source.WinnerRegistrationId`.
4. Tinh loser id:
   - neu winner == Team1 => loser = Team2.
   - neu winner == Team2 => loser = Team1.
5. Tim tat ca target matches co:
   - `Team1SourceType IN ('WINNER_MATCH','LOSER_MATCH') && Team1SourceMatchId == source.MatchId`
   - hoac `Team2SourceType IN ('WINNER_MATCH','LOSER_MATCH') && Team2SourceMatchId == source.MatchId`
6. Voi tung target:
   - neu target da completed thi khong overwrite, ghi warning/log.
   - neu slot Team1 phu hop winner/loser thi set `Team1RegistrationId`.
   - neu slot Team2 phu hop winner/loser thi set `Team2RegistrationId`.
   - neu sau khi set hai slot bi trung doi thi bao loi/log va khong save target do.
7. Save changes.
8. Neu target vua du 2 doi, co the gui notification lich dau neu can.

Pseudo:

```csharp
public async Task PropagateFromMatchAsync(long matchId, CancellationToken ct)
{
    var source = await LoadMatchAsync(matchId, ct);
    if (!source.IsCompleted || source.WinnerRegistrationId == null) return;

    var winner = source.WinnerRegistrationId.Value;
    var loser = ResolveLoser(source);
    if (loser == null) return;

    var targets = await _db.TournamentGroupMatches
        .Where(x =>
            (x.Team1SourceMatchId == matchId && (x.Team1SourceType == "WINNER_MATCH" || x.Team1SourceType == "LOSER_MATCH")) ||
            (x.Team2SourceMatchId == matchId && (x.Team2SourceType == "WINNER_MATCH" || x.Team2SourceType == "LOSER_MATCH")))
        .ToListAsync(ct);

    foreach (var target in targets)
    {
        if (target.IsCompleted) continue;

        if (target.Team1SourceMatchId == matchId)
            target.Team1RegistrationId = target.Team1SourceType == "WINNER_MATCH" ? winner : loser;

        if (target.Team2SourceMatchId == matchId)
            target.Team2RegistrationId = target.Team2SourceType == "WINNER_MATCH" ? winner : loser;

        target.UpdatedAt = DateTime.UtcNow;
    }

    await _db.SaveChangesAsync(ct);
}
```

### 6.2 `PropagateFromGroupAsync`

Input: group vua co match completed/score thay doi.

Steps:

1. Kiem tra group co du match complete chua.
2. Tinh standings.
3. Tim target matches co:
   - `Team1SourceType = GROUP_RANK && Team1SourceGroupId = groupId`
   - hoac `Team2SourceType = GROUP_RANK && Team2SourceGroupId = groupId`
4. Voi tung target:
   - lay standings rank tuong ung.
   - set registration id vao slot.
   - khong overwrite neu target completed.
5. Save.

Can co mot helper standings dung chung voi `TournamentClientController` de tranh moi noi tinh mot kieu.

### 6.3 Khi Score Thay Doi Sau Khi Da Propagate

Scenario: Tran A da xong, winner map vao C. Sau do admin sua diem A lam winner doi.

MVP rule de don gian:

- Neu target match C chua completed:
  - cho update slot da resolve theo winner moi.
- Neu target match C da completed:
  - khong tu overwrite.
  - tra warning trong response hoac log.
  - UI can hien canh bao "Tran sau da dau, khong tu cap nhat".

## 7. Cap Nhat Controller

### 7.1 `AdminTournamentGroupMatchesController.Create`

Viec can lam:

- Thay validate bat buoc `Team1RegistrationId > 0` va `Team2RegistrationId > 0`.
- Validate `Team1` va `Team2` theo source type.
- Cho phep registration id null neu source chua resolve.
- Tao `TournamentGroupMatch` voi source fields.

Cu:

```csharp
if (dto.Team1RegistrationId <= 0 || dto.Team2RegistrationId <= 0)
    return BadRequest(...);
```

Moi:

```csharp
var team1 = await ValidateSlotAsync(rm.TournamentId, 0, dto.Team1, 1, ct);
var team2 = await ValidateSlotAsync(rm.TournamentId, 0, dto.Team2, 2, ct);
```

### 7.2 `AdminTournamentGroupMatchesController.Update`

- Neu match completed: giu rule chi cho sua lich/san/trong tai/video.
- Neu chua completed:
  - cho sua source slot.
  - neu source doi tu `WINNER_MATCH` sang `REGISTRATION`, clear source match/group/rank.
  - validate khong trung doi neu ca 2 slot resolved.

### 7.3 `AdminTournamentGroupMatchesController.SetScore`

Sau khi save score va commit:

```csharp
if (m.IsCompleted)
{
    await _bracketPropagationService.PropagateFromMatchAsync(m.MatchId);
    await _bracketPropagationService.PropagateFromGroupAsync(groupId);
}
```

Can inject service vao controller.

Thu tu khuyen nghi:

1. Save score trong transaction.
2. Commit.
3. Goi propagation trong transaction rieng hoac service tu quan ly transaction.
4. Notification khong duoc lam fail viec save score.

### 7.4 `RefereeMatchesApiController`

Referee cung co API cap nhat score. Can them propagation sau khi referee submit diem thanh cong.

Task:

- Tim method update score trong `RefereeMatchesApiController`.
- Inject `ITournamentBracketPropagationService`.
- Sau khi set completed/winner, goi:

```csharp
await _bracketPropagationService.PropagateFromMatchAsync(matchId);
await _bracketPropagationService.PropagateFromGroupAsync(match.TournamentRoundGroupId);
```

## 8. Cap Nhat List/Detail Match

Nhung noi dang inner join registration can doi thanh left join.

### 8.1 Admin Match List

Hien tai:

```csharp
join r1 in _db.TournamentRegistrations on m.Team1RegistrationId equals r1.RegistrationId
join r2 in _db.TournamentRegistrations on m.Team2RegistrationId equals r2.RegistrationId
```

Can doi thanh left join de match TBD van hien.

Team text logic:

```csharp
Team1Text = r1 != null
    ? BuildTeamText(...)
    : BuildSourceText(m.Team1SourceType, m.Team1SourceMatchId, m.Team1SourceGroupId, m.Team1SourceRank)
```

Vi du output:

- `Cho thang tran #12`
- `Cho thua tran #13`
- `Cho hang 1 bang A`
- `TBD`

### 8.2 Public/Client Schedule

Can search va sua tat ca query dang join `TournamentGroupMatches` voi `TournamentRegistrations`.

Files can kiem tra:

- `TournamentClientController.cs`
- `PublicTournamentsController.cs`
- `RefereeMatchesApiController.cs`
- `RefereePortalController.cs` neu co query rieng
- Views/JS hien match

Rule:

- Public schedule phai hien match TBD.
- Neu doi chua resolve, khong crash.
- Neu match chua du 2 doi, khong cho referee cham diem.

## 9. Tinh Standings Cho `GROUP_RANK`

Hien tai da co standings trong `TournamentClientController`.

Task:

1. Tach logic tinh standings thanh service dung chung:

```text
HanakaServer/Service/TournamentStandingsService.cs
```

Interface:

```csharp
public interface ITournamentStandingsService
{
    Task<IReadOnlyList<GroupStandingRow>> GetGroupStandingsAsync(long groupId, CancellationToken ct = default);
    Task<bool> IsGroupCompletedAsync(long groupId, CancellationToken ct = default);
}
```

2. Row can co:

```csharp
public sealed class GroupStandingRow
{
    public long RegistrationId { get; set; }
    public int Rank { get; set; }
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Points { get; set; }
    public int ScoreFor { get; set; }
    public int ScoreAgainst { get; set; }
    public int ScoreDiff { get; set; }
}
```

3. Tie-break rule can ro rang:

MVP khuyen nghi:

- wins desc
- points desc neu co
- score diff desc
- score for desc
- registration id asc

4. `GROUP_RANK` chi resolve khi group completed.

Group completed khi:

- group co it nhat 1 match.
- tat ca match trong group da `IsCompleted = true`.
- tat ca match co winner.

Can chu y:

- Neu group co match TBD thi group chua completed.
- Neu admin sua/xoa match trong group, propagation co the can recalc lai target slots.

## 10. UI Admin Tao/Sua Tran

File chinh:

- `Views/TournamentGroupMatches/Index.cshtml`

Task UI:

### 10.1 Doi Form Chon Doi Thanh Chon Source Slot

Moi slot Team 1/Team 2 co:

- Select `SourceType`:
  - Doi cu the
  - Thang tran
  - Thua tran
  - Hang bang
- Neu `Doi cu the`:
  - hien picker/search registration.
- Neu `Thang tran`:
  - hien picker match nguon.
- Neu `Thua tran`:
  - hien picker match nguon.
- Neu `Hang bang`:
  - hien picker group + input rank.

### 10.2 API Ho Tro Picker

Hien controller da co:

- `GET /api/admin/groups/{groupId}/matches/winner-sources`
- `GET /api/admin/groups/{groupId}/matches/loser-sources`

Task:

- Kiem tra API nay co du data cho picker khong.
- Neu chua du, them endpoint tong quat:

```http
GET /api/admin/groups/{groupId}/matches/source-options
```

Tra ve:

```json
{
  "current": {},
  "previousRounds": [
    {
      "roundMapId": 1,
      "roundLabel": "Vong bang",
      "groups": [
        {
          "groupId": 10,
          "groupName": "Bang A",
          "matches": [
            { "matchId": 100, "label": "Tran #100 - A vs B", "isCompleted": true }
          ]
        }
      ]
    }
  ]
}
```

### 10.3 Hien Thi Match TBD

Trong table:

- Neu resolved: hien ten doi.
- Neu chua resolved: hien source text + badge `TBD`.

Vi du:

```text
Team 1: Cho thang tran #12
Team 2: Cho hang 1 bang A
```

### 10.4 Disable Score Khi Chua Du Doi

Nut nhap diem chi enable khi:

- `Team1RegistrationId != null`
- `Team2RegistrationId != null`
- Team1 != Team2

Neu chua du:

- Hien tooltip/message: `Tran chua xac dinh du 2 doi`.

## 11. UI Bracket/Admin Diagram

File can doc/sua:

- `TournamentAdminBracketController.cs`
- `wwwroot/js/admin-tournament-bracket.js`
- `wwwroot/css/admin-tournament-bracket.css`

Task:

- Bracket node hien source text khi doi TBD.
- Neu slot resolved, hien ten doi.
- Neu slot source la winner/loser, co the hien label nho:
  - `W#12`
  - `L#12`
  - `A1`
  - `B2`
- Khi score update va propagation xong, reload bracket de thay doi.

## 12. Public/Client Impact

Task:

- `TournamentClientController.GetRoundsWithMatches` phai tra match TBD.
- `TournamentClientController.GetMatchDetail` phai xu ly doi null.
- Public web/mobile schedule khong crash khi team null.
- Standings khong tinh match chua co du 2 doi.
- Referee upcoming matches khong hien match chua du doi, hoac hien nhung khong cho cham diem.

Rule khuyen nghi:

- Public schedule: hien tat ca tran, TBD neu chua resolve.
- Referee match list: chi hien tran da co du 2 doi, hoac hien disabled neu can.

## 13. Edge Cases Can Xu Ly

### 13.1 Tran Sau Da Dau Roi Ma Tran Truoc Doi Ket Qua

Rule MVP:

- Neu target match da completed: khong overwrite.
- Ghi warning/log.
- UI admin can hien canh bao khi edit score tran nguon.

### 13.2 Source Match Chua Co Du Doi

- Cho phep tao dependency.
- Target slot van TBD.
- Khi source match sau nay co du doi va completed, propagation binh thuong.

### 13.3 Match Source Bi Xoa

Neu match A dang la source cua match C:

- Khong cho xoa A.
- Hoac cho xoa nhung clear source cua C.

Khuyen nghi MVP: khong cho xoa.

Them check trong `Delete`:

```csharp
var usedAsSource = await _db.TournamentGroupMatches.AnyAsync(x =>
    x.Team1SourceMatchId == matchId || x.Team2SourceMatchId == matchId);

if (usedAsSource)
    return BadRequest(new { message = "Tran nay dang duoc dung lam nguon cho tran sau." });
```

### 13.4 Group Source Bi Xoa

Neu group A dang la source rank cua match C:

- Khong cho xoa group A.

Them check trong `AdminTournamentRoundGroupsController.Delete`.

### 13.5 Duplicate Team Pair

Hien co logic/index chan cap doi trung trong group. Sau khi nullable:

- Chi enforce khi ca 2 doi da resolve.
- Khi propagation lam target resolve xong, can catch duplicate.

### 13.6 Cycle Dependency

Khong cho:

- Match C lay winner tu Match D.
- Match D lai lay winner tu Match C.

MVP check toi thieu:

- Source match khong duoc la current match.
- Source match phai nam o round co `SortOrder` nho hon current round.

Neu can chac hon, viet DFS de detect cycle.

## 14. Thu Tu Trien Khai De Giam Rui Ro

### Phase 1 - Schema va Model

- [ ] Backup DB.
- [ ] Viet SQL alter table cho `TournamentGroupMatches`.
- [ ] Doi `Team1RegistrationId`, `Team2RegistrationId` thanh nullable.
- [ ] Them source fields.
- [ ] Sua constraints/indexes.
- [ ] Cap nhat `TournamentGroupMatch.cs`.
- [ ] Cap nhat `PickleballDbContext.cs`.
- [ ] Build project.

### Phase 2 - API Co Ban Cho Match TBD

- [ ] Sua `CreateMatchDto`, `UpdateMatchDto`.
- [ ] Sua `Create` de cho slot nullable.
- [ ] Sua `Update` de cho sua source slot.
- [ ] Sua `List` sang left join registrations.
- [ ] Build source text helper.
- [ ] Dam bao admin tao duoc match TBD.
- [ ] Dam bao match list khong crash.

### Phase 3 - Propagation Winner/Loser

- [ ] Tao `ITournamentBracketPropagationService`.
- [ ] Implement `PropagateFromMatchAsync`.
- [ ] Inject vao `AdminTournamentGroupMatchesController`.
- [ ] Goi sau `SetScore`.
- [ ] Inject vao `RefereeMatchesApiController`.
- [ ] Goi sau referee submit score.
- [ ] Test Winner A -> Match C.
- [ ] Test Loser A -> Match D.
- [ ] Test sua ket qua tran A khi C chua dau.
- [ ] Test sua ket qua tran A khi C da dau, khong overwrite.

### Phase 4 - Group Rank

- [ ] Tach standings logic thanh `TournamentStandingsService`.
- [ ] Implement `IsGroupCompletedAsync`.
- [ ] Implement `PropagateFromGroupAsync`.
- [ ] Goi sau match completed.
- [ ] UI/API cho chon source `GROUP_RANK`.
- [ ] Test bang xoay vong A co rank 1/2 map vao playoff.

### Phase 5 - UI Admin

- [ ] Sua form tao/sua match trong `Views/TournamentGroupMatches/Index.cshtml`.
- [ ] Them source type select cho Team1/Team2.
- [ ] Them picker registration/match/group rank.
- [ ] Disable score khi chua du doi.
- [ ] Hien source text/TBD trong table.
- [ ] Hien canh bao khi source chua resolve.
- [ ] Kiem tra mobile viewport neu view admin co responsive.

### Phase 6 - Public/Referee/Bracket

- [ ] Sua public/client schedule query sang left join.
- [ ] Sua match detail de team nullable.
- [ ] Sua referee list/score guard.
- [ ] Sua bracket JS hien TBD/source label.
- [ ] Kiem tra notifications khong gui sai khi match chua du doi.

### Phase 7 - Hardening

- [ ] Chan xoa match dang la source.
- [ ] Chan xoa group dang la source.
- [ ] Chan dependency nguoc/cycle.
- [ ] Them log warning khi target completed nen khong overwrite.
- [ ] Them transaction hop ly cho propagation.
- [ ] Them unit/integration tests neu project test duoc setup.

## 15. Test Cases Chi Tiet

### 15.1 Playoff Co Ban

Setup:

- Match A: Team 1 vs Team 2.
- Match B: Team 3 vs Team 4.
- Match C:
  - Team1 = WINNER_MATCH A.
  - Team2 = WINNER_MATCH B.

Expected:

- Truoc khi A/B xong, C hien `TBD`.
- A xong, C Team1 duoc dien winner A.
- B xong, C Team2 duoc dien winner B.
- Khi C du 2 doi, cho phep nhap diem.

### 15.2 Tranh Hang Ba

Setup:

- Match D:
  - Team1 = LOSER_MATCH A.
  - Team2 = LOSER_MATCH B.

Expected:

- A/B xong thi D co 2 doi thua.

### 15.3 Vong Bang Len Ban Ket

Setup:

- Bang A co cac match round robin.
- Bang B co cac match round robin.
- Ban ket 1:
  - Team1 = GROUP_RANK Bang A rank 1.
  - Team2 = GROUP_RANK Bang B rank 2.
- Ban ket 2:
  - Team1 = GROUP_RANK Bang B rank 1.
  - Team2 = GROUP_RANK Bang A rank 2.

Expected:

- Khi bang chua complete, ban ket hien TBD.
- Khi bang A complete, cac slot tu bang A duoc fill.
- Khi bang B complete, cac slot tu bang B duoc fill.

### 15.4 Sua Ket Qua Tran Nguon

Setup:

- A da xong, winner map vao C.
- C chua completed.
- Sua diem A de doi winner.

Expected:

- C slot tu A doi sang winner moi.

### 15.5 Sua Ket Qua Khi Tran Sau Da Completed

Setup:

- A da xong, C da dau xong.
- Sua diem A.

Expected:

- C khong bi overwrite.
- API/log co warning.
- UI admin can co canh bao.

### 15.6 Xoa Match Dang La Source

Setup:

- C Team1 source tu A.
- Thu xoa A.

Expected:

- API tra BadRequest.
- Message: tran dang duoc dung lam nguon cho tran sau.

### 15.7 Duplicate Sau Propagation

Setup loi:

- C Team1 = WINNER A.
- C Team2 = WINNER B.
- Do setup sai, A va B cung resolve ra cung 1 registration.

Expected:

- Khong save target thanh 2 doi trung nhau.
- API/log bao loi de admin sua setup.

## 16. Goi Y SQL Migration Day Du Can Viet Rieng

Viec can lam can than vi repo hien khong co EF Migrations.

Task:

- [ ] Tao file SQL rieng, vi du:

```text
database/migrations/2026xxxx_add_match_sources.sql
```

- [ ] Chay tren local DB truoc.
- [ ] Verify bang:

```sql
SELECT TOP 10 *
FROM dbo.TournamentGroupMatches
ORDER BY MatchId DESC;
```

- [ ] Verify create old style registration match van hoat dong.
- [ ] Verify create TBD match hoat dong.

## 17. Goi Y Response Warning

Khi propagation co warning, co the tra:

```json
{
  "matchId": 10,
  "scoreTeam1": 11,
  "scoreTeam2": 8,
  "isCompleted": true,
  "winnerRegistrationId": 1001,
  "warnings": [
    "Tran #20 da hoan tat nen khong tu cap nhat doi tu tran #10."
  ]
}
```

MVP co the log truoc, response warning lam sau.

## 18. Naming De Dong Nhat

Khuyen nghi constants:

```csharp
public static class MatchSourceTypes
{
    public const string Registration = "REGISTRATION";
    public const string WinnerMatch = "WINNER_MATCH";
    public const string LoserMatch = "LOSER_MATCH";
    public const string GroupRank = "GROUP_RANK";
    public const string Bye = "BYE";
}
```

Dat file:

```text
HanakaServer/Helpers/MatchSourceTypes.cs
```

## 19. Acceptance Criteria

Tinh nang duoc coi la xong khi:

- Admin tao duoc tran playoff vong sau khi chua biet doi.
- Admin map slot theo winner/loser match truoc.
- Admin map slot theo hang N cua group.
- Nhap diem tran truoc tu dong dien doi vao tran sau.
- Vong bang complete tu dong dien doi vao playoff theo rank.
- Public schedule hien TBD/source text, khong crash.
- Referee khong cham diem duoc tran chua du 2 doi.
- Khong overwrite tran sau da completed khi ket qua tran nguon thay doi.
- Khong xoa duoc match/group dang duoc dung lam source.

## 20. Thu Tu Dev De Lam Ngay

1. Lam schema/model nullable + source fields.
2. Sua admin match list de hien duoc TBD.
3. Sua create/update match de luu source.
4. Lam propagation winner/loser.
5. Lam UI admin chon winner/loser.
6. Tach standings service.
7. Lam propagation group rank.
8. Sua public/referee/bracket.
9. Hardening delete/cycle/overwrite.

