using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers.Admin
{
    [Authorize(Roles = "Admin")]
    public class CourtsController : Controller
    {
        [HttpGet("/Admin/Courts")]
        public IActionResult Index()
        {
            return View();
        }
    }
}