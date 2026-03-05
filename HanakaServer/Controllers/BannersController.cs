using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class BannersController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}