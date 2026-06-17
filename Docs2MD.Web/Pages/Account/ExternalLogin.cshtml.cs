using Docs2MD.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Docs2MD.Web.Pages.Account;

public class ExternalLoginModel(SignInManager<AppUser> signInManager) : PageModel
{
    // POST: initiate OAuth challenge
    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("/Account/ExternalLoginCallback",
            values: new { returnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            provider, redirectUrl);
        return Challenge(properties, provider);
    }
}
