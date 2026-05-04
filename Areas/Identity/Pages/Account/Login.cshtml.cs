using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NestStats2.Models;

namespace NestStats2.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IList<AuthenticationScheme> ExternalLogins { get; set; } = [];

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task OnGetAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        ReturnUrl = CleanReturnUrl(ReturnUrl);
        Input.RememberMe = ReadKeepSignedInPreference();

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl = CleanReturnUrl(returnUrl);
        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        var isEnglish = string.Equals(
            HttpContext.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()?.RequestCulture.UICulture.TwoLetterISOLanguageName,
            "en",
            StringComparison.OrdinalIgnoreCase);

        // Replace framework-generated English-only validation messages with locale-aware ones.
        if (!isEnglish)
            LocaliseModelErrors();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        WriteKeepSignedInPreference(Input.RememberMe);
        var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            _logger.LogInformation("User logged in.");
            return LocalRedirect(returnUrl);
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, Input.RememberMe });
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("User account locked out.");
            return RedirectToPage("./Lockout");
        }

        ModelState.AddModelError(
            string.Empty,
            isEnglish
                ? "Sign-in failed. Check your email and password."
                : "Prihlásenie sa nepodarilo. Skontroluj email a heslo.");
        return Page();
    }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }

    // Replaces the framework's English-only [Required]/[EmailAddress] messages
    // with Slovak equivalents. Called only when SK culture is active.
    private void LocaliseModelErrors()
    {
        if (ModelState.TryGetValue("Input.Email", out var emailEntry) && emailEntry.Errors.Count > 0)
        {
            emailEntry.Errors.Clear();
            ModelState.AddModelError("Input.Email",
                string.IsNullOrWhiteSpace(Input.Email)
                    ? "Pole Email je povinné."
                    : "Zadaj platnú emailovú adresu.");
        }

        if (ModelState.TryGetValue("Input.Password", out var pwEntry) && pwEntry.Errors.Count > 0)
        {
            pwEntry.Errors.Clear();
            ModelState.AddModelError("Input.Password", "Pole Heslo je povinné.");
        }
    }

    private bool ReadKeepSignedInPreference()
    {
        return Request.Cookies.TryGetValue(NestStats2.Pages.Settings.IndexModel.KeepSignedInCookieName, out var rawValue)
               && bool.TryParse(rawValue, out var parsed)
               && parsed;
    }

    private void WriteKeepSignedInPreference(bool value)
    {
        Response.Cookies.Append(
            NestStats2.Pages.Settings.IndexModel.KeepSignedInCookieName,
            value ? "true" : "false",
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(365),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
    }

    private string CleanReturnUrl(string? returnUrl)
    {
        var fallback = Url.Content("~/");
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
        {
            return fallback;
        }

        if (!Uri.TryCreate(new Uri("http://neststats.local"), returnUrl, out var uri))
        {
            return fallback;
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        var keptQuery = query
            .Where(pair =>
                !string.Equals(pair.Key, "LoadToken", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(pair.Key, "QuietRefresh", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(pair.Key, "handler", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(pair.Key, "jobId", StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)));

        var cleanPath = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        var cleanQuery = QueryString.Create(keptQuery).ToUriComponent();
        return cleanPath + cleanQuery + uri.Fragment;
    }
}
