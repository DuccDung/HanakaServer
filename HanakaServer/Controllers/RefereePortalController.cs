using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    public class RefereePortalController : Controller
    {
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [Authorize(Roles = "REFEREE,Admin")]
        [HttpGet]
        public IActionResult Matches()
        {
            return View();
        }

        [Authorize(Roles = "REFEREE,Admin")]
        [HttpGet]
        public IActionResult RelayMatch(long id)
        {
            if (id <= 0) return NotFound();
            return LocalRedirect($"/RefereePortal/Matches/{id}");
        }
    }
}
