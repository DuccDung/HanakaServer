using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class LinksController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}