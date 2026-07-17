using System.Security.Claims;
using System.Text.Json;
using HanakaServer.Dtos.Payments;
using HanakaServer.Services.Payments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers;

[ApiController]
[Route("api/tournament-registration-payments")]
public sealed class TournamentRegistrationPaymentsController : ControllerBase
{
    private readonly TournamentRegistrationPaymentService _paymentService;

    public TournamentRegistrationPaymentsController(TournamentRegistrationPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("registrations/{registrationId:long}/checkout")]
    public async Task<IActionResult> CreateCheckout(long registrationId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Bạn cần đăng nhập để thanh toán." });
        }

        var result = await _paymentService.CreateOrReuseCheckoutAsync(
            userId,
            registrationId,
            cancellationToken);

        if (!result.Success || result.Payment is null)
        {
            return StatusCode(result.StatusCode, new { message = result.Message });
        }

        return Ok(result.Payment);
    }

    [AllowAnonymous]
    [HttpGet("{transactionCode}")]
    public async Task<ActionResult<TournamentPaymentCheckoutResponse>> GetCheckout(
        string transactionCode,
        CancellationToken cancellationToken)
    {
        var checkout = await _paymentService.GetCheckoutByTransactionCodeAsync(transactionCode, cancellationToken);
        if (checkout is null)
        {
            return NotFound(new { message = "Không tìm thấy mã thanh toán." });
        }

        return Ok(checkout);
    }

    [AllowAnonymous]
    [HttpGet("{transactionCode}/status")]
    public async Task<ActionResult<TournamentPaymentStatusResponse>> GetStatus(
        string transactionCode,
        CancellationToken cancellationToken)
    {
        var status = await _paymentService.GetStatusAsync(transactionCode, cancellationToken);
        if (status is null)
        {
            return NotFound(new { message = "Không tìm thấy mã thanh toán." });
        }

        return Ok(status);
    }

    [AllowAnonymous]
    [HttpPost("sepay/webhook")]
    public async Task<IActionResult> HandleSepayWebhook(
        [FromBody] JsonElement payload,
        CancellationToken cancellationToken)
    {
        var rawPayload = payload.GetRawText();
        var webhookPayload = payload.Deserialize<SepayWebhookPayload>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (webhookPayload is null)
        {
            return BadRequest(new { success = false, message = "Webhook payload không hợp lệ." });
        }

        var result = await _paymentService.HandleSepayWebhookAsync(
            webhookPayload,
            rawPayload,
            Request.Headers.Authorization.ToString(),
            Request.Headers["X-Api-Key"].ToString(),
            cancellationToken);

        if (!result.Authorized)
        {
            return Unauthorized(new { success = false, message = result.Message });
        }

        return Ok(new
        {
            success = result.Success,
            processed = result.Processed,
            message = result.Message,
            transactionCode = result.TransactionCode,
            registrationId = result.RegistrationId
        });
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(uid, out userId);
    }
}
