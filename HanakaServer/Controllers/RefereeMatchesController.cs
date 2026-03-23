using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "REFEREE,Admin")]
    public class RefereeMatchesController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}