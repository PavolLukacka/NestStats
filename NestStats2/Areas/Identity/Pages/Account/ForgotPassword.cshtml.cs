using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NestStats2.Models;
using System.Text;

namespace NestStats2.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser> _emailSender;
    private readonly EmailOptions _emailOptions;
    private readonly IWebHostEnvironment _environment;

    public ForgotPasswordModel(
        UserManager<ApplicationUser> userManager,
        Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser> emailSender,
        IOptions<EmailOptions> emailOptions,
        IWebHostEnvironment environment)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _emailOptions = emailOptions.Value;
        _environment = environment;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? LocalResetLink { get; set; }

    [TempData]
    public bool ShowLocalResetLink { get; set; }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is not null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var callbackUrl = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", code = encodedToken, email = Input.Email },
                protocol: Request.Scheme);

            if (!string.IsNullOrWhiteSpace(callbackUrl))
            {
                if (!_emailOptions.IsConfigured && _environment.IsDevelopment())
                {
                    LocalResetLink = callbackUrl;
                    ShowLocalResetLink = true;
                }

                await _emailSender.SendPasswordResetLinkAsync(user, Input.Email, callbackUrl);
            }
        }

        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
