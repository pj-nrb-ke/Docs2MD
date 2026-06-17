using Docs2MD.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Docs2MD.Web.Pages.Account;

public class ExternalLoginCallbackModel(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError is not null)
            return RedirectToPage("/Account/Login", new { error = remoteError });

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
            return RedirectToPage("/Account/Login");

        // Try sign in with existing external login
        var result = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: true);

        if (result.Succeeded)
            return LocalRedirect(returnUrl ?? "/");

        // First time: auto-create account
        var email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? "";
        var name  = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email;

        var user = new AppUser { UserName = email, Email = email, DisplayName = name };
        var createResult = await userManager.CreateAsync(user);

        if (createResult.Succeeded)
        {
            await userManager.AddLoginAsync(user, info);
            await signInManager.SignInAsync(user, isPersistent: true);
            return LocalRedirect(returnUrl ?? "/");
        }

        return RedirectToPage("/Account/Login",
            new { error = string.Join("; ", createResult.Errors.Select(e => e.Description)) });
    }
}
