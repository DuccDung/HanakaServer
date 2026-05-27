# Rule Khong Lam Anh Huong App Mobile

Tai lieu nay duoc lap sau khi doc app mobile tai:

```text
D:\laptrinhweb\code_outsrc\Hanaka_Sport\hanaka-sport\src\services
```

Muc tieu: khi sua backend `HanakaServer`, dac biet task bracket/playoff trong `Task.md`, duoc phep sua code backend nhung khong duoc lam vo contract ma app mobile dang goi.

## 1. Nguyen Tac Bat Buoc

1. Khong xoa, doi ten, doi method HTTP hoac doi path cua endpoint app dang goi.
2. Khong doi ten field response cu neu app dang doc field do.
3. Chi them field moi vao response, khong thay field cu bang field moi.
4. Khong doi kieu du lieu cua field cu:
   - number van la number hoac null neu truoc do da co the null.
   - string van la string hoac null.
   - object/list giu cung shape.
5. Neu backend can ho tro nghiep vu moi, tao field moi song song voi field cu.
6. Neu match chua co doi that, public API van phai tra match do va khong duoc throw 500.
7. Neu mot field cu khong co gia tri that, tra fallback an toan de app render duoc.
8. Loi validation moi khong duoc lam fail cac flow cu dang hop le.
9. Sau moi thay doi backend lien quan API public/mobile, can build backend va kiem tra cac endpoint mobile quan trong.

## 2. API Client Cua App

App dung `axios` trong `src/services/apiClient.js`.

- Base URL REST: `${API_BASE_URL}/api`
- Production hien tai: `https://hanakasport.click/api`
- Timeout: `15000ms`
- JWT duoc tu dong gan vao header `Authorization: Bearer {token}`, tru khi request co `skipAuth: true`.
- Neu response `401`, app se clear auth session va realtime state.

Quy tac backend:

- Khong tra `401` cho endpoint public/anonymous dang duoc app goi neu truoc do khong can login.
- Voi endpoint can JWT, giu co che Bearer token hien tai.
- Response loi nen co `message` de app hien thi duoc.

## 3. Endpoint App Dang Goi

### Auth

- `POST /api/auths/register`
- `POST /api/auths/confirm-otp`
- `POST /api/auths/resend-otp`
- `POST /api/auths/forgot-password`
- `POST /api/auths/forgot-password/verify-otp`
- `POST /api/auths/forgot-password/reset`
- `POST /api/auths/login`
- `POST /api/auths/logout`

Khong doi response login/confirm/reset: app can `accessToken`, `expiresAtUtc`, `user`.

### User

- `GET /api/users/me`
- `PUT /api/users/me`
- `POST /api/users/me/avatar`
- `POST /api/users/me/change-password`
- `GET /api/users/members`
- `GET /api/users/{userId}`
- `GET /api/users/{userId}/rating-history`
- `GET /api/users/me/rating-history`
- `PUT /api/users/me/self-rating`
- `DELETE /api/users/me`
- `GET /api/users/{userId}/achievements`
- `GET /api/users/me/achievements`
- `GET /api/users`

### Tournament

- `GET /api/admin/tournaments`
- `GET /api/admin/tournaments/{id}`
- `POST /api/admin/tournaments`
- `PUT /api/admin/tournaments/{id}`
- `GET /api/public/tournaments`
- `GET /api/public/tournaments/{id}`
- `GET /api/public/tournaments/{id}/registrations`
- `GET /api/tournaments/{id}`
- `GET /api/tournaments/{tournamentId}/rounds-with-matches`
- `GET /api/tournaments/{tournamentId}/round-maps/{roundMapId}/standings`
- `GET /api/tournaments/{tournamentId}/rule`
- `GET /api/tournament-registrations/tournaments/{tournamentId}/me`
- `GET /api/tournament-registrations/tournaments/{tournamentId}/partner-search`
- `POST /api/tournament-registrations/tournaments/{tournamentId}/single`
- `POST /api/tournament-registrations/tournaments/{tournamentId}/waiting`
- `POST /api/tournament-registrations/tournaments/{tournamentId}/pair-requests`
- `GET /api/tournament-registrations/pair-requests`
- `GET /api/tournament-registrations/pair-requests/{pairRequestId}`
- `POST /api/tournament-registrations/pair-requests/{pairRequestId}/accept`
- `POST /api/tournament-registrations/pair-requests/{pairRequestId}/reject`
- `POST /api/tournament-registrations/pair-requests/{pairRequestId}/cancel`
- `GET /api/notifications/pair-requests`

### Schedule/Standings Contract Quan Trong

`TournamentScheduleScreen` doc response tu:

```http
GET /api/tournaments/{tournamentId}/rounds-with-matches
```

Shape app dang can:

```json
{
  "tournament": {
    "tournamentId": 1,
    "title": "...",
    "playoffType": "...",
    "expectedTeams": 16,
    "matchesCount": 10
  },
  "rounds": [
    {
      "tournamentRoundMapId": 1,
      "roundKey": "GROUP",
      "roundLabel": "Vong bang",
      "sortOrder": 1,
      "groups": [
        {
          "groupId": 1,
          "groupName": "A",
          "matches": [
            {
              "matchId": 100,
              "startAt": "2026-01-01T08:00:00",
              "addressText": "...",
              "courtText": "...",
              "team1RegistrationId": 10,
              "team2RegistrationId": 11,
              "team1": { "displayName": "Doi A" },
              "team2": { "displayName": "Doi B" },
              "scoreTeam1": 11,
              "scoreTeam2": 8,
              "isCompleted": true,
              "winnerRegistrationId": 10,
              "winner": {},
              "videoUrl": "..."
            }
          ]
        }
      ]
    }
  ]
}
```

Quy tac khi lam match TBD:

- Van tra match TBD trong `groups[].matches[]`.
- `team1RegistrationId` va `team2RegistrationId` duoc phep la `null`.
- Van phai tra `team1` va `team2` object de app khong can sua.
- Neu slot chua resolve:

```json
{
  "team1RegistrationId": null,
  "team1": {
    "displayName": "Chua xac dinh"
  },
  "team1Text": "Chua xac dinh",
  "team1SourceText": "Cho thang tran #12",
  "team1Resolved": false
}
```

- Neu slot da resolve, giu `team1.displayName`/`team2.displayName` la ten doi nhu cu.
- Field moi nhu `team1SourceType`, `team1SourceMatchId`, `team1SourceGroupId`, `team1SourceRank`, `team1SourceText`, `team1Resolved` chi duoc them, khong thay field cu.
- `scoreTeam1`/`scoreTeam2` neu chua co diem nen la `null`, app da fallback thanh `-`.
- `winnerRegistrationId` neu chua co winner nen la `null`.
- Khong set `isCompleted = true` cho match chua du 2 doi.

`TournamentStandingsScreen` doc response tu:

```http
GET /api/tournaments/{tournamentId}/round-maps/{roundMapId}/standings
```

Shape app dang can:

```json
{
  "groups": [
    {
      "groupId": 1,
      "groupName": "A",
      "rows": [
        {
          "registrationId": 10,
          "teamName": "Doi A",
          "played": 3,
          "wins": 2,
          "points": 2,
          "scoreDiff": 10,
          "scoreFor": 33,
          "scoreAgainst": 23,
          "rank": 1
        }
      ]
    }
  ]
}
```

Quy tac:

- Khong tinh match TBD vao standings.
- Khong tinh match chua completed/chua co winner vao standings.
- Khong doi ten field `teamName`, `wins`, `points`, `scoreDiff`, `rank`.

### Notification

- `GET /api/notifications/upcoming-matches`
- `GET /api/notifications/pair-requests`

Upcoming match notification app doc:

```json
{
  "items": [
    {
      "id": 1,
      "title": "...",
      "message": "...",
      "opponentTeam": {
        "player1": { "name": "..." },
        "player2": { "name": "..." }
      },
      "match": {
        "startAt": "...",
        "startAtText": "...",
        "addressText": "...",
        "courtText": "..."
      }
    }
  ]
}
```

Quy tac:

- Khong gui notification upcoming cho match chua du 2 doi, tru khi response co fallback day du.
- Neu doi chua xac dinh, `opponentTeam` co the null/empty, app se hien "Chua xac dinh".

### Club/Chat/Realtime

- `GET /api/clubs/chat-rooms`
- `GET /api/clubs/{clubId}/messages`
- `POST /api/clubs/{clubId}/messages`
- `DELETE /api/clubs/{clubId}/messages/{messageId}`
- `POST /api/clubs/message-media`
- `POST /api/clubs/cover`
- `POST /api/clubs`
- `GET /api/clubs/my`
- `GET /api/clubs/{clubId}`
- `GET /api/clubs`
- `POST /api/clubs/{clubId}/join`
- `GET /api/clubs/{clubId}/overview`
- `GET /api/clubs/{clubId}/members`
- `GET /api/clubs/{clubId}/pending-members`
- `POST /api/clubs/{clubId}/pending-members/{userId}/approve`
- `DELETE /api/clubs/{clubId}/pending-members/{userId}`
- `DELETE /api/clubs/{clubId}/members/{userId}`
- `PUT /api/clubs/{clubId}/members/{userId}/role`
- `PUT /api/clubs/{clubId}/challenge-mode`
- `GET /api/clubs/challenging`
- WebSocket: `/ws`

Khong doi message realtime event names dang dung:

- `club.subscribe`
- `club.unsubscribe`
- `club.typing`
- server push club notifications/chat events neu dang co.

### Public Content

- `GET /api/public/banners`
- `GET /api/public/courts`
- `GET /api/public/courts/{courtId}`
- `GET /api/links`
- `GET /api/coaches`
- `GET /api/coaches/{coachId}`
- `GET /api/coaches/me`
- `POST /api/coaches/register-me`
- `PUT /api/coaches/me/profile`
- `GET /api/referees`
- `GET /api/referees/{refereeId}`
- `GET /api/referees/me`
- `POST /api/referees/register-me`
- `PUT /api/referees/me/profile`
- `GET /api/videos/videos`
- `GET /api/videos/users/{userId}/videos`

## 4. Rule Rieng Cho Task Bracket/Playoff

1. DB co the doi `Team1RegistrationId`/`Team2RegistrationId` sang nullable, nhung API response public/mobile phai co fallback.
2. Admin API co the nhan DTO moi `Team1`/`Team2`, nhung nen giu backward compatible voi `team1RegistrationId`/`team2RegistrationId`.
3. Public schedule phai hien match TBD, khong an match.
4. Referee/mobile scoring khong duoc cham diem match chua du 2 doi.
5. Notification lich dau khong duoc gui sai cho match TBD.
6. Standings khong tinh match TBD.
7. Video/history endpoints chi nen hien match da co doi that va co du lieu hop le, hoac phai co fallback ten doi.
8. Khi them source fields, dat default:
   - source type: `REGISTRATION`
   - source ids/rank: `null`
   - source text: `null` hoac mo ta ngan gon
   - resolved: `true` neu registration id co gia tri, `false` neu null

## 5. Checklist Truoc Khi Ket Luan Backend Khong Anh Huong App

Sau khi code backend, can kiem tra toi thieu:

1. `dotnet build`.
2. `GET /api/public/tournaments`.
3. `GET /api/public/tournaments/{id}`.
4. `GET /api/public/tournaments/{id}/registrations`.
5. `GET /api/tournaments/{id}/rounds-with-matches`.
6. `GET /api/tournaments/{id}/round-maps/{roundMapId}/standings`.
7. `GET /api/notifications/upcoming-matches`.
8. Login JWT van tra `accessToken`, `expiresAtUtc`, `user`.
9. App schedule van render duoc khi:
   - match co du 2 doi.
   - match TBD ca 2 doi.
   - match chi resolve 1 doi.
10. App standings van render duoc khi group co match TBD.

## 6. Cach Sua Backend Neu Can Doi Contract

Neu bat buoc phai thay contract:

1. Giu endpoint/field cu trong it nhat mot phien ban.
2. Them endpoint/field moi song song.
3. Cap nhat app mobile truoc de doc field moi co fallback field cu.
4. Sau khi app moi da release va app cu khong con dung, moi can nhac go field cu.

Trong task hien tai, uu tien khong sua app mobile. Backend phai tu backward-compatible.
