# Task: Sepay thanh toán đăng ký giải đấu

## Mục tiêu

Thêm thanh toán Sepay cho đăng ký giải đấu Hanaka. Mỗi giải có một phí đăng ký riêng. Khi một đội/cặp đã đăng ký thành công, người dùng có thể bấm "Thanh toán" tại trang danh sách đăng ký giải, quét QR, hệ thống nhận webhook Sepay và cập nhật trạng thái thanh toán theo thời gian thực.

## Tham khảo từ SystemPurchaseLauncher

Các phần nghiệp vụ Sepay nên copy theo hướng triển khai:

- `SepayOptions`: cấu hình tài khoản nhận, QR base URL, API token, webhook API key, prefix nội dung chuyển khoản, thời hạn thanh toán.
- `SepayGatewayClient`: dựng QR URL theo mẫu `https://qr.sepay.vn/img?acc=...&bank=...&amount=...&des=...`.
- `PaymentTransaction`: lưu giao dịch pending/paid/expired, mã chuyển khoản, số tiền, QR, tài khoản nhận.
- `SepayWebhook`: lưu raw webhook trước, sau đó match vào transaction.
- `HandleSepayWebhookAsync`: chỉ xử lý `transferType = in`, check đúng tài khoản nhận, match theo mã chuyển khoản/nội dung + số tiền, idempotent nếu đã paid.
- `PaymentRealtimeHub`: bắn trạng thái thanh toán về client đang xem trang QR.
- `GET status`: polling fallback nếu realtime lỗi.

## Nghiệp vụ đề xuất cho Hanaka

1. Admin tạo/sửa giải đấu
   - Thêm trường phí đăng ký: `RegistrationFeeAmount`.
   - Mỗi giải có thể có giá khác nhau.
   - Nếu phí bằng `0`, có thể coi là miễn phí hoặc không hiện nút thanh toán.

2. Khi đăng ký/ghép cặp thành công
   - `TournamentRegistration.Success = 1`.
   - `TournamentRegistration.Paid` vẫn giữ là cờ trạng thái cuối.
   - Nếu `Paid = 0` và giải có phí > 0, web public hiển thị nút `Thanh toán`.
   - Nếu `Paid = 1`, hiển thị nhãn xanh `Đã thanh toán`.

3. Tạo giao dịch thanh toán
   - Endpoint đề xuất: `POST /api/tournament-registration-payments/registrations/{registrationId}/checkout`.
   - Chỉ người thuộc đội đó hoặc admin được tạo/xem checkout.
   - Tái sử dụng giao dịch `pending/processing` còn hạn nếu có.
   - Tạo `TransactionCode`, ví dụ: `HNK16R0002` hoặc `HNK{TournamentId}R{RegistrationId}`.
   - `TransferContent = TransactionCode`.
   - `Amount = Tournament.RegistrationFeeAmount`.
   - QR URL dùng SepayGatewayClient.

4. Trang thanh toán
   - URL đề xuất: `/PickleballWeb/Tournament/{tournamentId}/Registration/{registrationId}/Payment`.
   - Hiển thị:
     - QR thanh toán.
     - Số tiền.
     - Nội dung chuyển khoản.
     - ID đội đấu.
     - Tên đội đấu.
     - Trạng thái.
   - Kết nối realtime hub hoặc polling `/status`.

5. Webhook Sepay
   - Endpoint đề xuất: `POST /api/tournament-registration-payments/sepay/webhook`.
   - Lưu raw payload vào `TournamentSepayWebhooks`.
   - Check webhook API key giống project mẫu.
   - Match giao dịch theo:
     - `transferType = in`.
     - đúng số tài khoản nhận.
     - `amount` khớp.
     - nội dung/code/description chứa `TransactionCode` hoặc `TransferContent`.
   - Khi match:
     - `TournamentRegistrationPayments.Status = paid`.
     - set `PaidAmount`, `PaidAt`, `ProviderTransactionId`, `RawResponse`.
     - `TournamentRegistrations.Paid = 1`.
     - set `TournamentRegistrations.PaidAt`, `PaymentAmount`.
     - bắn realtime/popup "Bạn đã thanh toán thành công".

6. Trang danh sách đăng ký
   - Với đăng ký thành công:
     - `Paid = 0`: hiện nút `Thanh toán`.
     - `Paid = 1`: hiện badge xanh `Đã thanh toán`.
   - Không làm thay đổi dữ liệu waiting cũ.

## API đề xuất

- `POST /api/tournament-registration-payments/registrations/{registrationId}/checkout`
- `GET /api/tournament-registration-payments/{transactionCode}`
- `GET /api/tournament-registration-payments/{transactionCode}/status`
- `POST /api/tournament-registration-payments/sepay/webhook`
- SignalR hub: `/hubs/tournament-payments`

## File cần triển khai sau

- Options:
  - `Options/SepayOptions.cs`
- Services:
  - `Service/Payments/SepayGatewayClient.cs`
  - `Service/Payments/TournamentRegistrationPaymentService.cs`
  - `Service/Payments/TournamentPaymentRealtimeHub.cs`
- Controllers:
  - `Controllers/TournamentRegistrationPaymentsController.cs`
- Models:
  - `TournamentRegistrationPayment`
  - `TournamentSepayWebhook`
- DbContext:
  - DbSet + Fluent configuration.
- Web:
  - payment page route/view.
  - button/badge ở registration list.
  - realtime/polling JS.
- App:
  - nếu cần sau: button thanh toán và WebView/payment screen.

## Kiểm thử nghiệp vụ

- Giải phí 0: không hiện thanh toán hoặc auto coi là miễn phí.
- Đội chưa paid: hiện nút thanh toán.
- Tạo checkout 2 lần: reuse pending transaction còn hạn.
- Webhook sai account: lưu webhook, không xử lý.
- Webhook sai số tiền: lưu webhook, không xử lý.
- Webhook đúng: cập nhật paid một lần, gọi lại webhook không tạo paid duplicate.
- Quay về danh sách đăng ký: nút thanh toán đổi thành `Đã thanh toán`.
