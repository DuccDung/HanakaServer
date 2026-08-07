using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HanakaServer.Services.Payments;

public sealed class SepaySettingsProvider
{
    private readonly PickleballDbContext _db;
    private readonly SepayOptions _fallback;
    private readonly ILogger<SepaySettingsProvider> _logger;

    public SepaySettingsProvider(
        PickleballDbContext db,
        IOptions<SepayOptions> fallbackOptions,
        ILogger<SepaySettingsProvider> logger)
    {
        _db = db;
        _fallback = fallbackOptions.Value;
        _logger = logger;
    }

    public async Task<SepayOptions> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var setting = await _db.SepaySettings
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.SepaySettingId == SepaySetting.SingletonId, cancellationToken);

            return setting is null ? CloneFallback() : Map(setting);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to load SePay settings from SQL Server. Falling back to appsettings configuration.");
            return CloneFallback();
        }
    }

    public SepayOptions CloneFallback()
    {
        return new SepayOptions
        {
            ApiBaseUrl = _fallback.ApiBaseUrl,
            ApiToken = _fallback.ApiToken,
            BankAccountId = _fallback.BankAccountId,
            QrBaseUrl = _fallback.QrBaseUrl,
            ReceiverBankShortName = _fallback.ReceiverBankShortName,
            ReceiverBankName = _fallback.ReceiverBankName,
            ReceiverAccountNumber = _fallback.ReceiverAccountNumber,
            ReceiverAccountName = _fallback.ReceiverAccountName,
            WebhookApiKey = _fallback.WebhookApiKey,
            TransferCodePrefix = _fallback.TransferCodePrefix,
            PaymentExpireMinutes = _fallback.PaymentExpireMinutes
        };
    }

    private static SepayOptions Map(SepaySetting setting)
    {
        return new SepayOptions
        {
            ApiBaseUrl = setting.ApiBaseUrl,
            ApiToken = setting.ApiToken ?? string.Empty,
            BankAccountId = setting.BankAccountId,
            QrBaseUrl = setting.QrBaseUrl,
            ReceiverBankShortName = setting.ReceiverBankShortName,
            ReceiverBankName = setting.ReceiverBankName,
            ReceiverAccountNumber = setting.ReceiverAccountNumber,
            ReceiverAccountName = setting.ReceiverAccountName,
            WebhookApiKey = setting.WebhookApiKey ?? string.Empty,
            TransferCodePrefix = setting.TransferCodePrefix,
            PaymentExpireMinutes = setting.PaymentExpireMinutes
        };
    }
}
