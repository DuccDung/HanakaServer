using HanakaServer.ViewModels.PickleballWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers.Web
{
    [AllowAnonymous]
    public class PickleballWebAuthController : Controller
    {
        [HttpGet("/PickleballWeb/Login")]
        public async Task<IActionResult> Login([FromQuery] string? returnUrl = null, [FromQuery] string? email = null)
        {
            var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
            var redirect = await RedirectIfAuthenticatedAsync(normalizedReturnUrl);
            if (redirect != null)
            {
                return redirect;
            }

            return View("~/Views/PickleballWeb/Login.cshtml", new PickleballWebAuthPageViewModel
            {
                Title = "Đăng nhập",
                ReturnUrl = normalizedReturnUrl,
                Email = email?.Trim() ?? string.Empty
            });
        }

        [HttpGet("/PickleballWeb/ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromQuery] string? returnUrl = null, [FromQuery] string? email = null)
        {
            var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
            var redirect = await RedirectIfAuthenticatedAsync(normalizedReturnUrl);
            if (redirect != null)
            {
                return redirect;
            }

            return View("~/Views/PickleballWeb/ForgotPassword.cshtml", new PickleballWebAuthPageViewModel
            {
                Title = "Quên mật khẩu",
                BackHref = "/PickleballWeb/Login",
                BackLabel = "Đăng nhập",
                ReturnUrl = normalizedReturnUrl,
                Email = email?.Trim() ?? string.Empty
            });
        }

        [HttpGet("/PickleballWeb/Register")]
        public async Task<IActionResult> Register([FromQuery] string? returnUrl = null)
        {
            var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
            var redirect = await RedirectIfAuthenticatedAsync(normalizedReturnUrl);
            if (redirect != null)
            {
                return redirect;
            }

            return View("~/Views/PickleballWeb/Register.cshtml", new PickleballWebAuthPageViewModel
            {
                Title = "Đăng ký",
                BackHref = "/PickleballWeb/Login",
                BackLabel = "Đăng nhập",
                ReturnUrl = normalizedReturnUrl
            });
        }

        [HttpGet("/PickleballWeb/RegisterOtp")]
        public async Task<IActionResult> RegisterOtp(
            [FromQuery] string? email = null,
            [FromQuery] string? fullName = null,
            [FromQuery] bool agreedToTerms = false,
            [FromQuery] string? returnUrl = null)
        {
            var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
            var redirect = await RedirectIfAuthenticatedAsync(normalizedReturnUrl);
            if (redirect != null)
            {
                return redirect;
            }

            return View("~/Views/PickleballWeb/RegisterOtp.cshtml", new PickleballWebAuthPageViewModel
            {
                Title = "Xác thực OTP",
                BackHref = "/PickleballWeb/Register",
                BackLabel = "Đăng ký",
                ReturnUrl = normalizedReturnUrl,
                Email = email?.Trim() ?? string.Empty,
                FullName = fullName?.Trim() ?? string.Empty,
                AgreedToTerms = agreedToTerms
            });
        }

        [HttpGet("/PickleballWeb/Account")]
        public IActionResult Account()
        {
            return View("~/Views/PickleballWeb/Account.cshtml", new PickleballWebAuthPageViewModel
            {
                Title = "Thông tin tài khoản",
                BackHref = "/",
                BackLabel = "Trang chủ"
            });
        }

        [HttpGet("/PickleballWeb/ChangePassword")]
        public IActionResult ChangePassword()
        {
            return View("~/Views/PickleballWeb/ChangePassword.cshtml", new PickleballWebAuthPageViewModel
            {
                Title = "Đổi mật khẩu",
                BackHref = "/PickleballWeb/Account",
                BackLabel = "Tài khoản"
            });
        }

        [HttpGet("/PickleballWeb/CommunitySafety")]
        public IActionResult CommunitySafety()
        {
            return View("~/Views/PickleballWeb/CommunitySafety.cshtml", new PickleballWebAuthPageViewModel
            {
                Title = "An toàn cộng đồng",
                BackHref = "/PickleballWeb/Account",
                BackLabel = "Tài khoản"
            });
        }

        private async Task<IActionResult?> RedirectIfAuthenticatedAsync(string returnUrl)
        {
            var authResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (authResult.Succeeded && authResult.Principal != null)
            {
                return LocalRedirect(returnUrl);
            }

            return null;
        }

        private static string NormalizeReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                return "/";
            }

            if (returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
            {
                return returnUrl;
            }

            return "/";
        }
    }
}
