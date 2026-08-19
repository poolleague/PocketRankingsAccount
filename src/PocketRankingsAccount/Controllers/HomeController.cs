using Microsoft.AspNetCore.Mvc;

namespace PocketRankingsAccount.Controllers;

// Placeholder landing action so the default MVC route resolves to
// something. Real sign-up/login UI is next-session work, once
// AccountRepository and the auth pipeline exist -- see AGENTS.md.
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return Content("Pocket Rankings Account -- scaffolding in progress. See /health.");
    }
}
