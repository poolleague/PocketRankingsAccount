using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using PocketRankingsAccount.Services;

namespace PocketRankingsAccount.Controllers;

public class LoginViewModel
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}

public class SignupViewModel
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Error { get; set; }
}

public class AccountController : Controller
{
    private readonly AccountSecurityFoundationService _security;

    public AccountController(AccountSecurityFoundationService security)
    {
        _security = security;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var now = DateTime.UtcNow;
        var result = _security.Login(model.UserName, model.Password, now);

        if (result.Status != LoginStatus.Success || result.SessionToken is null || result.Person is null)
        {
            model.Error = result.Status switch
            {
                LoginStatus.LockedOut => "Too many failed attempts. Try again in a few minutes.",
                LoginStatus.AccountInactive => "This account is not active.",
                _ => "Incorrect username or password."
            };
            model.Password = "";
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.Person.PersonId.ToString()),
            new(ClaimTypes.Name, result.Person.DisplayName),
            new(AccountSecurityFoundationService.SessionCookieClaim, result.SessionToken)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        if (Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl!);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Signup() => View(new SignupViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Signup(SignupViewModel model)
    {
        var now = DateTime.UtcNow;
        var result = _security.Signup(model.UserName, model.Password, model.DisplayName, now);

        if (result.Status != SignupStatus.Success || result.Person is null)
        {
            model.Error = result.Error ?? "Could not create that account.";
            model.Password = "";
            return View(model);
        }

        // Reuses Login rather than adding a separate "create a session for
        // an already-authenticated person" method on the service. This
        // does re-verify the password we just hashed two lines above --
        // one redundant PBKDF2 pass (~100k iterations, roughly a hundred
        // milliseconds), paid once at signup. Not free, but not worth a
        // second service method to avoid either.
        var loginResult = _security.Login(model.UserName, model.Password, now);
        if (loginResult.Status != LoginStatus.Success || loginResult.SessionToken is null)
        {
            // Extremely unlikely (the credential we just created should
            // always verify), but fail toward "go log in manually" rather
            // than a confusing error if it somehow happens.
            return RedirectToAction("Login");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.Person.PersonId.ToString()),
            new(ClaimTypes.Name, result.Person.DisplayName),
            new(AccountSecurityFoundationService.SessionCookieClaim, loginResult.SessionToken)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var sessionHandle = User.FindFirst(AccountSecurityFoundationService.SessionCookieClaim)?.Value;
        if (!string.IsNullOrEmpty(sessionHandle))
            _security.Logout(sessionHandle, DateTime.UtcNow);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}
