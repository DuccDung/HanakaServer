# Task: Chuyển Bracket Template sang mô hình thiết kế thủ công và tái sử dụng

## 1. Thông tin task

- Ngày lập kế hoạch: 03/08/2026
- Trạng thái: Đang triển khai — backend, Editor, apply/runtime, migration, unit test và các integration test cốt lõi đã hoàn thành; còn integration đồng thời/rollback, E2E/UAT và kiểm chứng migration trên database thật
- Mức ưu tiên: Cao
- Phạm vi: Admin Web, API, business logic, database, bracket viewer và kiểm thử
- Định hướng chính: Admin tự tạo vòng, bảng, trận và nối nguồn đội; hệ thống không tự quyết định topology
- Nguyên tắc tương thích: Không làm hỏng bracket thủ công cũ và các bracket đã được sinh từ implementation hiện tại

## 2. Mục tiêu nghiệp vụ đã chốt

Admin phải có thể thiết kế một bracket thủ công một lần và lưu thành template để dùng lại cho nhiều giải.

Luồng thiết kế template:

```text
Tạo template
    ↓
Tạo vòng đấu thủ công
    ↓
Tạo bảng/nhánh thủ công
    ↓
Tạo từng trận thủ công
    ↓
Chọn nguồn cho từng slot
    ├── Seed N
    ├── Thắng trận X
    ├── Thua trận X
    ├── Hạng N bảng X
    └── BYE được khai báo rõ ràng nếu cần
    ↓
Kiểm tra cấu trúc
    ↓
Publish template
```

Luồng dùng lại template:

```text
Giải đã có đủ đội hợp lệ
    ↓
Chọn template đã publish
    ↓
Gán đội thật vào các Seed của template
    ↓
Preview đúng cấu trúc đã thiết kế
    ↓
Áp dụng
    ↓
Sao chép nguyên Round → Group → Match và các đường đi tiếp
```

Hệ thống chỉ làm hai việc tự động:

1. Gán Registration thật vào các vị trí `SEED`.
2. Sao chép cấu trúc template và chuyển stable key thành runtime ID.

Hệ thống không được:

- Tự quyết định cần bao nhiêu vòng.
- Tự tạo thêm bảng hoặc trận ngoài template.
- Tự thay đổi cặp đấu đã được thiết kế.
- Tự thay đổi đường winner/loser/group-rank.
- Tự thu nhỏ hoặc mở rộng topology theo số đội.

## 3. Khái niệm nghiệp vụ

### 3.1. Template không chứa đội thật

Template không được tham chiếu `TournamentRegistrationId`.

Đội ở vòng đầu được biểu diễn bằng vị trí:

- `Seed 1`
- `Seed 2`
- ...
- `Seed N`

Khi áp dụng template vào giải, Admin mới gán Registration thật vào các seed này.

### 3.2. Template là bản thiết kế hoàn chỉnh

Template phải lưu được:

- Danh sách vòng và thứ tự vòng.
- Danh sách bảng/nhánh trong từng vòng.
- Danh sách trận và thứ tự trận.
- Tên/nhãn của từng trận.
- Hai slot của từng trận.
- Nguồn của từng slot.
- Trận xác định vô địch, hạng ba hoặc thứ hạng khác.
- Quan hệ đội thắng, đội thua và hạng bảng.

### 3.3. Apply không phải là sinh thiết kế

Trong phạm vi task này, từ `apply` có nghĩa:

> Sao chép một bản thiết kế đã publish vào một tournament và gắn đội thật vào các seed.

Apply không có nghĩa là hệ thống tự thiết kế bracket.

## 4. Hiện trạng có thể giữ lại

Implementation hiện tại đã có các nền tảng phù hợp:

- `BracketTemplate`.
- `BracketTemplateVersion`.
- `BracketTemplateRound`.
- `BracketTemplateGroup`.
- `BracketTemplateMatch`.
- `BracketTemplateMatchSlot`.
- Stable key cho round, group và match.
- Source type `SEED`, `WINNER_MATCH`, `LOSER_MATCH`, `GROUP_RANK`, `BYE`.
- Version draft/published.
- Template validation.
- Seed snapshot.
- `TournamentBracketApplication`.
- Transaction apply.
- Liên kết runtime record với application.
- Reset/reseed trước khi thi đấu.

Không cần bỏ toàn bộ module hiện tại. Task tập trung sửa workflow, API và UI để thiết kế thủ công trở thành luồng chính.

## 5. Khoảng cách của hệ thống hiện tại

### GAP-01 — Auto-generate đang được đặt làm luồng nổi bật

- Template Editor hiện có khu vực `Sinh nhanh`.
- Tài liệu và UI tạo cảm giác hệ thống sẽ tự thiết kế bracket.
- Hướng mới yêu cầu thiết kế thủ công là luồng chính.

### GAP-02 — Trải nghiệm tạo template chưa giống tạo bracket thật

- Admin hiện chỉnh graph bằng nhiều input nhỏ trên canvas.
- Luồng chưa thống nhất với cách Admin đang tạo vòng, bảng và trận ở bracket runtime.
- Picker nguồn trận/bảng chưa đủ trực quan cho vận hành thực tế.

### GAP-03 — Draft không lưu được trạng thái đang làm dở

- Backend hiện từ chối lưu graph nếu graph còn lỗi.
- Khi thiết kế thủ công, trạng thái trung gian thường chưa hoàn chỉnh.
- Admin phải được lưu draft chưa hợp lệ; chỉ publish mới bắt buộc hợp lệ.

### GAP-04 — Metadata template bị mất khi tạo runtime

Runtime hiện chưa lưu đầy đủ:

- `RoundType`.
- `GroupType`.
- `MatchLabel`.
- `IsTerminal`.
- `TerminalType`.

Điều này làm các trận như `Tranh hạng ba` có thể không còn nhãn rõ ràng sau khi apply.

### GAP-05 — Validation vòng bảng chưa xác nhận đủ lịch đấu

- Hiện mới kiểm tra cặp đấu bị trùng.
- Chưa kiểm tra một bảng vòng tròn đã có đủ tất cả các cặp cần thiết.
- Một bảng thiếu trận vẫn có thể được publish.

### GAP-06 — Cách chia seed vòng bảng hiện tại không phù hợp làm mặc định

- Generator hiện chia seed tuần tự theo bảng.
- Nếu seed theo trình, các đội mạnh có thể bị dồn vào cùng bảng.
- Hướng mới không dựa vào generator; Admin tự quyết định seed nào thuộc bảng nào.

### GAP-07 — Propagation chưa có cơ chế phục hồi chắc chắn

- Kết quả trận được lưu trước, propagation chạy sau.
- Lỗi propagation không làm rollback kết quả và chưa có retry.
- Có khả năng kết quả đã hoàn thành nhưng đội ở trận sau vẫn chưa được resolve.

### GAP-08 — Template default seeding chưa được wizard tôn trọng

- Màn hình áp dụng luôn chọn `REGISTRATION_ORDER`.
- `DefaultSeedingMethod` của template chưa thực sự quyết định lựa chọn ban đầu.

## 6. Phạm vi triển khai

### 6.1. Phạm vi bắt buộc

- Thiết kế template hoàn toàn thủ công.
- Tạo/sửa/xóa vòng trong draft.
- Tạo/sửa/xóa bảng hoặc nhánh trong draft.
- Tạo/sửa/xóa trận trong draft.
- Chọn nguồn riêng cho hai slot.
- Hỗ trợ seed, winner, loser, group rank và BYE khai báo trước.
- Lưu draft chưa hoàn chỉnh.
- Validation riêng, không chặn lưu draft.
- Chặn publish nếu còn error.
- Published version bất biến.
- Áp dụng đúng nguyên trạng template.
- Gán đội thật vào seed.
- Giữ nhãn và loại trận sau khi apply.
- Không ảnh hưởng bracket thủ công cũ.
- Không ảnh hưởng application cũ.

### 6.2. Phạm vi tùy chọn

- Giữ `Sinh nhanh` dưới dạng công cụ hỗ trợ phụ.
- Nếu giữ, kết quả sinh nhanh chỉ tạo một draft có thể chỉnh thủ công.
- Sinh nhanh không được là điều kiện bắt buộc để tạo template.

### 6.3. Ngoài phạm vi

- Không tự lập lịch ngày, giờ hoặc sân.
- Không tự phân công trọng tài.
- Không tự thay đổi topology theo số đội thực tế.
- Không tự thêm registration mới sau khi apply.
- Không cho sửa nguồn/đội trực tiếp trên runtime template đã apply.
- Không triển khai Swiss hoặc double elimination tự động trong task này.
- Không xây marketplace hoặc phân quyền theo đơn vị tổ chức trong task này.

## 7. Backlog triển khai

## BTM-01 — Chốt mô hình template thủ công

### Mục tiêu

Chốt template là một graph do Admin tự xây dựng, không phải tham số đầu vào cho một generator.

### Công việc

- [x] Chốt tên nghiệp vụ cho `Round`, `Group/Branch`, `Match`, `Slot`.
- [x] Chốt stable key là định danh liên kết trong một version.
- [x] Chốt template không chứa Registration ID thật.
- [x] Chốt một match luôn có đúng hai slot.
- [x] Chốt source type được phép.
- [x] Chốt quy tắc source phải đến từ cấu trúc đứng trước.
- [x] Chốt published version là bất biến.
- [x] Chốt apply chỉ sao chép topology đã publish.

### Acceptance criteria

- Có một tài liệu định nghĩa duy nhất cho template thủ công.
- Không còn mô tả mơ hồ rằng apply sẽ tự sinh topology mới.
- Mọi API/UI mới tuân theo cùng khái niệm.

## BTM-02 — Thiết kế lại lifecycle lưu draft

### Mục tiêu

Admin có thể lưu công việc đang làm dở mà không cần graph hoàn chỉnh.

### Công việc

- [x] Cho phép draft chưa có round.
- [x] Cho phép round tạm thời chưa có group.
- [x] Cho phép group tạm thời chưa có match.
- [x] Cho phép match tạm thời chưa cấu hình xong source.
- [x] Tách `Save Draft` khỏi `Validate`.
- [x] Save Draft trả warning/error hiện tại nhưng không từ chối chỉ vì graph chưa hoàn chỉnh.
- [x] Publish bắt buộc chạy validation đầy đủ.
- [x] Chỉ error mới chặn publish; warning yêu cầu Admin xác nhận.
- [x] Giữ optimistic concurrency và cảnh báo khi draft bị Admin khác thay đổi.
- [x] Không cho sửa graph của version đã publish.

### Acceptance criteria

- Admin tạo một round rỗng và lưu draft thành công.
- Admin thoát trang rồi mở lại vẫn thấy đúng trạng thái đang làm dở.
- Draft có lỗi không được publish.
- Published version không thể bị sửa bằng UI hoặc gọi API trực tiếp.

## BTM-03 — API CRUD thủ công cho cấu trúc template

### Mục tiêu

Cách thao tác template tương đương cách thao tác round/group/match runtime.

### Công việc

- [x] API tạo/sửa/xóa template round.
- [x] API tạo/sửa/xóa template group/branch.
- [x] API tạo/sửa/xóa template match.
- [x] API cập nhật từng slot của match.
- [x] API trả source options hợp lệ cho một match.
- [x] API trả danh sách seed đã sử dụng/chưa sử dụng.
- [x] API trả các match nguồn thuộc vòng/nhánh trước.
- [x] API trả các group nguồn hợp lệ cho `GROUP_RANK`.
- [x] Chặn thao tác CRUD khi version không còn là draft.
- [x] Chặn xóa một match/group đang được tham chiếu hoặc trả danh sách dependency để Admin xác nhận sửa.
- [x] Giữ API graph tổng thể để preview, validate và apply.

### Nguyên tắc API

- Thao tác nhỏ phải lưu được ngay.
- Mỗi response trả lại row version mới.
- Lỗi nghiệp vụ phải có code ổn định.
- Không dùng Registration ID trong API template.

### Acceptance criteria

- Admin có thể xây toàn bộ template bằng CRUD mà không dùng generator.
- Refresh trang sau mỗi thao tác vẫn đọc được đúng dữ liệu.
- Hai Admin sửa cùng draft không ghi đè âm thầm.

## BTM-04 — Giao diện Template Editor theo cách quản lý bracket thật

### Mục tiêu

Admin sử dụng cách tạo template gần giống màn hình tạo vòng, bảng và trận hiện tại.

### Công việc

- [x] Có chế độ `Thiết kế` và `Xem trước`.
- [x] Panel quản lý cấu trúc hiển thị danh sách vòng.
- [x] Action `Tạo vòng`.
- [x] Trong mỗi vòng có action `Tạo bảng/nhánh`.
- [x] Trong mỗi bảng có action `Tạo trận`.
- [x] Dùng drawer/modal tạo trận tương tự bracket runtime.
- [x] Hiển thị rõ mã ổn định và tên hiển thị của round/group/match.
- [x] Hiển thị hai slot trên match card.
- [x] Vẽ đường nối winner/loser/group-rank.
- [x] Cho focus từ validation issue tới đúng round/group/match/slot.
- [x] Có trạng thái `Đang lưu`, `Đã lưu`, `Có xung đột`, `Draft có lỗi`.
- [x] Đưa `Sinh nhanh` ra khỏi luồng chính.
- [x] Nếu giữ `Sinh nhanh`, đặt trong menu công cụ phụ và cảnh báo thay thế draft.

### Acceptance criteria

- Một Admin đã biết tạo bracket thủ công có thể học tạo template mà không cần hiểu graph kỹ thuật.
- Admin nhìn được trận nguồn và trận đích trực tiếp trên sơ đồ.
- Không cần dùng SQL hoặc sửa JSON để hoàn thiện template.

## BTM-05 — Form tạo/sửa trận trong template

### Mục tiêu

Mỗi slot được cấu hình thủ công và dễ hiểu.

### Source của slot

#### `SEED`

- [x] Nhập/chọn Seed N.
- [x] Không chứa Registration ID.
- [x] Hiển thị `Seed N` trên match card.

#### `WINNER_MATCH`

- [x] Chọn một trận nguồn hợp lệ.
- [x] Hiển thị `Thắng trận {label/key}`.
- [x] Vẽ đường winner tới slot đích.

#### `LOSER_MATCH`

- [x] Chọn một trận nguồn hợp lệ.
- [x] Hiển thị `Thua trận {label/key}`.
- [x] Vẽ đường loser với màu phân biệt.

#### `GROUP_RANK`

- [x] Chọn bảng nguồn.
- [x] Nhập/chọn hạng N.
- [x] Hiển thị `Hạng N · {tên bảng}`.

#### `BYE`

- [x] BYE phải do Admin khai báo rõ hoặc đến từ seed trống theo policy đã chọn.
- [x] Hiển thị `Miễn đấu`.
- [x] Không cho cả hai slot đều BYE.

### Quy tắc picker

- [x] Không hiển thị chính match hiện tại làm nguồn.
- [x] Ưu tiên chỉ hiển thị match/group nằm trước.
- [x] Hiển thị round, group và label để tránh chọn nhầm.
- [x] Cảnh báo nếu đổi/xóa source đang được nhiều match sử dụng.
- [x] Không cho hai slot dùng cùng một đội/seed khi điều đó chắc chắn tạo cặp trùng.

### Acceptance criteria

- Admin tạo được knockout hoàn toàn bằng thao tác thủ công.
- Admin tạo được trận tranh hạng ba từ hai loser bán kết.
- Admin tạo được vòng bảng sang knockout bằng `GROUP_RANK`.

## BTM-06 — Validation cho template thủ công

### Mục tiêu

Validation bảo vệ publish nhưng không cản trở quá trình lưu draft.

### Error bắt buộc

- [x] Stable key round bị trùng.
- [x] Stable key group bị trùng.
- [x] Stable key match bị trùng.
- [x] Match không có đúng hai slot khi publish.
- [x] Source type không hợp lệ.
- [x] Seed ngoài phạm vi.
- [x] Cùng một seed bị dùng sai ở các trận đầu vào.
- [x] Source match không tồn tại.
- [x] Source group không tồn tại.
- [x] Match tự tham chiếu.
- [x] Source nằm sau target.
- [x] Dependency cycle.
- [x] Cả hai slot đều BYE.
- [x] Group rank không hợp lệ.
- [x] Hai slot chắc chắn resolve về cùng một registration/seed.
- [x] Không có đủ seed bắt buộc để apply.

### Validation vòng bảng

- [x] Xác định tập seed tham gia từng bảng.
- [x] Kiểm tra không có cặp trùng.
- [x] Nếu group type là `ROUND_ROBIN`, kiểm tra đủ tất cả cặp một lượt.
- [x] Hạng được tham chiếu không vượt số đội của bảng.
- [x] Chỉ resolve `GROUP_RANK` sau khi toàn bộ lịch bảng hoàn thành.

### Warning

- [x] Match không dẫn tới downstream và không phải terminal.
- [x] Không có trận vô địch.
- [x] Có nhiều trận xác định vô địch.
- [x] Winner của một trận đi tới nhiều đích.
- [x] Loser không có nơi đi trong template có nhánh thua.
- [x] Seed chưa được sử dụng.
- [x] Nhánh knockout mất cân đối.

> Ghi chú triển khai: `Không có trận vô địch`, `nhiều trận vô địch` và `Seed chưa được sử dụng`
> được nâng thành `ERROR` theo quyết định chốt dải Seed `1..N` và đúng một terminal `CHAMPION`.

### Acceptance criteria

- Draft lỗi vẫn lưu được.
- Publish bị chặn khi có error.
- Warning được hiển thị rõ và có thể publish sau xác nhận.
- Validation issue dẫn Admin tới đúng vị trí cần sửa.

## BTM-07 — Publish, versioning và thư viện template

### Mục tiêu

Template đã hoàn thiện được tái sử dụng an toàn.

### Công việc

- [x] Validate trước publish.
- [x] Tạo configuration hash cho graph đã publish.
- [x] Khóa version sau publish.
- [x] Tạo draft version mới bằng cách clone version published.
- [x] Giữ lịch sử version.
- [x] Cho clone một version thành template khác.
- [x] Archive template nhưng không ảnh hưởng giải đã apply.
- [x] Hiển thị số lần template/version đã được sử dụng.
- [x] Hiển thị rõ template thủ công, không gắn nhãn auto-generated.

### Acceptance criteria

- Version published không thay đổi khi draft mới được chỉnh.
- Giải đã apply không thay đổi khi template có version mới.
- Template archived không được chọn cho giải mới.

## BTM-08 — Gán đội thật vào seed khi áp dụng

### Mục tiêu

Đội thật chỉ được đưa vào các vị trí seed đã được Admin thiết kế.

### Công việc

- [x] Đọc danh sách seed bắt buộc từ graph.
- [x] Hiển thị số seed cần gán.
- [x] Chỉ lấy registration đủ điều kiện.
- [x] Khóa đăng ký trước apply.
- [x] Cho gán theo thứ tự đăng ký.
- [x] Cho gán ngẫu nhiên.
- [x] Cho Admin sắp xếp thủ công.
- [x] Cho gán theo trình nếu nghiệp vụ tiếp tục sử dụng.
- [x] Mặc định UI theo `DefaultSeedingMethod` của template.
- [x] Hiển thị đội chưa gán và seed chưa có đội.
- [x] Không cho một registration vào nhiều seed.
- [x] Không cho thiếu đội nếu template yêu cầu đủ seed.
- [x] Không tự thu nhỏ bracket khi thiếu đội.

### Chính sách số đội đề xuất cho MVP

- Template có `N` seed bắt buộc thì cần đúng `N` đội được gán.
- Nếu muốn tổ chức với số đội khác, Admin chọn template khác.
- BYE chỉ được dùng khi template đã thiết kế/publish cho phép BYE.
- Thiếu đội không được làm hệ thống tự xóa trận hoặc đổi topology.

### Acceptance criteria

- Template 8 seed chỉ apply khi 8 seed đã được gán hợp lệ, trừ policy BYE được khai báo trước.
- Thứ tự đội trong preview giống hoàn toàn kết quả apply.
- Registration mới sau apply không tự đi vào bracket.

## BTM-09 — Preview và checksum

### Mục tiêu

Admin nhìn thấy chính xác dữ liệu sẽ được tạo.

### Công việc

- [x] Preview hiển thị nguyên round/group/match của published version.
- [x] Preview hiển thị đúng `MatchLabel`.
- [x] Preview hiển thị winner/loser/group-rank bằng label dễ hiểu.
- [x] Preview hiển thị seed và đội thật.
- [x] Preview hiển thị BYE.
- [x] Preview hiển thị terminal/champion/third-place.
- [x] Preview checksum bao gồm template version/hash.
- [x] Checksum bao gồm seed-to-registration mapping.
- [x] Checksum bao gồm snapshot cần thiết của đội để phát hiện đổi thành viên trước apply.
- [x] Apply từ chối nếu dữ liệu khác preview.
- [x] Admin phải xác nhận đã kiểm tra đội và đường đi tiếp.

### Acceptance criteria

- Preview và runtime sau apply có cùng số vòng, bảng, trận và source.
- Không thể thay thành viên registration sau preview rồi apply bằng token cũ.

## BTM-10 — Apply nguyên trạng template vào runtime

### Mục tiêu

Không sinh topology; chỉ sao chép topology đã được thiết kế.

### Công việc

- [x] Tạo application và seed snapshot.
- [x] Sao chép từng template round thành runtime round.
- [x] Sao chép từng template group thành runtime group.
- [x] Sao chép từng template match thành runtime match.
- [x] Map `SEED` thành Registration ID.
- [x] Map `WINNER_MATCH` thành runtime Match ID.
- [x] Map `LOSER_MATCH` thành runtime Match ID.
- [x] Map `GROUP_RANK` thành runtime Group ID và rank.
- [x] Map `BYE` mà không nhập điểm.
- [x] Không tạo thêm entity ngoài graph.
- [x] Không bỏ entity có trong graph.
- [x] Chạy transaction và chống apply trùng.
- [x] Health check sau apply.

### Acceptance criteria

- Số round/group/match runtime bằng đúng template.
- Mọi source runtime trỏ đúng entity cùng application.
- Không còn template entity ID trong source runtime.
- Retry không tạo bracket trùng.

## BTM-11 — Bảo toàn metadata nghiệp vụ ở runtime

### Mục tiêu

Bracket sau apply vẫn giữ nguyên ý nghĩa mà Admin đã thiết kế.

### Công việc

- [x] Chốt cách lưu hoặc truy xuất `RoundType`.
- [x] Chốt cách lưu hoặc truy xuất `GroupType`.
- [x] Chốt cách lưu hoặc truy xuất `MatchLabel`.
- [x] Chốt cách lưu hoặc truy xuất `IsTerminal`.
- [x] Chốt cách lưu hoặc truy xuất `TerminalType`.
- [x] Viewer Admin hiển thị nhãn trận.
- [x] Viewer public/mobile hiển thị nhãn phù hợp.
- [x] Trận tranh hạng ba không bị hiển thị như trận chung kết thứ hai.
- [x] API runtime trả metadata cần thiết mà không yêu cầu client đọc bảng template trực tiếp.

### Acceptance criteria

- `Chung kết` và `Tranh hạng ba` được phân biệt rõ sau apply.
- Template archive hoặc có version mới không làm mất metadata của application cũ.

## BTM-12 — Propagation và tính nhất quán sau khi nhập điểm

### Mục tiêu

Đường đi tiếp đã thiết kế phải hoạt động tin cậy.

### Công việc

- [x] Winner được đẩy đúng tới mọi slot `WINNER_MATCH`.
- [x] Loser được đẩy đúng tới mọi slot `LOSER_MATCH`.
- [x] Group rank chỉ resolve khi bảng hoàn thành.
- [x] BYE tự đi tiếp.
- [x] Không cập nhật target đã completed.
- [x] Có trạng thái/log rõ khi propagation chưa hoàn thành.
- [x] Có cơ chế retry/recalculate an toàn.
- [x] Không để lỗi propagation bị mất hoàn toàn sau khi score đã commit.
- [x] Khi upstream đổi kết quả, xử lý nhất quán target chưa thi đấu.
- [x] Không giữ tỷ số cũ nếu đội của một target chưa completed bị thay đổi.

### Acceptance criteria

- Kết thúc một trận nguồn luôn có cách kiểm tra và phục hồi downstream.
- Không cần sửa database thủ công khi propagation tạm thời lỗi.

## BTM-13 — Chỉnh sửa runtime sau apply

### Mục tiêu

Giữ nguyên topology nhưng cho phép vận hành trận đấu.

### Công việc

- [x] Không cho thêm/xóa round thuộc application.
- [x] Không cho thêm/xóa group thuộc application.
- [x] Không cho thêm/xóa match thuộc application.
- [x] Không cho sửa seed/source/đội trực tiếp.
- [x] Cho sửa lịch thi đấu.
- [x] Cho sửa sân và địa điểm.
- [x] Cho sửa video.
- [x] Cho phân công/chỉnh trọng tài.
- [x] Cho nhập điểm theo quyền hiện tại.
- [x] Muốn đổi đội phải reset/reseed khi còn đủ điều kiện.

### Acceptance criteria

- Không thể làm lệch topology published bằng API trực tiếp.
- Admin vẫn vận hành lịch và trọng tài bình thường.

## BTM-14 — Reset và reseed

### Mục tiêu

Cho phép làm lại trước khi thi đấu mà vẫn giữ lịch sử.

### Công việc

- [x] Chỉ reset application active.
- [x] Chặn nếu có trận thường đã completed.
- [x] Chặn nếu trận đã bắt đầu.
- [x] Chặn nếu có score history.
- [x] Chặn nếu có dependency ngoài application.
- [x] Xóa đúng runtime do application sinh.
- [x] Giữ seed snapshot cũ.
- [x] Đánh dấu application `REVERTED`.
- [x] Lưu người, thời gian và lý do.
- [x] Apply lại tạo application/snapshot mới.
- [x] Xử lý timezone nhất quán khi kiểm tra trận đã bắt đầu.

### Acceptance criteria

- Reset không xóa cấu trúc thủ công ngoài application.
- Không reset được giải đã thực sự bắt đầu.
- Lịch sử các lần apply/reseed vẫn xem được.

## BTM-15 — Tương thích và migration

### Mục tiêu

Thay đổi mới không phá dữ liệu hiện tại.

### Công việc

- [ ] Kiểm kê các application đã tạo từ implementation cũ.
- [x] Giữ các cột liên kết runtime nullable.
- [x] Không bắt buộc chuyển bracket thủ công cũ thành template.
- [x] Bổ sung metadata runtime theo migration không mất dữ liệu.
- [x] Có phương án backfill metadata cho application cũ nếu có thể.
- [x] Script migration có transaction và chạy lại an toàn.
- [ ] Kiểm tra database chưa có schema bracket.
- [ ] Kiểm tra database đã có schema bracket revision hiện tại.
- [x] Có feature flag để rollout UI mới.

### Acceptance criteria

- Giải cũ vẫn xem và nhập điểm được.
- Bracket thủ công vẫn tạo được.
- Application cũ không bị mất round/group/match.

## BTM-16 — Kiểm thử

### Unit test

- [x] Lưu draft chưa hoàn chỉnh.
- [x] Publish draft lỗi bị chặn.
- [x] Stable key trùng.
- [x] Source không tồn tại.
- [x] Source từ vòng sau.
- [x] Dependency cycle.
- [x] Winner source.
- [x] Loser source.
- [x] Group rank source.
- [x] BYE.
- [x] Round-robin đủ cặp.
- [x] Round-robin thiếu cặp.
- [x] Seed bị trùng.
- [x] Seed bị thiếu.

### Integration test

- [x] CRUD thủ công round/group/match/slot.
- [x] Tạo và publish template knockout 8 seed.
- [x] Apply tạo đúng runtime.
- [x] Tranh hạng ba giữ đúng label.
- [x] Vòng bảng sang knockout.
- [ ] Apply rollback khi một bước lỗi.
- [ ] Hai Admin apply đồng thời.
- [x] Preview hết hiệu lực khi registration thay đổi.
- [x] Reset/reseed giữ lịch sử.

### UI/E2E

- [ ] Tạo template không dùng generator.
- [ ] Tạo vòng.
- [ ] Tạo bảng.
- [ ] Tạo trận.
- [ ] Nối winner.
- [ ] Nối loser.
- [ ] Nối group rank.
- [ ] Lưu draft đang làm dở.
- [ ] Validate và sửa lỗi.
- [ ] Publish.
- [ ] Chọn template cho giải.
- [ ] Gán đội vào seed.
- [ ] Preview.
- [ ] Apply.
- [ ] Nhập kết quả và kiểm tra đường đi tiếp.
- [ ] Kiểm tra Admin/public/mobile.

### Regression

- [ ] Tạo bracket thủ công cũ.
- [ ] Sửa trận thủ công.
- [ ] Nhập điểm bởi Admin.
- [ ] Nhập điểm bởi trọng tài.
- [ ] Standings hiện tại.
- [ ] Public bracket hiện tại.
- [ ] Reset application cũ.

## 8. Thứ tự triển khai đề xuất

### Phase 1 — Chốt nghiệp vụ

1. `BTM-01`
2. Chốt các quyết định tại mục 10.

### Phase 2 — Nền tảng draft thủ công

1. `BTM-02`
2. `BTM-03`
3. `BTM-06`

### Phase 3 — Template Editor

1. `BTM-04`
2. `BTM-05`
3. `BTM-07`

### Phase 4 — Gán đội và apply

1. `BTM-08`
2. `BTM-09`
3. `BTM-10`
4. `BTM-11`

### Phase 5 — Vận hành bracket

1. `BTM-12`
2. `BTM-13`
3. `BTM-14`

### Phase 6 — Rollout

1. `BTM-15`
2. `BTM-16`
3. UAT với Admin tổ chức giải.

## 9. Definition of Done

- [x] Admin tạo template hoàn chỉnh mà không dùng generator.
- [x] Trải nghiệm tạo round/group/match gần giống bracket runtime.
- [x] Admin nối được seed, winner, loser và group rank thủ công.
- [x] Draft chưa hoàn chỉnh lưu được.
- [x] Draft lỗi không publish được.
- [x] Published version bất biến.
- [x] Template không chứa Registration ID thật.
- [x] Đội thật chỉ được gán khi áp dụng vào giải.
- [x] Apply sao chép đúng nguyên trạng template.
- [x] Không tự thêm/bớt round/group/match.
- [x] Preview và runtime giống nhau.
- [x] Nhãn chung kết, hạng ba và xếp hạng được giữ lại.
- [x] Winner/loser/group-rank propagation đúng.
- [x] Có cơ chế kiểm tra và phục hồi propagation.
- [x] Reset/reseed an toàn trước khi thi đấu.
- [ ] Bracket thủ công và giải cũ không bị ảnh hưởng.
- [ ] Unit, integration, E2E và regression test đạt.
- [ ] UAT được Admin vận hành giải xác nhận.

## 10. Các quyết định cần chốt trước khi code

- [x] `Sinh nhanh` sẽ bị xóa hoàn toàn hay giữ làm công cụ phụ? — Giữ làm công cụ phụ, mặc định thu gọn và cảnh báo ghi đè draft.
- [x] Draft sẽ dùng API CRUD nhỏ hay vẫn lưu toàn graph sau mỗi thao tác? — Có CRUD nhỏ; Editor hiện lưu graph tổng thể để giữ thao tác liền mạch.
- [x] Template có bắt buộc dùng đủ dải Seed `1..N` hay cho phép seed rời rạc? — Bắt buộc đủ dải `1..N` khi publish.
- [x] MVP yêu cầu đúng số đội hay cho phép thiếu đội bằng BYE? — Đúng số đội nếu không bật `AllowBye`; thiếu đội chỉ được phép theo policy BYE.
- [x] BYE chỉ được khai báo trong template hay được tạo từ seed không có đội? — Hỗ trợ BYE khai báo rõ và seed trống khi `AllowBye`.
- [x] Round-robin có bắt buộc đủ mọi cặp khi publish không? — Có, đủ một lượt.
- [x] Một winner có được phép đi tới nhiều match đích không? — Cho phép nhưng phát cảnh báo.
- [x] Một loser có được phép đi tới nhiều match đích không? — Cho phép cho topology tùy chỉnh; validation vẫn kiểm tra nhánh thua bị bỏ.
- [x] Có bắt buộc đúng một trận `CHAMPION` không? — Có.
- [x] Metadata runtime sẽ được lưu thành cột snapshot hay đọc qua application/template version? — Lưu cột snapshot nullable ở runtime.
- [x] Khi source upstream đổi kết quả và target đã có điểm tạm nhưng chưa completed, xử lý tỷ số cũ thế nào? — Xóa tỷ số, winner và completion reason cũ.
- [x] Có cho Admin sửa topology runtime trước khi thi đấu hay bắt buộc reset/reseed? — Không sửa topology; bắt buộc reset/reseed.

## 11. Khuyến nghị mặc định

- Giữ `Sinh nhanh` như công cụ phụ, không đặt trong luồng chính.
- Dùng CRUD nhỏ cho round/group/match/slot và giữ graph API cho preview/validate.
- Cho lưu mọi trạng thái draft; chỉ publish mới validate cứng.
- Template khai báo dải seed rõ ràng từ `1..N`.
- MVP yêu cầu đủ đội; BYE chỉ dùng khi template đã khai báo policy rõ.
- `ROUND_ROBIN` phải đủ mọi cặp một lượt.
- Bắt buộc đúng một terminal `CHAMPION`; hạng ba là terminal riêng.
- Snapshot metadata cần thiết sang runtime/application để viewer không phụ thuộc template hiện hành.
- Runtime topology đã apply không được sửa; muốn đổi phải reset/reseed.
- Propagation phải có retry/recalculate và màn hình cảnh báo trạng thái chưa đồng bộ.
