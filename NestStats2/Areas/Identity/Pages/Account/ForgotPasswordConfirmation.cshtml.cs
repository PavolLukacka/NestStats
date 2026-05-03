using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NestStats2.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordConfirmationModel : PageModel
{
    [TempData]
    public string? LocalResetLink { get; set; }

    [TempData]
    public bool ShowLocalResetLink { get; set; }

    public void OnGet()
    {
    }
}
