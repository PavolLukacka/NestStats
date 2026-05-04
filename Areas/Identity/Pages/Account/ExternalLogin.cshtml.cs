using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NestStats2.Models;

namespace NestStats2.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ExternalLoginModel> _logger;

    public ExternalLoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<ExternalLoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ProviderDisplayName { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    [TempData]
    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    // Direct GET to this page has no meaning — send back to login
    public IActionResult OnGet() => RedirectToPage("./Login");

    // Login.cshtml posts the provider name here → redirect to OAuth provider
    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback",
            values: new { returnUrl = CleanReturnUrl(returnUrl) });

        var properties = _signInManager
            .ConfigureExternalAuthenticationProperties(provider, redirectUrl);

        return new ChallengeResult(provider, properties);
    }

    // OAuth provider redirects back here after user approves
    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl = CleanReturnUrl(returnUrl);

        if (remoteError != null)
        {
            ErrorMessage = $"Error from external provider: {remoteError}";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ErrorMessage = "Could not load external login information.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        // If this external login is already linked to an account — sign in directly
        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            _logger.LogInformation("{Name} signed in via {Provider}.",
                info.Principal.Identity?.Name, info.LoginProvider);
            return LocalRedirect(returnUrl);
        }

        if (signInResult.IsLockedOut)
            return RedirectToPage("./Lockout");

        // New login — try to use the email the provider gave us
        ReturnUrl = returnUrl;
        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email))
        {
            Input.Email = email;
            return await CreateOrLinkAccountAsync(info, returnUrl);
        }

        // No email from provider — show the confirmation form
        return Page();
    }

    // User submitted the email confirmation form
    public async Task<IActionResult> OnPostConfirmationAsync(string? returnUrl = null)
    {
        returnUrl = CleanReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            ReturnUrl = returnUrl;
            return Page();
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ErrorMessage = "Could not load external login information during confirmation.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        return await CreateOrLinkAccountAsync(info, returnUrl);
    }

    // ── shared logic: link existing account or register new one ──────────
    private async Task<IActionResult> CreateOrLinkAccountAsync(
        ExternalLoginInfo info, string returnUrl)
    {
        var existing = await _userManager.FindByEmailAsync(Input.Email);

        if (existing != null)
        {
            // Email already registered — link the external login to that account
            var addResult = await _userManager.AddLoginAsync(existing, info);
            if (addResult.Succeeded || addResult.Errors.All(e => e.Code == "LoginAlreadyAssociated"))
            {
                await _signInManager.SignInAsync(existing, isPersistent: false,
                    authenticationMethod: info.LoginProvider);
                _logger.LogInformation("Linked {Provider} to existing account {Email}.",
                    info.LoginProvider, Input.Email);
                return LocalRedirect(returnUrl);
            }

            foreach (var err in addResult.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
        }
        else
        {
            // Brand-new user — create account and link
            var displayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            var user = new ApplicationUser
            {
                UserName = Input.Email,
                Email = Input.Email,
                EmailConfirmed = true,     // provider already verified the email
                DisplayName = displayName,
            };

            var createResult = await _userManager.CreateAsync(user);
            if (createResult.Succeeded)
            {
                createResult = await _userManager.AddLoginAsync(user, info);
                if (createResult.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false,
                        authenticationMethod: info.LoginProvider);
                    _logger.LogInformation("Created account for {Email} via {Provider}.",
                        Input.Email, info.LoginProvider);
                    return LocalRedirect(returnUrl);
                }
            }

            foreach (var err in createResult.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
        }

        ReturnUrl = returnUrl;
        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        return Page();
    }

    private string CleanReturnUrl(string? returnUrl)
    {
        const string fallback = "/";
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

        var cleanPath = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? fallback : uri.AbsolutePath;
        var cleanQuery = QueryString.Create(keptQuery).ToUriComponent();
        return cleanPath + cleanQuery + uri.Fragment;
    }
}
