using Microsoft.AspNetCore.Identity;

namespace Docs2MD.Web.Data;

public class AppUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
