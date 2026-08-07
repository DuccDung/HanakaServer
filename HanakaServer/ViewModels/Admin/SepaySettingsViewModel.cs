using System.ComponentModel.DataAnnotations;

namespace HanakaServer.ViewModels.Admin;

public sealed class SepaySettingsViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập API base URL.")]
    [Url(ErrorMessage = "API base URL không hợp lệ.")]
    [StringLength(500)]
    [Display(Name = "API base URL")]
    public string ApiBaseUrl { get; set; } = "https://my.sepay.vn";

    [StringLength(2000)]
    [Display(Name = "API token mới")]
    public string? ApiToken { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Bank account ID phải lớn hơn 0.")]
    [Display(Name = "Bank account ID")]
    public int? BankAccountId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập QR base URL.")]
    [Url(ErrorMessage = "QR base URL không hợp lệ.")]
    [StringLength(500)]
    [Display(Name = "QR base URL")]
    public string QrBaseUrl { get; set; } = "https://qr.sepay.vn";

    [Required(ErrorMessage = "Vui lòng nhập mã ngân hàng.")]
    [StringLength(50)]
    [Display(Name = "Mã ngân hàng")]
    public string ReceiverBankShortName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên ngân hàng.")]
    [StringLength(100)]
    [Display(Name = "Tên ngân hàng")]
    public string ReceiverBankName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số tài khoản nhận tiền.")]
    [StringLength(50)]
    [Display(Name = "Số tài khoản")]
    public string ReceiverAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên chủ tài khoản.")]
    [StringLength(255)]
    [Display(Name = "Tên chủ tài khoản")]
    public string ReceiverAccountName { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Webhook API key mới")]
    public string? WebhookApiKey { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tiền tố mã chuyển khoản.")]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Tiền tố chỉ được gồm chữ và số.")]
    [StringLength(20)]
    [Display(Name = "Tiền tố mã chuyển khoản")]
    public string TransferCodePrefix { get; set; } = "HNK";

    [Range(0, 10080, ErrorMessage = "Thời gian hết hạn phải từ 0 đến 10.080 phút.")]
    [Display(Name = "Thời gian hết hạn (phút)")]
    public int PaymentExpireMinutes { get; set; } = 15;

    public bool ClearApiToken { get; set; }

    public bool ClearWebhookApiKey { get; set; }

    public bool HasApiToken { get; set; }

    public bool HasWebhookApiKey { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
