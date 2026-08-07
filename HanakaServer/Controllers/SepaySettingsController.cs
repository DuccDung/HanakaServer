using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.Services.Payments;
using HanakaServer.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers;

[Authorize(Roles = "Admin")]
public sealed class SepaySettingsController : Controller
{
    private readonly PickleballDbContext _db;
    private readonly SepaySettingsProvider _settingsProvider;
    private readonly ILogger<SepaySettingsController> _logger;

    public SepaySettingsController(
        PickleballDbContext db,
        SepaySettingsProvider settingsProvider,
        ILogger<SepaySettingsController> logger)
    {
        _db = db;
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var setting = await _db.SepaySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SepaySettingId == SepaySetting.SingletonId, cancellationToken);

        if (setting is not null)
        {
            return View(Map(setting));
        }

        var fallback = _settingsProvider.CloneFallback();
        return View(new SepaySettingsViewModel
        {
            ApiBaseUrl = fallback.ApiBaseUrl,
            BankAccountId = fallback.BankAccountId,
            QrBaseUrl = fallback.QrBaseUrl,
            ReceiverBankShortName = fallback.ReceiverBankShortName,
            ReceiverBankName = fallback.ReceiverBankName,
            ReceiverAccountNumber = fallback.ReceiverAccountNumber,
            ReceiverAccountName = fallback.ReceiverAccountName,
            TransferCodePrefix = fallback.TransferCodePrefix,
            PaymentExpireMinutes = fallback.PaymentExpireMinutes,
            HasApiToken = !string.IsNullOrWhiteSpace(fallback.ApiToken),
            HasWebhookApiKey = !string.IsNullOrWhiteSpace(fallback.WebhookApiKey)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        SepaySettingsViewModel model,
        CancellationToken cancellationToken)
    {
        var setting = await _db.SepaySettings
            .SingleOrDefaultAsync(item => item.SepaySettingId == SepaySetting.SingletonId, cancellationToken);

        var fallback = _settingsProvider.CloneFallback();
        var currentApiToken = setting?.ApiToken ?? fallback.ApiToken;
        var currentWebhookApiKey = setting?.WebhookApiKey ?? fallback.WebhookApiKey;

        if (!ModelState.IsValid)
        {
            RemoveSecretValuesFromModelState();
            PrepareSecretState(model, currentApiToken, currentWebhookApiKey);
            return View(model);
        }

        setting ??= new SepaySetting { SepaySettingId = SepaySetting.SingletonId };
        if (_db.Entry(setting).State == EntityState.Detached)
        {
            _db.SepaySettings.Add(setting);
        }

        setting.ApiBaseUrl = model.ApiBaseUrl.Trim().TrimEnd('/');
        setting.BankAccountId = model.BankAccountId;
        setting.QrBaseUrl = model.QrBaseUrl.Trim().TrimEnd('/');
        setting.ReceiverBankShortName = model.ReceiverBankShortName.Trim();
        setting.ReceiverBankName = model.ReceiverBankName.Trim();
        setting.ReceiverAccountNumber = model.ReceiverAccountNumber.Trim();
        setting.ReceiverAccountName = model.ReceiverAccountName.Trim();
        setting.TransferCodePrefix = model.TransferCodePrefix.Trim().ToUpperInvariant();
        setting.PaymentExpireMinutes = model.PaymentExpireMinutes;
        setting.ApiToken = ResolveSecret(model.ApiToken, model.ClearApiToken, currentApiToken);
        setting.WebhookApiKey = ResolveSecret(model.WebhookApiKey, model.ClearWebhookApiKey, currentWebhookApiKey);
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedBy = User.Identity?.Name;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Unable to save SePay settings.");
            ModelState.AddModelError(string.Empty, "Không thể lưu cấu hình Sepay vào SQL Server.");
            RemoveSecretValuesFromModelState();
            PrepareSecretState(model, currentApiToken, currentWebhookApiKey);
            return View(model);
        }

        TempData["SepaySettingsSuccess"] = "Đã lưu cấu hình Sepay. Các yêu cầu thanh toán mới sẽ dùng cấu hình này.";
        return RedirectToAction(nameof(Index));
    }

    private static SepaySettingsViewModel Map(SepaySetting setting)
    {
        return new SepaySettingsViewModel
        {
            ApiBaseUrl = setting.ApiBaseUrl,
            BankAccountId = setting.BankAccountId,
            QrBaseUrl = setting.QrBaseUrl,
            ReceiverBankShortName = setting.ReceiverBankShortName,
            ReceiverBankName = setting.ReceiverBankName,
            ReceiverAccountNumber = setting.ReceiverAccountNumber,
            ReceiverAccountName = setting.ReceiverAccountName,
            TransferCodePrefix = setting.TransferCodePrefix,
            PaymentExpireMinutes = setting.PaymentExpireMinutes,
            HasApiToken = !string.IsNullOrWhiteSpace(setting.ApiToken),
            HasWebhookApiKey = !string.IsNullOrWhiteSpace(setting.WebhookApiKey),
            UpdatedAt = setting.UpdatedAt,
            UpdatedBy = setting.UpdatedBy
        };
    }

    private static string? ResolveSecret(string? newValue, bool clear, string? currentValue)
    {
        if (clear)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(newValue) ? currentValue : newValue.Trim();
    }

    private static void PrepareSecretState(
        SepaySettingsViewModel model,
        string? currentApiToken,
        string? currentWebhookApiKey)
    {
        model.ApiToken = null;
        model.WebhookApiKey = null;
        model.HasApiToken = !model.ClearApiToken && !string.IsNullOrWhiteSpace(currentApiToken);
        model.HasWebhookApiKey = !model.ClearWebhookApiKey && !string.IsNullOrWhiteSpace(currentWebhookApiKey);
    }

    private void RemoveSecretValuesFromModelState()
    {
        ModelState.Remove(nameof(SepaySettingsViewModel.ApiToken));
        ModelState.Remove(nameof(SepaySettingsViewModel.WebhookApiKey));
    }
}
