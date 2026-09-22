using HanakaServer.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers;

[Authorize(AuthenticationSchemes = CoordinatorPortalAuthentication.Scheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoordinatorPortalController : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login() => User.Identity?.IsAuthenticated == true
        ? RedirectToAction(nameof(Matches)) : View();

    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(Matches));

    [HttpGet]
    public IActionResult Matches() => View();
}
