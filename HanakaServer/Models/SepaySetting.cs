namespace HanakaServer.Models;

public sealed class SepaySetting
{
    public const int SingletonId = 1;

    public int SepaySettingId { get; set; } = SingletonId;

    public string ApiBaseUrl { get; set; } = "https://my.sepay.vn";

    public string? ApiToken { get; set; }

    public int? BankAccountId { get; set; }

    public string QrBaseUrl { get; set; } = "https://qr.sepay.vn";

    public string ReceiverBankShortName { get; set; } = "MBBank";

    public string ReceiverBankName { get; set; } = "MBBank";

    public string ReceiverAccountNumber { get; set; } = string.Empty;

    public string ReceiverAccountName { get; set; } = string.Empty;

    public string? WebhookApiKey { get; set; }

    public string TransferCodePrefix { get; set; } = "HNK";

    public int PaymentExpireMinutes { get; set; } = 15;

    public DateTime UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
