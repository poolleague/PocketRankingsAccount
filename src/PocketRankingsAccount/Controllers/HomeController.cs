using Microsoft.AspNetCore.Mvc;

namespace PocketRankingsAccount.Controllers;

// Presents a small Account hub so privacy controls are discoverable without guessing a route.
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
