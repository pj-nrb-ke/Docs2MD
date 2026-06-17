using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Docs2MD.Web.Pages.Account;

public class LoginModel : PageModel
{
    public string? ReturnUrl { get; private set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }
}
