namespace HanakaServer.Options;

public sealed class SepayOptions
{
    public const string SectionName = "SePay";

    public string ApiBaseUrl { get; set; } = "https://my.sepay.vn";

    public string ApiToken { get; set; } = string.Empty;

    public int? BankAccountId { get; set; }

    public string QrBaseUrl { get; set; } = "https://qr.sepay.vn";

    public string ReceiverBankShortName { get; set; } = "MBBank";

    public string ReceiverBankName { get; set; } = "MBBank";

    public string ReceiverAccountNumber { get; set; } = "02299597799999";

    public string ReceiverAccountName { get; set; } = "NGUYEN XUAN PHONG";

    public string WebhookApiKey { get; set; } = string.Empty;

    public string TransferCodePrefix { get; set; } = "HNK";

    public int PaymentExpireMinutes { get; set; } = 15;
}
