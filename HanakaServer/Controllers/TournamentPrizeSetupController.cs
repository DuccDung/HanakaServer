using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    public class TournamentPrizeSetupController : Controller
    {
        [HttpGet]
        public IActionResult Index(long tournamentId)
        {
            ViewBag.TournamentId = tournamentId;
            return View();
        }
    }
}