# Task: Bracket Template và tự động sinh cấu trúc giải đấu

## 1. Thông tin tài liệu

- Trạng thái: Đang triển khai — đã hoàn thành phần lớn code MVP, còn các mục chưa tick bên dưới
- Mức ưu tiên: Cao
- Phạm vi: Admin Web, API, database, business logic, bracket viewer, kiểm thử
- Mục tiêu phát hành đề xuất: Chia thành MVP và giai đoạn nâng cao
- Nguyên tắc tương thích: Không làm hỏng các giải đang tạo vòng, bảng và trận thủ công

### 1.1. Cập nhật triển khai ngày 02/08/2026

- [x] Database schema đã được chạy trên SQL Server và đối chiếu đủ 8 bảng mới, 9 cột runtime nullable.
- [x] Solution build thành công: 0 lỗi.
- [x] Unit test: 39/39 test thành công.
- [x] Kiểm tra cú pháp 7 file JavaScript liên quan: thành công.
- [ ] Chưa chạy integration test với dữ liệu giải thật.
- [ ] Chưa chạy UI/E2E, regression đầy đủ và UAT với Admin vận hành.
- [ ] Chưa triển khai feature flag, staging rollout và bộ template mặc định trong database.

## 2. Bối cảnh hiện tại

Hệ thống hiện tại lưu cấu trúc thi đấu theo thứ tự:

```text
Tournament
└── TournamentRoundMap
    └── TournamentRoundGroup
        └── TournamentGroupMatch
```

Bracket không phải là một thực thể được lưu riêng. Bracket được dựng khi đọc các vòng, bảng, trận và quan hệ nguồn đội của từng trận.

Hiện tại Admin phải thực hiện thủ công:

1. Tạo từng vòng đấu.
2. Tạo từng bảng đấu trong mỗi vòng.
3. Tạo từng trận đấu.
4. Chọn đội hoặc nguồn đội cho từng slot.
5. Kết nối đội thắng, đội thua hoặc hạng bảng vào vòng sau.

Việc này tốn thời gian, dễ chọn sai nguồn và khó tái sử dụng cùng một thể thức cho nhiều giải.

## 3. Mục tiêu nghiệp vụ

Xây dựng thư viện Bracket Template để Admin có thể:

1. Tạo và lưu các mẫu bracket dùng lại nhiều lần.
2. Thiết kế sẵn vòng, bảng, trận và luồng đi tiếp.
3. Publish các phiên bản template ổn định.
4. Chọn một template sau khi giải đã chốt đăng ký.
5. Chọn cách xếp seed và xem trước cặp đấu.
6. Xác nhận để hệ thống tự động tạo toàn bộ vòng, bảng và trận thật.
7. Tiếp tục sử dụng logic nhập điểm, xếp hạng và propagation hiện tại.

Kết quả mong muốn:

```text
Chốt đăng ký
    ↓
Chọn Bracket Template
    ↓
Xếp seed tự động hoặc thủ công
    ↓
Xem trước toàn bộ bracket
    ↓
Xác nhận áp dụng
    ↓
Hệ thống tự sinh RoundMap, Group, Match và các nguồn đi tiếp
```

## 4. Phạm vi triển khai

### 4.1. MVP bắt buộc

- Bracket loại trực tiếp.
- Vòng bảng kết hợp loại trực tiếp.
- Trận tranh hạng ba.
- Nguồn đội thắng trận.
- Nguồn đội thua trận.
- Nguồn hạng N của bảng.
- Seed trực tiếp.
- BYE/miễn đấu.
- Xếp seed ngẫu nhiên.
- Xếp seed theo thứ tự đăng ký.
- Admin tự sắp xếp seed.
- Preview trước khi áp dụng.
- Apply trong một transaction.
- Chống apply trùng.
- Reset/reseed khi giải chưa bắt đầu.
- Versioning template.
- Audit người tạo, publish, apply, reset và reseed.
- Không ảnh hưởng bracket thủ công và các giải cũ.

### 4.2. Giai đoạn nâng cao

- Double elimination đầy đủ.
- Swiss system.
- Seed theo bảng xếp hạng ngoài hệ thống.
- Tránh các đội cùng CLB gặp nhau ở vòng đầu.
- Tự động lập lịch theo ngày, giờ và sân.
- Tự động phân công trọng tài.
- Chia sẻ template theo đơn vị tổ chức.
- Marketplace/thư viện template hệ thống.
- Cho phép chỉnh topology bracket sau khi đã apply nhưng chưa thi đấu.

### 4.3. Ngoài phạm vi MVP

- Không tự động ghi đè cấu trúc vòng/bảng/trận thủ công đã tồn tại.
- Không cho thay bracket khi đã có trận bắt đầu hoặc có lịch sử điểm.
- Không gộp tính năng lập lịch sân và phân công trọng tài vào engine sinh bracket.
- Không tự động đưa đăng ký mới vào bracket sau khi đã apply.

## 5. Thuật ngữ

- Bracket Template: Mẫu thể thức có thể tái sử dụng, không chứa đội thật.
- Template Version: Phiên bản bất biến của một template đã publish.
- Template Round: Vòng trong template.
- Template Group: Bảng/nhóm trận trong template.
- Template Match: Trận trong template.
- Slot: Một vị trí đội trong trận.
- Seed: Vị trí xếp hạt giống, ví dụ Seed 1, Seed 2.
- Bracket Application: Một lần áp dụng một template version vào một giải.
- Runtime Bracket: Các `TournamentRoundMap`, `TournamentRoundGroup` và `TournamentGroupMatch` thật đã được sinh.
- BYE: Vị trí miễn đấu, đội còn lại tự động đi tiếp.

## 6. Kiến trúc nghiệp vụ đề xuất

```text
BracketTemplate
└── BracketTemplateVersion
    ├── BracketTemplateRound
    │   └── BracketTemplateGroup
    │       └── BracketTemplateMatch
    │           ├── Slot 1
    │           └── Slot 2
    └── Cấu hình capacity, seed và BYE

BracketTemplateVersion
        +
TournamentRegistration hợp lệ
        ↓
TournamentBracketApplication
├── Seed snapshot
├── TournamentRoundMap
├── TournamentRoundGroup
└── TournamentGroupMatch
```

Nguyên tắc:

- Template không tham chiếu Registration ID thật.
- Template sử dụng stable key để nối vòng, bảng và trận.
- Khi apply, stable key được map sang ID thật của giải.
- Runtime bracket tiếp tục dùng các source type hiện tại.
- Một giải lưu snapshot của version tại thời điểm apply.
- Thay đổi template sau này không được làm thay đổi các giải đã apply.

## 7. Trạng thái và lifecycle

### 7.1. Trạng thái template

```text
DRAFT → PUBLISHED → ARCHIVED
```

Quy tắc:

- `DRAFT`: Được phép chỉnh sửa.
- `PUBLISHED`: Không được sửa trực tiếp, được phép áp dụng vào giải.
- `ARCHIVED`: Không được chọn cho giải mới, các giải cũ vẫn hoạt động.
- Muốn sửa template đã publish phải tạo version draft mới.

### 7.2. Trạng thái application

```text
APPLYING → APPLIED
         ↘ FAILED

APPLIED → REVERTED
```

Quy tắc:

- Chỉ có một application đang hoạt động trên một giải.
- `FAILED` không được để lại cấu trúc giải tạo dở.
- `REVERTED` phải giữ lịch sử audit.

## 8. Thiết kế dữ liệu đề xuất

### BR-DATA-001 — Bracket Template

- [x] Tạo thực thể quản lý thông tin chung của template.
- [x] Có tên template.
- [x] Có mã template duy nhất.
- [x] Có mô tả.
- [x] Có loại thể thức.
- [x] Có trạng thái lifecycle.
- [x] Có người tạo/người cập nhật.
- [x] Có thời gian tạo/cập nhật.
- [x] Có thông tin version hiện tại.

Loại thể thức đề xuất:

- `SINGLE_ELIMINATION`.
- `GROUP_KNOCKOUT`.
- `DOUBLE_ELIMINATION`.
- `CUSTOM`.

Tiêu chí nghiệm thu:

- Không cho trùng mã template.
- Không cho xóa cứng template đã từng được áp dụng.
- Template archived không xuất hiện trong danh sách chọn mặc định.

### BR-DATA-002 — Template Version

- [x] Tạo thực thể version thuộc template.
- [x] Lưu số phiên bản tăng dần.
- [x] Lưu capacity tối đa.
- [x] Lưu số đội tối thiểu.
- [x] Lưu chính sách BYE.
- [x] Lưu seed policy mặc định.
- [x] Lưu thời điểm/người publish.
- [x] Lưu checksum hoặc configuration hash.
- [x] Đảm bảo version published là bất biến.

Tiêu chí nghiệm thu:

- Không có hai version cùng số trong một template.
- Chỉnh sửa template đã publish tạo draft version mới.
- Giải đã apply version cũ không bị ảnh hưởng bởi version mới.

### BR-DATA-003 — Template Round

- [x] Lưu stable key của vòng, ví dụ `R1`, `QF`, `SF`, `FINAL`.
- [x] Lưu tên hiển thị.
- [x] Lưu `SortOrder`.
- [x] Lưu loại vòng.
- [x] Ràng buộc stable key duy nhất trong một version.

Loại vòng đề xuất:

- `GROUP_STAGE`.
- `KNOCKOUT`.
- `FINAL`.
- `PLACEMENT`.
- `LOSER_BRACKET`.

### BR-DATA-004 — Template Group

- [x] Lưu stable key của bảng.
- [x] Liên kết với template round.
- [x] Lưu tên hiển thị.
- [x] Lưu `SortOrder`.
- [x] Ràng buộc tên hoặc stable key duy nhất trong một round.

### BR-DATA-005 — Template Match

- [x] Lưu stable key, ví dụ `R1-M01`.
- [x] Liên kết với template group.
- [x] Lưu thứ tự hiển thị.
- [x] Lưu nhãn trận nếu cần.
- [x] Có đúng hai slot.
- [x] Có metadata phục vụ viewer/template editor.
- [x] Ràng buộc stable key duy nhất trong một version.

### BR-DATA-006 — Template Match Slot

Mỗi slot hỗ trợ một trong các nguồn:

- `SEED`.
- `WINNER_MATCH`.
- `LOSER_MATCH`.
- `GROUP_RANK`.
- `BYE`.

Thông tin theo source type:

- `SEED`: Seed number.
- `WINNER_MATCH`: Source template match key/ID.
- `LOSER_MATCH`: Source template match key/ID.
- `GROUP_RANK`: Source template group key/ID và rank.
- `BYE`: Không cần source ID.

Tiêu chí nghiệm thu:

- Mỗi slot chỉ có đúng một source type.
- Dữ liệu không liên quan đến source type phải để trống.
- Không cho hai slot của cùng một trận trỏ tới cùng một seed/source dẫn đến cùng đội chắc chắn.

### BR-DATA-007 — Tournament Bracket Application

- [x] Lưu Tournament ID.
- [x] Lưu Template ID và Version ID.
- [x] Lưu trạng thái application.
- [x] Lưu phương pháp seed.
- [x] Lưu random seed nếu có.
- [x] Lưu số đội và số BYE.
- [x] Lưu người/thời gian apply.
- [x] Lưu người/thời gian reset.
- [x] Lưu lỗi nếu application thất bại.
- [x] Ràng buộc chỉ một application hoạt động cho một tournament.

### BR-DATA-008 — Seed Snapshot

- [x] Lưu application ID.
- [x] Lưu seed number.
- [x] Lưu Registration ID.
- [x] Lưu thứ tự đầu vào.
- [x] Lưu phương pháp gán seed.
- [x] Lưu cờ admin đã điều chỉnh thủ công.
- [x] Lưu BYE nếu seed không có registration.

Tiêu chí nghiệm thu:

- Một Registration chỉ có một seed trong một application.
- Một seed chỉ có một Registration hoặc BYE.
- Snapshot không tự thay đổi khi có đăng ký mới.

### BR-DATA-009 — Liên kết dữ liệu runtime với application

- [x] Các round/group/match được sinh phải truy ngược được application.
- [x] Lưu template stable key tương ứng trên runtime entity hoặc bảng mapping.
- [x] Cho phép phân biệt dữ liệu sinh tự động với dữ liệu tạo thủ công.
- [x] Hỗ trợ reset đúng phạm vi.
- [x] Không tạo quan hệ cascade có thể xóa nhầm dữ liệu giải.

### BR-DATA-010 — Trạng thái trận BYE

- [x] Bổ sung cách nhận biết trận hoàn thành do BYE.
- [x] Phân biệt kết quả thi đấu bình thường và tự động đi tiếp.
- [x] Không hiển thị BYE như tỷ số hòa `0-0`.
- [x] Không tính BYE vào thống kê điểm vòng bảng.

## 9. Validation engine

### BR-VAL-001 — Validation cấu trúc cơ bản

- [x] Template có ít nhất một round.
- [x] Mỗi match thuộc một group hợp lệ.
- [x] Mỗi match có đúng hai slot.
- [x] Stable key của round/group/match không trùng.
- [x] Seed number nằm trong capacity.
- [x] Không có source ID/key bị thiếu.
- [x] Không có match tự tham chiếu.

### BR-VAL-002 — Validation dependency graph

- [x] Xây dependency graph từ source match/group.
- [x] Phát hiện chu trình trực tiếp và gián tiếp.
- [x] Không cho source ở vòng sau cấp đội cho vòng trước.
- [x] Không chỉ dựa vào `SortOrder`; phải kiểm tra topology thực tế.
- [x] Không cho `GROUP_RANK` lấy từ chính group chứa trận đích.
- [x] Không cho `GROUP_RANK` lấy từ group chưa thể hoàn thành trước trận đích.

### BR-VAL-003 — Validation seed

- [x] Seed nằm trong khoảng `1..Capacity`.
- [x] Hai slot cùng trận không được có cùng seed.
- [x] Knockout vòng đầu không dùng một seed ở nhiều trận.
- [x] Vòng bảng được phép dùng cùng seed trong nhiều trận.
- [x] Cảnh báo seed không được sử dụng.
- [x] Cảnh báo nhánh không nhận được đội từ bất kỳ seed/source nào.

### BR-VAL-004 — Validation group rank

- [x] Rank lớn hơn 0.
- [x] Rank không vượt số đội có thể có trong group.
- [x] Source group tồn tại.
- [x] Source group thuộc cùng template version.
- [x] Source group phải nằm trước trận đích về dependency.

### BR-VAL-005 — Validation BYE

- [x] Chỉ cho dùng BYE khi version cho phép.
- [x] Không cho một trận có hai slot đều BYE.
- [x] Không cho BYE trong vòng bảng của MVP.
- [x] Cảnh báo cách đặt BYE không ưu tiên seed cao.
- [x] Xác định được winner tự động của trận BYE.

### BR-VAL-006 — Warning nghiệp vụ

- [x] Cảnh báo winner một trận được nối vào nhiều trận đích.
- [x] Cảnh báo loser không có nơi đi trong template khai báo nhánh thua.
- [x] Cảnh báo match không dẫn tới bất kỳ trận terminal nào.
- [x] Cảnh báo có nhiều trận chung kết ngoài chủ đích.
- [x] Cảnh báo nhánh bracket mất cân đối.
- [x] Phân loại kết quả thành `ERROR`, `WARNING`, `INFO`.

Tiêu chí publish:

- Không còn `ERROR`.
- `WARNING` phải được Admin xác nhận.
- Kết quả validation được lưu hoặc có checksum gắn với draft vừa kiểm tra.

## 10. Template management API

### BR-API-001 — Danh sách template

- [x] Phân trang.
- [x] Tìm theo tên/mã.
- [x] Lọc theo loại thể thức.
- [x] Lọc theo trạng thái.
- [x] Trả version hiện tại và số lần đã áp dụng.

### BR-API-002 — CRUD draft template

- [x] Tạo template.
- [x] Xem chi tiết.
- [x] Cập nhật thông tin draft.
- [x] Lưu cấu trúc round/group/match/slot.
- [x] Clone template.
- [x] Archive template.
- [x] Không cho sửa version published.

### BR-API-003 — Version và publish

- [x] Tạo draft version từ version published.
- [x] Lấy graph đầy đủ của version.
- [x] Validate version.
- [x] Preview template bằng seed giả.
- [x] Publish version.
- [x] Chặn publish nếu còn lỗi.

### BR-API-004 — Quyền truy cập

- [x] Tất cả endpoint quản trị yêu cầu role Admin.
- [x] Không đưa draft template vào public API.
- [x] Kiểm tra quyền cho mọi thao tác clone/publish/archive.
- [x] Có concurrency token khi hai Admin cùng sửa draft.

## 11. Template Builder UI

### BR-UI-001 — Menu thư viện Bracket

- [x] Thêm menu quản trị `Thư viện Bracket`.
- [x] Hiển thị tên, mã, loại, capacity, version, trạng thái.
- [x] Hiển thị người và ngày cập nhật.
- [x] Hiển thị số lần template đã được sử dụng.
- [x] Có tìm kiếm, filter và phân trang.
- [x] Có action tạo mới, xem, sửa draft, clone, publish và archive.

### BR-UI-002 — Wizard tạo template

Các bước UI:

1. Thông tin chung.
2. Capacity và BYE policy.
3. Thiết kế vòng và bảng.
4. Thiết kế trận và source slot.
5. Cấu hình seed.
6. Validate.
7. Preview.
8. Publish.

Yêu cầu:

- [ ] Cho lưu draft ở từng bước.
- [x] Khi tải lại trang có thể tiếp tục.
- [x] Hiển thị trạng thái dữ liệu chưa lưu.
- [x] Có xác nhận trước khi rời trang khi chưa lưu.

### BR-UI-003 — Canvas thiết kế bracket

- [x] Hiển thị các round theo cột.
- [x] Hiển thị group và match theo cấu trúc.
- [x] Hiển thị hai slot trên match card.
- [x] Hiển thị đường winner/loser.
- [x] Hiển thị nguồn group rank.
- [x] Cho thêm/sửa/xóa round.
- [x] Cho thêm/sửa/xóa group.
- [x] Cho thêm/sửa/xóa match.
- [x] Cho chọn source type cho từng slot.
- [x] Cho chọn seed number.
- [x] Cho chọn source match.
- [x] Cho chọn source group/rank.
- [x] Cho đánh dấu BYE.
- [x] Focus node khi click lỗi validation.

### BR-UI-004 — Công cụ sinh bracket nhanh

- [x] Sinh knockout 4 đội.
- [x] Sinh knockout 8 đội.
- [x] Sinh knockout 16 đội.
- [x] Sinh knockout 32 đội.
- [x] Cho tùy chọn trận tranh hạng ba.
- [x] Sinh seed placement chuẩn.
- [x] Sinh vòng bảng theo số bảng và số đội mỗi bảng.
- [x] Sinh lịch vòng tròn một lượt.
- [x] Cho cấu hình lấy N đội mỗi bảng vào knockout.
- [x] Sinh source `GROUP_RANK` cho vòng sau.
- [x] Cho chỉnh tay sau khi sinh.

### BR-UI-005 — Validation panel

- [x] Hiển thị tổng số error/warning/info.
- [x] Nhóm lỗi theo round/group/match.
- [x] Click lỗi để focus đúng vị trí.
- [x] Có nút kiểm tra lại.
- [x] Chặn publish khi có error.
- [x] Yêu cầu xác nhận warning trước publish.

### BR-UI-006 — Preview template

- [x] Hiển thị bracket bằng tên Seed thay vì đội thật.
- [x] Hiển thị winner/loser/group-rank connection.
- [x] Hiển thị số round/group/match.
- [x] Hiển thị số trận tối đa/tối thiểu.
- [x] Hiển thị BYE dự kiến theo số đội.
- [x] Hỗ trợ desktop và mobile.

## 12. Seeding engine

### BR-SEED-001 — Chọn registration hợp lệ

- [x] Chỉ lấy đăng ký thành công của đúng tournament.
- [x] Loại đăng ký đã hủy hoặc không còn hợp lệ.
- [x] Không lấy registration bị trùng.
- [x] Kiểm tra đăng ký đủ thành viên theo loại giải.
- [x] Chụp snapshot danh sách đầu vào khi preview.

### BR-SEED-002 — Seed theo thứ tự đăng ký

- [x] Sắp theo thời điểm đăng ký/xác nhận.
- [x] Dùng Registration ID làm tie-break.
- [x] Kết quả ổn định giữa preview và apply.

### BR-SEED-003 — Seed ngẫu nhiên

- [x] Sinh random seed cho mỗi lần xáo.
- [x] Lưu random seed.
- [x] Cùng input và random seed phải tạo cùng kết quả.
- [x] Có action `Xáo lại`.
- [x] Apply dùng đúng kết quả preview cuối cùng.

### BR-SEED-004 — Seed thủ công

- [x] Kéo thả đội vào seed.
- [x] Hoán đổi hai seed.
- [x] Tìm kiếm đội.
- [x] Hiển thị đội chưa được gán.
- [x] Không cho một registration xuất hiện ở nhiều seed.
- [x] Không cho registration ngoài snapshot.
- [x] Không cho apply nếu còn seed bắt buộc bị trống.

### BR-SEED-005 — Phân bổ BYE

- [x] Tính số BYE từ capacity và số đội.
- [x] Ưu tiên BYE cho seed cao theo seed placement của template.
- [x] Hiển thị đội được miễn vòng đầu.
- [x] Cho Admin điều chỉnh nếu policy cho phép.
- [x] Chặn nếu số đội thấp hơn minimum của template.

## 13. Preview application

### BR-PREVIEW-001 — Điều kiện preview

- [x] Tournament tồn tại và chưa bị xóa.
- [x] Template version đã publish.
- [x] Số đội nằm trong giới hạn template.
- [x] Không có application đang hoạt động gây xung đột.
- [x] Kiểm tra cấu trúc thủ công hiện có.

### BR-PREVIEW-002 — Nội dung preview

- [x] Template/version.
- [x] Danh sách registration snapshot.
- [x] Seed assignment.
- [x] Tổng số đội và BYE.
- [x] Danh sách vòng/bảng/trận dự kiến.
- [x] Cặp đấu vòng đầu.
- [x] Nguồn các vòng sau.
- [x] Đội được tự động đi tiếp.
- [x] Số trận chưa có trọng tài.
- [x] Danh sách error/warning.

### BR-PREVIEW-003 — Preview token

- [x] Sinh preview token/checksum.
- [x] Token gắn với tournament, template version và registration snapshot.
- [x] Apply phải gửi lại token.
- [x] Từ chối apply nếu danh sách đăng ký hoặc template đã thay đổi.
- [x] Cho Admin refresh preview khi token hết hiệu lực.

## 14. Apply engine

### BR-APP-001 — Điều kiện áp dụng

- [x] Tournament tồn tại và chưa bị xóa.
- [x] Tournament ở trạng thái cho phép cấu hình bracket.
- [x] Danh sách đăng ký đã được chốt.
- [x] Template version hợp lệ và đã publish.
- [x] Preview token còn hợp lệ.
- [x] Không có application hoạt động.
- [x] Không có trận đã bắt đầu hoặc có lịch sử điểm.
- [x] MVP chặn apply nếu đã có round/group/match thủ công.

### BR-APP-002 — Transaction và concurrency

- [x] Toàn bộ apply chạy trong một transaction.
- [x] Dùng isolation phù hợp để chống hai request đồng thời.
- [x] Có idempotency token.
- [x] Có unique constraint bảo vệ một active application/tournament.
- [x] Lỗi bất kỳ phải rollback toàn bộ runtime structure.

### BR-APP-003 — Sinh application và snapshot

- [x] Tạo application trạng thái `APPLYING`.
- [x] Lưu template/version snapshot.
- [x] Lưu seed snapshot.
- [x] Lưu random seed và phương pháp xếp.
- [x] Lưu người thực hiện.

### BR-APP-004 — Sinh vòng và bảng

- [x] Map template round thành `TournamentRoundMap`.
- [x] Map template group thành `TournamentRoundGroup`.
- [x] Giữ đúng thứ tự và nhãn.
- [x] Ghi application ID/template key.
- [x] Xây dictionary mapping template key sang runtime ID.

### BR-APP-005 — Sinh trận

- [x] Tạo toàn bộ runtime match trước khi nối source.
- [x] Ghi application ID/template match key.
- [x] Mặc định score chưa thi đấu.
- [x] Không yêu cầu lịch, sân hoặc video.
- [x] Cho phép chưa phân công trọng tài.
- [x] Xây mapping template match key sang runtime Match ID.

### BR-APP-006 — Resolve nguồn slot

Mapping bắt buộc:

| Template source | Runtime source |
|---|---|
| `SEED` | `REGISTRATION` và Registration ID thật |
| `WINNER_MATCH` | `WINNER_MATCH` và runtime source Match ID |
| `LOSER_MATCH` | `LOSER_MATCH` và runtime source Match ID |
| `GROUP_RANK` | `GROUP_RANK` và runtime source Group ID/rank |
| `BYE` | `BYE` |

- [x] Không còn template ID trong runtime record.
- [x] Tất cả source thuộc cùng tournament.
- [x] Không tạo dependency cycle.
- [x] Không resolve hai slot thành cùng registration.

### BR-APP-007 — Kiểm tra sau khi sinh

- [x] Đúng số lượng round/group/match dự kiến.
- [x] Đúng số lượng seed assignment.
- [x] Không có source bị mất.
- [x] Không có source khác tournament.
- [x] Không có cặp đội trùng trong cùng group.
- [x] Không có graph cycle.
- [ ] Runtime bracket đọc được bằng API viewer hiện tại.
- [x] Chỉ chuyển application sang `APPLIED` sau khi tất cả kiểm tra thành công.

### BR-APP-008 — Chống apply trùng

- [x] Bấm nút hai lần không sinh hai bracket.
- [x] Retry cùng idempotency token trả lại kết quả application cũ.
- [x] Hai Admin apply đồng thời chỉ một request thành công.
- [x] UI khóa nút trong thời gian request đang chạy.

## 15. BYE và auto-advance

### BR-BYE-001 — Nhận diện trận BYE

- [x] Một slot resolve được đội thật và slot còn lại là BYE.
- [x] Không yêu cầu nhập tỷ số.
- [x] Đội thật được xác định là winner.
- [x] Lưu lý do hoàn thành là BYE/auto-advance.

### BR-BYE-002 — Propagation

- [x] Winner của trận BYE được đẩy sang `WINNER_MATCH` downstream.
- [x] Xử lý được chuỗi nhiều BYE liên tiếp.
- [x] Propagation chạy đến khi không còn thay đổi.
- [x] Không tạo hai đội giống nhau trong trận downstream.
- [x] Có log nếu propagation không hoàn thành.

### BR-BYE-003 — Hiển thị

- [x] Match card hiển thị `Miễn đấu`.
- [x] Không hiển thị `0-0` như trận bình thường.
- [x] Hiển thị đội đã tự động đi tiếp.
- [x] Không cho mở modal nhập tỷ số cho trận BYE.
- [x] Public/mobile bracket hiển thị thống nhất.

### BR-BYE-004 — Thống kê và thông báo

- [x] Không tính trận BYE vào standings vòng bảng.
- [x] Không cộng điểm thắng thi đấu bình thường.
- [x] Xác định rõ có gửi thông báo auto-advance hay không: MVP không gửi thông báo cho BYE.
- [x] Không gửi thông báo sai nội dung `đã thắng trận` nếu không thi đấu.

## 16. Trọng tài và trạng thái sẵn sàng thi đấu

### BR-REF-001 — Sinh trận chưa có trọng tài

- [x] Engine được phép sinh match với `RefereeUserId` trống.
- [x] Không dùng trọng tài placeholder.
- [x] Không tự gán một trọng tài cho toàn bộ bracket.
- [x] Hiển thị badge `Chưa phân công trọng tài`.

### BR-REF-002 — Quy tắc trước khi thi đấu

- [x] Trận bình thường phải có trọng tài hợp lệ trước khi xuất hiện trong tài khoản trọng tài.
- [x] Trọng tài phải active và verified theo logic hiện tại.
- [x] Admin được phân công/chỉnh trọng tài sau khi bracket được sinh.
- [x] Dashboard hiển thị tổng số trận chưa có trọng tài.

## 17. UI áp dụng template vào giải

### BR-TUI-001 — Entry point

- [x] Thêm nút `Áp dụng Bracket` tại trang quản lý bracket của giải.
- [x] Hiển thị trạng thái chưa áp dụng/đã áp dụng/đã khóa.
- [x] Nếu đã áp dụng, hiển thị template và version.
- [x] Nếu có cấu trúc thủ công, hiển thị lý do không thể apply.

### BR-TUI-002 — Chọn template

- [x] Hiển thị các template published phù hợp số đội.
- [x] Hiển thị loại bracket.
- [x] Hiển thị minimum/capacity.
- [x] Hiển thị số BYE dự kiến.
- [x] Hiển thị số vòng/bảng/trận.
- [x] Hiển thị version và mô tả.
- [x] Template không phù hợp được disable và nêu rõ lý do.

### BR-TUI-003 — Chọn phương pháp seed

- [x] Theo thứ tự đăng ký.
- [x] Ngẫu nhiên.
- [x] Admin tự sắp xếp.
- [x] Hiển thị giải thích tác động của từng phương pháp.

### BR-TUI-004 — Màn hình seed

- [x] Hiển thị seed number.
- [x] Hiển thị đội/cặp và thành viên.
- [x] Hiển thị thời gian đăng ký.
- [x] Hiển thị trình độ/CLB nếu có.
- [x] Hiển thị trạng thái xác nhận/thanh toán cần thiết.
- [x] Kéo thả và hoán đổi seed.
- [x] Có action xáo lại.
- [x] Có action khôi phục mặc định.
- [x] Hiển thị đội chưa được xếp.
- [x] Hiển thị BYE rõ ràng.

### BR-TUI-005 — Preview xác nhận

- [x] Hiển thị bracket đầy đủ trước khi apply.
- [x] Hiển thị cặp đấu vòng đầu.
- [x] Hiển thị đội được BYE.
- [x] Hiển thị source winner/loser/group rank.
- [x] Hiển thị số trận chưa có trọng tài.
- [x] Hiển thị error/warning.
- [x] Admin phải tick xác nhận đã kiểm tra danh sách đội và seed.

### BR-TUI-006 — Trạng thái apply

- [x] Hiển thị loading và chặn submit lại.
- [x] Hiển thị kết quả thành công.
- [x] Hiển thị số vòng/bảng/trận đã tạo.
- [x] Hiển thị số BYE.
- [x] Có link mở bracket.
- [x] Có link quản lý lịch và trọng tài.
- [x] Nếu lỗi, hiển thị bước lỗi và không hiển thị dữ liệu tạo dở.

### BR-TUI-007 — Thông tin application trên bracket

- [x] Hiển thị `Tạo thủ công` hoặc `Sinh từ template`.
- [x] Hiển thị template name/version.
- [x] Hiển thị người và thời gian apply.
- [x] Có action xem seed snapshot.
- [x] Có action reset/reseed khi còn được phép.

## 18. Khóa đăng ký và snapshot

### BR-REG-001 — Chốt danh sách đội

- [x] Xác định trạng thái/flag danh sách đăng ký đã khóa.
- [x] Chỉ cho apply chính thức sau khi đã khóa đăng ký.
- [x] Hiển thị số đội đủ điều kiện trước khi khóa.
- [x] Cảnh báo đăng ký chưa thanh toán/chưa xác nhận nếu có.

### BR-REG-002 — Đăng ký phát sinh sau apply

- [x] Không tự động thêm vào seed snapshot.
- [x] Hiển thị đăng ký mới chưa nằm trong bracket.
- [x] Muốn thêm đội phải reseed nếu chưa thi đấu.
- [x] Nếu giải đã bắt đầu, chặn việc thêm đội vào bracket.

## 19. Reset và reseed

### BR-LIFE-001 — Điều kiện reset

- [x] Không có trận hoàn thành.
- [x] Không có lịch sử điểm.
- [x] Không có trận đang diễn ra.
- [x] Không có dữ liệu bên ngoài phụ thuộc vào trận được sinh.
- [x] Application hiện tại ở trạng thái `APPLIED`.

### BR-LIFE-002 — Thực hiện reset

- [x] Hiển thị danh sách dữ liệu sẽ bị xóa.
- [x] Yêu cầu Admin nhập xác nhận.
- [x] Yêu cầu lý do reset.
- [x] Xóa đúng round/group/match thuộc application.
- [x] Không xóa dữ liệu thủ công không liên quan.
- [x] Chạy trong một transaction.
- [x] Đánh dấu application là `REVERTED`.
- [x] Lưu người và thời gian reset.

### BR-LIFE-003 — Reseed

- [x] Chỉ cho reseed khi chưa có trận bắt đầu.
- [x] Tạo preview và snapshot mới.
- [x] Không sửa lịch sử snapshot cũ.
- [x] Khuyến nghị tạo application mới sau khi revert application cũ.
- [x] Hiển thị lịch sử các lần apply/reseed.

### BR-LIFE-004 — Khóa khi giải đã thi đấu

- [x] Không cho đổi template.
- [x] Không cho reseed.
- [x] Không cho reset.
- [x] Vẫn cho sửa lịch, sân, video và trọng tài theo quyền hiện tại.
- [x] Nếu cần thay đội phải dùng workflow đặc biệt ngoài MVP.

## 20. Tương thích hệ thống hiện tại

### BR-COMP-001 — Giải cũ và bracket thủ công

- [x] Các giải không có application tiếp tục hoạt động.
- [x] Admin vẫn có thể tạo vòng/bảng/trận thủ công.
- [x] Không yêu cầu migration dữ liệu bracket cũ thành template.
- [x] Các cột/liên kết mới phải nullable đối với dữ liệu cũ.

### BR-COMP-002 — Viewer hiện tại

- [x] API `rounds-with-matches` tiếp tục trả runtime data.
- [x] Admin bracket viewer hiển thị được bracket sinh tự động.
- [x] Public/mobile viewer hiển thị được bracket sinh tự động.
- [x] Đường winner/loser giữ đúng hành vi hiện tại.
- [x] Bổ sung hiển thị BYE mà không phá layout.

### BR-COMP-003 — Propagation hiện tại

- [x] Trận bình thường tiếp tục propagate winner/loser như cũ.
- [x] Group standings tiếp tục cung cấp `GROUP_RANK`.
- [x] Bổ sung propagation cho BYE.
- [x] Không cập nhật đội của match downstream đã hoàn thành.
- [x] Có cảnh báo/audit khi source upstream thay đổi nhưng downstream đã khóa.

### BR-COMP-004 — Chỉnh sửa runtime bracket

- [x] Cho sửa thời gian, sân, địa điểm, video và trọng tài.
- [x] Hạn chế sửa đội/source trực tiếp trên bracket sinh từ template.
- [ ] Nếu cho phép tùy biến topology, đánh dấu application `Customized`.
- [ ] Khi đã customized, reset/reseed phải kiểm tra lại phạm vi dữ liệu.

## 21. Security và audit

### BR-SEC-001 — Phân quyền

- [x] Chỉ Admin được quản lý template.
- [x] Chỉ Admin được publish/archive.
- [x] Chỉ Admin được apply/reset/reseed.
- [x] Không public draft template hoặc seed snapshot.
- [ ] Kiểm tra tournament ownership/scope nếu sau này có nhiều đơn vị tổ chức.

### BR-SEC-002 — Audit log

Ghi nhận tối thiểu:

- [x] Tạo template.
- [x] Sửa draft.
- [ ] Validate.
- [x] Publish version.
- [x] Archive.
- [x] Apply vào tournament.
- [x] Seed trước và sau khi chỉnh thủ công.
- [x] Reset.
- [x] Reseed.
- [ ] Thay đổi runtime bracket ngoài snapshot.

### BR-SEC-003 — Concurrency

- [x] Optimistic concurrency khi sửa draft.
- [x] Hiển thị cảnh báo nếu version đã thay đổi bởi Admin khác.
- [x] Lock logic khi apply/reset/reseed.
- [x] Không phụ thuộc hoàn toàn vào việc disable nút ở UI.

## 22. Logging và giám sát

### BR-OPS-001 — Application log

Mỗi apply có correlation ID và log:

- [x] Tournament ID.
- [x] Template/version ID.
- [x] Số registration.
- [x] Số round/group/match dự kiến.
- [x] Số runtime record đã sinh.
- [x] Số BYE.
- [x] Thời gian xử lý.
- [x] Bước bị lỗi.
- [x] Kết quả rollback.

### BR-OPS-002 — Health check sau apply

- [x] Kiểm tra số lượng entity.
- [x] Kiểm tra source mapping.
- [x] Kiểm tra tournament consistency.
- [x] Kiểm tra cycle.
- [x] Kiểm tra seed assignment.
- [x] Kiểm tra duplicate pair.
- [ ] Kiểm tra viewer API đọc thành công.

### BR-OPS-003 — Metrics đề xuất

- [x] Số template theo trạng thái.
- [x] Số application thành công/thất bại.
- [ ] Thời gian apply trung bình.
- [x] Số lần reset/reseed.
- [ ] Số lỗi propagation BYE.
- [x] Số bracket còn trận chưa phân công trọng tài.

## 23. Kiểm thử

### BR-TEST-001 — Unit test validation

- [x] Match tự tham chiếu.
- [x] Chu trình hai match.
- [x] Chu trình nhiều match.
- [x] Source match không tồn tại.
- [x] Source group không tồn tại.
- [x] Source từ vòng sau.
- [x] Group rank bằng 0.
- [x] Group rank vượt số đội.
- [x] Group rank lấy từ chính group hiện tại.
- [x] Seed ngoài capacity.
- [x] Hai slot cùng seed.
- [x] Hai slot đều BYE.
- [x] Stable key bị trùng.
- [x] Seed bị bỏ quên.

### BR-TEST-002 — Unit test seeding

- [x] Tám đội vào bracket tám seed.
- [x] Sáu đội vào bracket tám seed.
- [x] Số đội thấp hơn minimum.
- [x] Số đội vượt capacity.
- [x] Random có thể tái tạo bằng cùng random seed.
- [x] Xáo lại tạo kết quả mới.
- [x] Thứ tự đăng ký có tie-break ổn định.
- [x] Manual seed không trùng registration.
- [x] BYE được phân bổ đúng.

### BR-TEST-003 — Integration test knockout

Với template knockout tám đội:

- [ ] Tạo ba vòng.
- [ ] Tạo bảy trận.
- [ ] Tạo bốn trận vòng đầu.
- [ ] Tạo hai trận bán kết.
- [ ] Tạo một trận chung kết.
- [ ] Winner vòng đầu nối đúng bán kết.
- [ ] Winner bán kết nối đúng chung kết.
- [ ] Seed map đúng cặp đấu đã preview.

### BR-TEST-004 — Integration test group-to-knockout

- [ ] Sinh đúng số bảng.
- [ ] Sinh đủ cặp đấu vòng tròn.
- [ ] Không trùng cặp trong cùng bảng.
- [ ] Sinh đúng source `GROUP_RANK`.
- [ ] Hạng bảng được đẩy vào đúng nhánh knockout.
- [ ] Hoàn thành vòng bảng mới resolve đội vòng sau.

### BR-TEST-005 — Integration test transaction

- [ ] Lỗi khi tạo round rollback toàn bộ.
- [ ] Lỗi khi tạo group rollback toàn bộ.
- [ ] Lỗi khi tạo match rollback toàn bộ.
- [ ] Lỗi khi map source rollback toàn bộ.
- [ ] Không còn application hoạt động sai sau rollback.
- [ ] Retry an toàn sau lỗi.

### BR-TEST-006 — Concurrency và idempotency

- [ ] Bấm apply hai lần.
- [ ] Retry cùng idempotency token.
- [ ] Hai Admin apply đồng thời.
- [ ] Apply trong khi registration snapshot thay đổi.
- [ ] Apply trong khi template version thay đổi.

### BR-TEST-007 — BYE

- [x] Một đội gặp BYE tự động đi tiếp.
- [ ] Không cần nhập tỷ số.
- [ ] Không hiện `0-0`.
- [ ] Không tính vào standings.
- [x] Propagate qua một tầng.
- [x] Propagate qua nhiều tầng BYE liên tiếp.
- [ ] Không gửi thông báo sai nội dung.

### BR-TEST-008 — Lifecycle

- [ ] Reset khi chưa thi đấu thành công.
- [ ] Không cho reset sau khi có điểm.
- [ ] Không cho reset sau khi có lịch sử điểm.
- [ ] Reset không xóa dữ liệu thủ công ngoài application.
- [ ] Reseed giữ snapshot cũ.
- [ ] Registration mới không tự vào bracket.
- [ ] Template archive không ảnh hưởng giải đã apply.

### BR-TEST-009 — UI/E2E

- [ ] Tạo draft template.
- [ ] Dùng công cụ sinh knockout tám đội.
- [ ] Validate và sửa lỗi.
- [ ] Publish version.
- [ ] Chọn template trong giải.
- [ ] Chọn seed mode.
- [ ] Kéo thả seed.
- [ ] Preview bracket.
- [ ] Apply.
- [ ] Mở bracket kiểm tra đường nối.
- [ ] Nhập kết quả và kiểm tra đội đi tiếp.
- [ ] Kiểm tra desktop/mobile.

### BR-TEST-010 — Regression

- [ ] Tạo vòng thủ công.
- [ ] Tạo bảng thủ công.
- [ ] Tạo trận thủ công.
- [ ] Nhập điểm bởi Admin.
- [ ] Nhập điểm bởi trọng tài.
- [ ] Propagation winner/loser hiện tại.
- [ ] Xếp hạng bảng hiện tại.
- [ ] Bracket public/mobile hiện tại.
- [ ] Xóa match/group/round theo ràng buộc hiện tại.

## 24. API nghiệp vụ đề xuất

Tên route cuối cùng cần bám convention của dự án. Danh sách dưới đây mô tả trách nhiệm API, không phải cam kết tên kỹ thuật cuối cùng.

### Template

- `GET /api/admin/bracket-templates`
- `POST /api/admin/bracket-templates`
- `GET /api/admin/bracket-templates/{templateId}`
- `PUT /api/admin/bracket-templates/{templateId}`
- `POST /api/admin/bracket-templates/{templateId}/clone`
- `POST /api/admin/bracket-templates/{templateId}/archive`

### Version

- `GET /api/admin/bracket-templates/{templateId}/versions`
- `POST /api/admin/bracket-templates/{templateId}/versions`
- `GET /api/admin/bracket-template-versions/{versionId}`
- `PUT /api/admin/bracket-template-versions/{versionId}`
- `POST /api/admin/bracket-template-versions/{versionId}/validate`
- `POST /api/admin/bracket-template-versions/{versionId}/preview`
- `POST /api/admin/bracket-template-versions/{versionId}/publish`

### Tournament application

- `GET /api/admin/tournaments/{tournamentId}/bracket/templates`
- `GET /api/admin/tournaments/{tournamentId}/bracket/eligible-registrations`
- `POST /api/admin/tournaments/{tournamentId}/bracket/preview`
- `POST /api/admin/tournaments/{tournamentId}/bracket/apply`
- `GET /api/admin/tournaments/{tournamentId}/bracket/application`
- `GET /api/admin/tournaments/{tournamentId}/bracket/seeds`
- `POST /api/admin/tournaments/{tournamentId}/bracket/reset`
- `POST /api/admin/tournaments/{tournamentId}/bracket/reseed`

## 25. Các file/khu vực dự kiến bị ảnh hưởng khi triển khai

### Database và model

- `HanakaServer/Models/`
- `HanakaServer/Data/PickleballDbContext.cs`
- `HanakaServer/Migrations/`
- Các model runtime tournament round/group/match hiện tại.

### Backend

- Controllers quản lý template mới.
- Controller/API apply template vào tournament.
- Service validation graph.
- Service seeding.
- Service preview.
- Service instantiate/apply.
- Service reset/reseed.
- `TournamentBracketPropagationService` để hỗ trợ BYE.
- `TournamentStandingsService` để loại trừ kết quả BYE nếu cần.

### Admin UI

- Menu quản trị thư viện bracket.
- Trang danh sách template.
- Template builder/editor.
- Template validation/preview.
- Wizard áp dụng bracket vào giải.
- Seed editor.
- Application history.

### Viewer

- Admin bracket viewer.
- Public tournament bracket.
- Mobile/webview bracket nếu đang dùng chung payload.
- Hiển thị BYE và metadata template/application.

## 26. Migration và rollout

### BR-ROLL-001 — Migration an toàn

- [x] Các bảng template là bổ sung mới.
- [x] Quan hệ application trên runtime entity phải nullable.
- [x] Không backfill bắt buộc cho bracket cũ.
- [x] Có index cho template code, version và active application.
- [ ] Chạy migration thử trên bản sao dữ liệu production.
- [ ] Kiểm tra thời gian migration và lock database.

### BR-ROLL-002 — Feature flag

- [ ] Ẩn menu và nút apply sau feature flag.
- [ ] Cho phép bật ở staging trước.
- [ ] Có thể tắt UI mà không ảnh hưởng bracket đã sinh.
- [ ] Không được tắt propagation BYE nếu đã có application dùng BYE.

### BR-ROLL-003 — Template mặc định

Chuẩn bị sẵn và kiểm thử:

- [ ] Knockout 4 đội.
- [ ] Knockout 8 đội.
- [ ] Knockout 16 đội.
- [ ] Knockout 32 đội.
- [ ] Knockout 8 đội có tranh hạng ba.
- [ ] Hai bảng, lấy hai đội mỗi bảng vào bán kết.
- [ ] Bốn bảng, lấy hai đội mỗi bảng vào tứ kết.

### BR-ROLL-004 — UAT

- [ ] UAT với Admin vận hành giải.
- [ ] UAT giải đủ đội.
- [ ] UAT giải thiếu đội có BYE.
- [ ] UAT vòng bảng sang knockout.
- [ ] UAT nhập điểm bởi trọng tài.
- [ ] UAT reset/reseed trước khi thi đấu.
- [ ] Xác nhận không thể reset sau khi thi đấu.

## 27. Lộ trình triển khai đề xuất

### Milestone 1 — Chốt nghiệp vụ và nền tảng dữ liệu

- BR-DATA-001 đến BR-DATA-010.
- Chốt lifecycle template/application.
- Chốt chính sách đăng ký và BYE.
- Chốt quyền Admin.

Đầu ra:

- ERD được duyệt.
- Business rules được duyệt.
- Migration plan được duyệt.

### Milestone 2 — Template CRUD và validation

- BR-API-001 đến BR-API-004.
- BR-VAL-001 đến BR-VAL-006.
- Unit test validation.

Đầu ra:

- Tạo, sửa, clone, validate, publish và archive template qua API.

### Milestone 3 — Template Builder UI

- BR-UI-001 đến BR-UI-006.
- Công cụ sinh knockout và group-stage cơ bản.
- Preview bằng seed giả.

Đầu ra:

- Admin tự tạo và publish template hoàn chỉnh mà không thao tác database.

### Milestone 4 — Seeding và preview tournament

- BR-SEED-001 đến BR-SEED-005.
- BR-PREVIEW-001 đến BR-PREVIEW-003.
- UI chọn template và seed.

Đầu ra:

- Admin chọn template, xếp đội và xem preview nhưng chưa tạo runtime bracket.

### Milestone 5 — Apply engine

- BR-APP-001 đến BR-APP-008.
- Transaction, mapping và idempotency.
- Integration test knockout và group-to-knockout.

Đầu ra:

- Một lần xác nhận sinh đầy đủ round/group/match và source.

### Milestone 6 — BYE, trọng tài và lifecycle

- BR-BYE-001 đến BR-BYE-004.
- BR-REF-001 đến BR-REF-002.
- BR-LIFE-001 đến BR-LIFE-004.
- BR-REG-001 đến BR-REG-002.

Đầu ra:

- Thiếu đội vẫn vận hành đúng.
- Trận sinh tự động không bắt buộc có trọng tài ngay.
- Reset/reseed an toàn trước khi thi đấu.

### Milestone 7 — Regression, UAT và rollout

- BR-TEST-001 đến BR-TEST-010.
- BR-OPS-001 đến BR-OPS-003.
- BR-ROLL-001 đến BR-ROLL-004.

Đầu ra:

- Feature sẵn sàng bật trên production bằng feature flag.

## 28. Thứ tự ưu tiên backlog

### P0 — Bắt buộc trước khi phát hành

- Data model và migration an toàn.
- Template versioning.
- Validation dependency graph.
- Template builder cơ bản.
- Seeding theo đăng ký, random và thủ công.
- Preview token.
- Apply transaction.
- Idempotency/concurrency.
- Runtime source mapping.
- BYE auto-advance.
- Reset trước khi thi đấu.
- Regression luồng bracket hiện tại.
- Audit publish/apply/reset.

### P1 — Nên có trong bản phát hành đầu

- Wizard sinh nhanh knockout.
- Wizard sinh vòng bảng.
- Clone template.
- Application history.
- Dashboard trận chưa có trọng tài.
- Template mặc định của hệ thống.
- Metrics và health check sau apply.

### P2 — Nâng cao

- Double elimination.
- Swiss system.
- Club separation.
- Ranking seed.
- Schedule/court generation.
- Bulk referee assignment nâng cao.

## 29. Definition of Done

Feature được coi là hoàn thành khi đáp ứng tất cả điều kiện sau:

- [x] Admin tạo được template mà không cần sửa dữ liệu trực tiếp.
- [x] Template được validate trước khi publish.
- [x] Version published là bất biến.
- [x] Một template dùng được cho nhiều giải.
- [x] Danh sách đội được snapshot.
- [x] Seed có thể tự động hoặc chỉnh thủ công.
- [x] Preview và kết quả apply giống nhau.
- [x] Một lần apply tạo đầy đủ round/group/match.
- [x] Winner/loser/group-rank source được map đúng.
- [x] BYE tự động đi tiếp và hiển thị đúng.
- [x] Apply lỗi rollback toàn bộ.
- [x] Apply lặp không tạo dữ liệu trùng.
- [x] Không reset được sau khi có trận bắt đầu.
- [x] Các giải cũ không bị ảnh hưởng.
- [ ] Bracket Admin/public/mobile hiển thị đúng.
- [ ] Có unit, integration, E2E và regression test.
- [x] Có audit và application log.
- [ ] UAT được Admin vận hành giải xác nhận.

## 30. Ước lượng sơ bộ

Ước lượng cho một lập trình viên full-stack đã hiểu dự án, chưa bao gồm thời gian chờ duyệt nghiệp vụ:

| Hạng mục | Ước lượng |
|---|---:|
| Chốt nghiệp vụ và thiết kế dữ liệu | 2–4 ngày công |
| Database, model, migration, audit | 4–6 ngày công |
| Template API và versioning | 4–6 ngày công |
| Validation engine | 4–6 ngày công |
| Template Builder UI | 6–10 ngày công |
| Seeding và preview | 4–6 ngày công |
| Apply engine và idempotency | 5–8 ngày công |
| BYE, referee readiness, reset/reseed | 4–7 ngày công |
| Test, regression, UAT và rollout | 6–9 ngày công |
| Tổng sơ bộ | 39–62 ngày công |

Có thể rút gọn MVP xuống khoảng 24–35 ngày công nếu phiên bản đầu chỉ hỗ trợ:

- Knockout 4/8/16/32 đội.
- Số đội chính xác theo capacity.
- Chưa hỗ trợ BYE.
- Chưa hỗ trợ vòng bảng.
- Chưa có canvas kéo nối nâng cao.

## 31. Các quyết định cần được duyệt trước khi code

- [x] Giải bắt buộc phải khóa đăng ký trước khi apply — Chốt: bắt buộc khóa.
- [x] Đăng ký chưa thanh toán có được đưa vào seed hay không — Chốt: loại nếu giải có phí.
- [x] Có cho template nhận ít đội hơn capacity không — Chốt: có nếu đủ minimum và template cho phép BYE.
- [x] Chính sách BYE chuẩn cần áp dụng — Chốt: seed placement chuẩn, ưu tiên seed cao.
- [x] Có gửi thông báo cho đội được auto-advance hay không — Chốt: MVP không gửi.
- [x] Trận sinh tự động có được để trống trọng tài hay không — Chốt: được phép để trống.
- [x] Có cho sửa source/đội trực tiếp sau khi apply không — Chốt: không cho sửa trong MVP.
- [x] Có cho reset khi đã xếp lịch nhưng chưa nhập điểm không — Chốt: cho phép nếu trận chưa đến giờ bắt đầu và chưa có dữ liệu điểm/phụ thuộc.
- [x] Template vòng bảng hỗ trợ một lượt hay cả hai lượt trong MVP — Chốt: một lượt.
- [x] Có cần trận tranh hạng ba trong template mặc định hay không — Chốt: là tùy chọn khi sinh template.
- [x] Có hiển thị template draft cho tất cả Admin hay chỉ người tạo — Chốt: tất cả Admin.
- [x] Có cần scope template theo đơn vị tổ chức trong phiên bản đầu hay không — Chốt: chưa áp dụng scope ở phiên bản đầu.

Khuyến nghị mặc định:

- Khóa đăng ký trước khi apply.
- Chỉ lấy registration thành công và đủ điều kiện thanh toán/xác nhận.
- Cho phép ít đội hơn capacity nếu template bật BYE.
- Cho phép trận sinh tự động chưa có trọng tài.
- Không cho sửa trực tiếp đội/source; sử dụng reseed khi chưa thi đấu.
- Không cho reset ngay khi đã có điểm hoặc lịch sử điểm.
- Published version là bất biến.
- Apply luôn có preview và chạy trong một transaction.
