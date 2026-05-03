using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NestStats2.Data;
using NestStats2.Models;
using NestStats2.Services;

namespace NestStats2.Pages.Onboarding;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AppLanguageService _language;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        ApplicationDbContext dbContext,
        AppLanguageService language,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _language = language;
        _userManager = userManager;
    }

    [BindProperty]
    public OnboardingInput Input { get; set; } = new();

    public string UserEmail { get; private set; } = string.Empty;

    public int ConnectedSystemsCount { get; private set; }

    public string PrimarySystemName { get; private set; } = string.Empty;

    public bool HasPrimarySystem { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole))
        {
            return RedirectToPage("/Admin/Index");
        }

        await LoadUserContextAsync(user, cancellationToken);

        if (string.IsNullOrWhiteSpace(Input.DisplayName))
        {
            Input.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? SuggestedDisplayName(user)
                : user.DisplayName;
        }

        Input.ConnectNow = !HasPrimarySystem;

        if (user.OnboardingCompletedUtc.HasValue)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole))
        {
            return RedirectToPage("/Admin/Index");
        }

        await LoadUserContextAsync(user, cancellationToken);

        var displayName = Input.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length < 2)
        {
            ModelState.AddModelError(
                nameof(Input.DisplayName),
                Text("Zadaj aspoň krátky názov pracovného priestoru.", "Enter at least a short workspace name."));
        }

        if (displayName.Length > 160)
        {
            ModelState.AddModelError(
                nameof(Input.DisplayName),
                Text("Názov pracovného priestoru môže mať najviac 160 znakov.", "The workspace name can be at most 160 characters."));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.DisplayName = displayName;

        if (!await _userManager.IsInRoleAsync(user, IdentitySeeder.ClientRole))
        {
            await _userManager.AddToRoleAsync(user, IdentitySeeder.ClientRole);
        }

        if (HasPrimarySystem)
        {
            user.OnboardingCompletedUtc ??= DateTimeOffset.UtcNow;
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        if (Input.ConnectNow)
        {
            return RedirectToPage("/Systems/Connect");
        }

        return RedirectToPage("/GettingStarted/Index");
    }

    private async Task LoadUserContextAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        UserEmail = user.Email ?? user.UserName ?? string.Empty;
        var systems = await _dbContext.UserEnergySystems
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.SystemName)
            .ToListAsync(cancellationToken);

        ConnectedSystemsCount = systems.Count;
        var primary = systems.FirstOrDefault(x => x.IsPrimary)
            ?? systems.FirstOrDefault(x => string.Equals(x.SnNumber, user.PreferredSystemSn, StringComparison.OrdinalIgnoreCase));

        HasPrimarySystem = primary is not null;
        PrimarySystemName = primary?.SystemName ?? string.Empty;
    }

    private static string SuggestedDisplayName(ApplicationUser user)
    {
        var source = user.Email ?? user.UserName ?? string.Empty;
        var atIndex = source.IndexOf('@');
        return atIndex > 0
            ? source[..atIndex]
            : source;
    }

    private string Text(string slovak, string english)
        => _language.Pick(HttpContext, slovak, english);

    public sealed class OnboardingInput
    {
        [Display(Name = "Názov pracovného priestoru")]
        public string DisplayName { get; set; } = string.Empty;

        [Display(Name = "Pripojiť systém teraz")]
        public bool ConnectNow { get; set; }
    }
}

