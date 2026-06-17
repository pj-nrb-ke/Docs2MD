using Docs2MD.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Docs2MD.Web.Pages.Account;

public class LogoutModel(SignInManager<AppUser> signInManager) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        await signInManager.SignOutAsync();
        return Redirect("/Account/Login");
    }
}
