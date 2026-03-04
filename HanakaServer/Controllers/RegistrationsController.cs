using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class RegistrationsController : Controller
    {
        public IActionResult Index(long tournamentId)
        {
            ViewBag.TournamentId = tournamentId;
            return View();
        }
    }
}