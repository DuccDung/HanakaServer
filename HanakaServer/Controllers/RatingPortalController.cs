using HanakaServer.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    public class RatingPortalController : Controller
    {
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [Authorize(Roles = $"{RoleCodes.RatingAssessor},{RoleCodes.Admin}")]
        [HttpGet]
        public IActionResult Dashboard()
        {
            return View();
        }
    }
}
