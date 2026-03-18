using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    public class PolicyController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
