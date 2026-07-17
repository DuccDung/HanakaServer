using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using HanakaServer.Options;
using Microsoft.Extensions.Options;

namespace HanakaServer.Services.Payments;

public sealed class SepayGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly SepayOptions _options;
    private readonly ILogger<SepayGatewayClient> _logger;

    public SepayGatewayClient(
        HttpClient httpClient,
        IOptions<SepayOptions> sepayOptions,
        ILogger<SepayGatewayClient> logger)
    {
        _httpClient = httpClient;
        _options = sepayOptions.Value;
        _logger = logger;
    }

    public async Task<SepayCheckoutSnapshot> PrepareCheckoutAsync(
        decimal amount,
        string transferContent,
        CancellationToken cancellationToken = default)
    {
        var receiver = await ResolveReceiverAsync(cancellationToken);
        var roundedAmount = Math.Max(0, decimal.Round(amount, 0, MidpointRounding.AwayFromZero));
        var qrImageUrl = BuildQrImageUrl(
            receiver.AccountNumber,
            receiver.BankShortName,
            roundedAmount,
            transferContent);

        return new SepayCheckoutSnapshot(
            receiver.BankName,
            receiver.BankShortName,
            receiver.AccountNumber,
            receiver.AccountName,
            qrImageUrl,
            receiver.ProviderRawResponse,
            receiver.ResolvedByApi);
    }

    private async Task<SepayReceiverSnapshot> ResolveReceiverAsync(CancellationToken cancellationToken)
    {
        var fallback = new SepayReceiverSnapshot(
            string.IsNullOrWhiteSpace(_options.ReceiverBankName) ? _options.ReceiverBankShortName : _options.ReceiverBankName,
            _options.ReceiverBankShortName,
            _options.ReceiverAccountNumber,
            _options.ReceiverAccountName,
            null,
            false);

        if (string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            return fallback;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildBankAccountLookupUrl());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken.Trim());

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "SePay bank account lookup returned status {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    rawResponse);
                return fallback with { ProviderRawResponse = rawResponse };
            }

            using var document = JsonDocument.Parse(rawResponse);
            var receiver = TryParseReceiver(document.RootElement, rawResponse);
            return receiver ?? fallback with { ProviderRawResponse = rawResponse };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to resolve SePay receiver information from API.");
            return fallback;
        }
    }

    private string BuildBankAccountLookupUrl()
    {
        var baseUrl = _options.ApiBaseUrl.TrimEnd('/');
        if (_options.BankAccountId.HasValue && _options.BankAccountId.Value > 0)
        {
            return $"{baseUrl}/api/v1/bank-accounts/{_options.BankAccountId.Value}";
        }

        return $"{baseUrl}/api/v1/bank-accounts";
    }

    private SepayReceiverSnapshot? TryParseReceiver(JsonElement root, string rawResponse)
    {
        if (TrySelectMatchingBankAccount(root, out var bankAccount))
        {
            return MapReceiver(bankAccount, rawResponse);
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "data", "items", "results" })
            {
                if (!root.TryGetProperty(propertyName, out var nestedElement))
                {
                    continue;
                }

                if (TrySelectMatchingBankAccount(nestedElement, out bankAccount))
                {
                    return MapReceiver(bankAccount, rawResponse);
                }
            }
        }

        return null;
    }

    private bool TrySelectMatchingBankAccount(JsonElement element, out JsonElement bankAccount)
    {
        if (element.ValueKind == JsonValueKind.Object && LooksLikeBankAccount(element))
        {
            bankAccount = element;
            return true;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (LooksLikeBankAccount(item) && MatchesConfiguredAccount(item))
                {
                    bankAccount = item;
                    return true;
                }
            }

            foreach (var item in element.EnumerateArray())
            {
                if (LooksLikeBankAccount(item))
                {
                    bankAccount = item;
                    return true;
                }
            }
        }

        bankAccount = default;
        return false;
    }

    private static bool LooksLikeBankAccount(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object &&
               (TryGetPropertyValue(element, "account_number") is not null ||
                TryGetPropertyValue(element, "accountNumber") is not null);
    }

    private bool MatchesConfiguredAccount(JsonElement element)
    {
        var accountNumber = TryGetPropertyValue(element, "account_number") ??
                            TryGetPropertyValue(element, "accountNumber");
        var bankShortName = TryGetPropertyValue(element, "bank_short_name") ??
                            TryGetPropertyValue(element, "bankShortName") ??
                            TryGetPropertyValue(element, "short_name") ??
                            TryGetPropertyValue(element, "shortName");

        var matchesAccount = string.IsNullOrWhiteSpace(_options.ReceiverAccountNumber) ||
                             string.Equals(accountNumber, _options.ReceiverAccountNumber, StringComparison.OrdinalIgnoreCase);
        var matchesBank = string.IsNullOrWhiteSpace(_options.ReceiverBankShortName) ||
                          string.Equals(
                              NormalizeBankAlias(bankShortName),
                              NormalizeBankAlias(_options.ReceiverBankShortName),
                              StringComparison.OrdinalIgnoreCase);

        return matchesAccount && matchesBank;
    }

    private SepayReceiverSnapshot MapReceiver(JsonElement element, string rawResponse)
    {
        var bankShortName = TryGetPropertyValue(element, "bank_short_name") ??
                            TryGetPropertyValue(element, "bankShortName") ??
                            TryGetPropertyValue(element, "short_name") ??
                            TryGetPropertyValue(element, "shortName") ??
                            _options.ReceiverBankShortName;
        var bankName = TryGetPropertyValue(element, "bank_name") ??
                       TryGetPropertyValue(element, "bankName") ??
                       bankShortName;
        var accountNumber = TryGetPropertyValue(element, "account_number") ??
                            TryGetPropertyValue(element, "accountNumber") ??
                            _options.ReceiverAccountNumber;
        var accountName = TryGetPropertyValue(element, "account_name") ??
                          TryGetPropertyValue(element, "accountName") ??
                          _options.ReceiverAccountName;

        return new SepayReceiverSnapshot(
            bankName,
            bankShortName,
            accountNumber,
            accountName,
            rawResponse,
            true);
    }

    private string BuildQrImageUrl(
        string accountNumber,
        string bankShortName,
        decimal amount,
        string transferContent)
    {
        var baseUrl = _options.QrBaseUrl.TrimEnd('/');
        var amountText = amount.ToString("0", CultureInfo.InvariantCulture);

        return $"{baseUrl}/img?acc={Uri.EscapeDataString(accountNumber)}&bank={Uri.EscapeDataString(bankShortName)}&amount={Uri.EscapeDataString(amountText)}&des={Uri.EscapeDataString(transferContent)}";
    }

    private static string? TryGetPropertyValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        return propertyValue.ValueKind switch
        {
            JsonValueKind.String => propertyValue.GetString(),
            JsonValueKind.Number => propertyValue.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static string NormalizeBankAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Concat(value
            .Trim()
            .ToUpperInvariant()
            .Where(character => char.IsLetterOrDigit(character)));

        return normalized switch
        {
            "MBBANK" => "MB",
            "MBB" => "MB",
            "MILITARYBANK" => "MB",
            _ => normalized
        };
    }
}

public sealed record SepayCheckoutSnapshot(
    string BankName,
    string BankShortName,
    string AccountNumber,
    string AccountName,
    string QrImageUrl,
    string? ProviderRawResponse,
    bool ResolvedByApi);

public sealed record SepayReceiverSnapshot(
    string BankName,
    string BankShortName,
    string AccountNumber,
    string AccountName,
    string? ProviderRawResponse,
    bool ResolvedByApi);
