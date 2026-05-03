using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NestStats2.Data;
using NestStats2.Models;
using NestStats2.Services;

namespace NestStats2.Pages.Settings;

[Authorize]
public sealed class IndexModel : PageModel
{
    public const string KeepSignedInCookieName = "neststats.keepSignedInAfterClose";
    public const string CompactInterfaceCookieName = "neststats.ui.compact";
    public const string ReduceMotionCookieName = "neststats.ui.reduceMotion";

    private static readonly TimeSpan PreferenceCookieLifetime = TimeSpan.FromDays(365);

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
    public SettingsInput Input { get; set; } = new();

    public IReadOnlyList<UserEnergySystemAssignment> Systems { get; private set; } = [];

    public bool OnboardingCompleted { get; private set; }

    public string PreferredSystemSummary { get; private set; } = string.Empty;

    public string UserEmail { get; private set; } = string.Empty;

    public string AccountCreatedSummary { get; private set; } = string.Empty;

    public string LastSeenSummary { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadReferenceDataAsync(user, cancellationToken);

        var displayName = (Input.DisplayName ?? string.Empty).Trim();
        if (displayName.Length > 160)
        {
            ModelState.AddModelError(
                nameof(Input.DisplayName),
                Text("Názov pracovného priestoru môže mať najviac 160 znakov.", "The workspace name can be at most 160 characters."));
        }

        if (Systems.Count > 0 &&
            !string.IsNullOrWhiteSpace(Input.PreferredSystemSn) &&
            Systems.All(x => !string.Equals(x.SnNumber, Input.PreferredSystemSn, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(
                nameof(Input.PreferredSystemSn),
                Text("Vyber systém, ktorý máš priradený.", "Select a system assigned to your account."));
        }

        if (!ModelState.IsValid)
        {
            PopulateSummary(user);
            return Page();
        }

        user.DisplayName = displayName;
        user.PreferredSystemSn = Input.PreferredSystemSn ?? string.Empty;

        foreach (var system in Systems)
        {
            system.IsPrimary = string.Equals(system.SnNumber, user.PreferredSystemSn, StringComparison.OrdinalIgnoreCase);
        }

        if (Systems.Count > 0 && string.IsNullOrWhiteSpace(user.PreferredSystemSn))
        {
            Systems[0].IsPrimary = true;
            user.PreferredSystemSn = Systems[0].SnNumber;
        }

        UpdateOnboardingState(user, Systems);
        WritePreferenceCookies(user.Id);

        await _dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = Text("Nastavenia boli uložené.", "Settings were saved.");
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return;
        }

        await LoadReferenceDataAsync(user, cancellationToken);

        Input = new SettingsInput
        {
            DisplayName = user.DisplayName,
            PreferredSystemSn = string.IsNullOrWhiteSpace(user.PreferredSystemSn)
                ? Systems.FirstOrDefault(x => x.IsPrimary)?.SnNumber ?? Systems.FirstOrDefault()?.SnNumber ?? string.Empty
                : user.PreferredSystemSn,
            KeepSignedInAfterClose = ReadBoolCookie(KeepSignedInCookieName, false),
            CompactInterface = ReadBoolCookie(CompactInterfaceCookieName, false),
            ReduceMotion = ReadBoolCookie(ReduceMotionCookieName, false)
        };

        PopulateSummary(user);
    }

    private async Task LoadReferenceDataAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        UserEmail = user.Email ?? user.UserName ?? string.Empty;
        AccountCreatedSummary = user.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy");
        LastSeenSummary = user.LastSeenUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? Text("Zatiaľ bez záznamu", "No record yet");

        Systems = await _dbContext.UserEnergySystems
            .Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.SystemName)
            .ToListAsync(cancellationToken);
    }

    private void WritePreferenceCookies(string userId)
    {
        WriteBoolCookie(KeepSignedInCookieName, Input.KeepSignedInAfterClose, httpOnly: true);
        WriteBoolCookie(CompactInterfaceCookieName, Input.CompactInterface);
        WriteBoolCookie(ReduceMotionCookieName, Input.ReduceMotion);

        if (Input.ClearLanguageCookie)
        {
            Response.Cookies.Delete(CookieRequestCultureProvider.DefaultCookieName);
        }

        if (Input.ResetDashboardPreferences)
        {
            Response.Cookies.Delete(BuildDashboardPreferenceCookieName(userId));
        }
    }

    private static void UpdateOnboardingState(ApplicationUser user, IReadOnlyCollection<UserEnergySystemAssignment> systems)
    {
        var isComplete = !string.IsNullOrWhiteSpace(user.PreferredSystemSn)
                         && systems.Any(x => string.Equals(x.SnNumber, user.PreferredSystemSn, StringComparison.OrdinalIgnoreCase));

        user.OnboardingCompletedUtc = isComplete ? user.OnboardingCompletedUtc ?? DateTimeOffset.UtcNow : null;
    }

    private void PopulateSummary(ApplicationUser user)
    {
        OnboardingCompleted = user.OnboardingCompletedUtc.HasValue;

        var preferredSystemSn = string.IsNullOrWhiteSpace(Input.PreferredSystemSn)
            ? user.PreferredSystemSn
            : Input.PreferredSystemSn;
        var selectedSystem = Systems.FirstOrDefault(x => string.Equals(x.SnNumber, preferredSystemSn, StringComparison.OrdinalIgnoreCase));
        PreferredSystemSummary = selectedSystem is null
            ? Text("Zatiaľ nie je vybraný", "Not selected yet")
            : $"{selectedSystem.SystemName} ({selectedSystem.SnNumber})";
    }

    private bool ReadBoolCookie(string name, bool defaultValue)
    {
        return Request.Cookies.TryGetValue(name, out var rawValue) && bool.TryParse(rawValue, out var parsed)
            ? parsed
            : defaultValue;
    }

    private void WriteBoolCookie(string name, bool value, bool httpOnly = false)
    {
        Response.Cookies.Append(
            name,
            value ? "true" : "false",
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.Add(PreferenceCookieLifetime),
                HttpOnly = httpOnly,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
    }

    private static string BuildDashboardPreferenceCookieName(string? userId)
    {
        var safeUserId = new string((userId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safeUserId))
        {
            safeUserId = "user";
        }

        return $"neststats.dashboard.{safeUserId.ToLowerInvariant()}";
    }

    private string Text(string slovak, string english)
        => _language.Pick(HttpContext, slovak, english);

    public sealed class SettingsInput
    {
        [StringLength(160)]
        [Display(Name = "Názov klienta alebo objektu")]
        public string DisplayName { get; set; } = string.Empty;

        [Display(Name = "Predvolený systém")]
        public string PreferredSystemSn { get; set; } = string.Empty;

        public bool KeepSignedInAfterClose { get; set; }

        public bool CompactInterface { get; set; }

        public bool ReduceMotion { get; set; }

        public bool ClearLanguageCookie { get; set; }

        public bool ResetDashboardPreferences { get; set; }
    }
}
