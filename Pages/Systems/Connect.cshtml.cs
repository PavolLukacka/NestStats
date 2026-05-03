using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NestStats2.Data;
using NestStats2.Models;
using NestStats2.Services;

namespace NestStats2.Pages.Systems;

[Authorize]
public sealed class ConnectModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEnergyDashboardService _dashboardService;
    private readonly AppLanguageService _language;
    private readonly ISystemCredentialProtector _credentialProtector;
    private readonly UserManager<ApplicationUser> _userManager;

    public ConnectModel(
        ApplicationDbContext dbContext,
        IEnergyDashboardService dashboardService,
        AppLanguageService language,
        ISystemCredentialProtector credentialProtector,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _dashboardService = dashboardService;
        _language = language;
        _credentialProtector = credentialProtector;
        _userManager = userManager;
    }

    [BindProperty]
    public ConnectInput Input { get; set; } = new();

    public IReadOnlyList<UserEnergySystemAssignment> ConnectedSystems { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAssignmentsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostConnectAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            await LoadAssignmentsAsync(cancellationToken);
            return Page();
        }

        var systems = await _dashboardService.GetSystemsAsync(cancellationToken);
        var matchedSystem = systems.FirstOrDefault(x => string.Equals(x.sn_number, Input.SnNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        if (matchedSystem is null)
        {
            ModelState.AddModelError(
                nameof(Input.SnNumber),
                Text("Zadané sériové číslo sa v databáze nenašlo.", "The entered serial number was not found in the database."));
            await LoadAssignmentsAsync(cancellationToken);
            return Page();
        }

        var existing = await _dbContext.UserEnergySystems
            .FirstOrDefaultAsync(
                x => x.UserId == user.Id && x.SnNumber == matchedSystem.sn_number,
                cancellationToken);

        if (existing is null)
        {
            existing = new UserEnergySystemAssignment
            {
                UserId = user.Id,
                SnNumber = matchedSystem.sn_number,
                ConnectedUtc = DateTimeOffset.UtcNow
            };
            _dbContext.UserEnergySystems.Add(existing);
        }

        existing.SystemName = string.IsNullOrWhiteSpace(Input.Alias)
            ? matchedSystem.system_name
            : Input.Alias.Trim();
        existing.SystemAddress = matchedSystem.system_address;
        existing.EncryptedPassword = _credentialProtector.Protect(Input.SystemPassword.Trim());
        existing.LastVerifiedUtc = DateTimeOffset.UtcNow;

        var hasPrimary = await _dbContext.UserEnergySystems
            .AnyAsync(x => x.UserId == user.Id && x.IsPrimary && x.Id != existing.Id, cancellationToken);
        if (!hasPrimary)
        {
            existing.IsPrimary = true;
        }

        if (string.IsNullOrWhiteSpace(user.PreferredSystemSn))
        {
            user.PreferredSystemSn = matchedSystem.sn_number;
        }

        if (!await _userManager.IsInRoleAsync(user, IdentitySeeder.ClientRole))
        {
            await _userManager.AddToRoleAsync(user, IdentitySeeder.ClientRole);
        }

        UpdateOnboardingState(user, await LoadAssignmentsAsync(user.Id, cancellationToken));
        await _dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = Text(
            $"Systém {matchedSystem.sn_number} bol úspešne pripojený.",
            $"System {matchedSystem.sn_number} was connected successfully.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetPrimaryAsync(int id, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var assignments = await _dbContext.UserEnergySystems
            .Where(x => x.UserId == user.Id)
            .ToListAsync(cancellationToken);
        var selected = assignments.FirstOrDefault(x => x.Id == id);
        if (selected is null)
        {
            return NotFound();
        }

        foreach (var assignment in assignments)
        {
            assignment.IsPrimary = assignment.Id == selected.Id;
        }

        user.PreferredSystemSn = selected.SnNumber;
        UpdateOnboardingState(user, assignments);
        await _dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = Text(
            $"Systém {selected.SnNumber} je teraz hlavný.",
            $"System {selected.SnNumber} is now primary.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var assignment = await _dbContext.UserEnergySystems
            .FirstOrDefaultAsync(x => x.UserId == user.Id && x.Id == id, cancellationToken);
        if (assignment is null)
        {
            return NotFound();
        }

        var wasPrimary = assignment.IsPrimary;
        _dbContext.UserEnergySystems.Remove(assignment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (wasPrimary)
        {
            var nextPrimary = await _dbContext.UserEnergySystems
                .Where(x => x.UserId == user.Id)
                .OrderBy(x => x.SystemName)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextPrimary is not null)
            {
                nextPrimary.IsPrimary = true;
                user.PreferredSystemSn = nextPrimary.SnNumber;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                user.PreferredSystemSn = string.Empty;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var remainingAssignments = await _dbContext.UserEnergySystems
            .Where(x => x.UserId == user.Id)
            .ToListAsync(cancellationToken);
        UpdateOnboardingState(user, remainingAssignments);
        await _dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = Text(
            $"Systém {assignment.SnNumber} bol odpojený.",
            $"System {assignment.SnNumber} was disconnected.");
        return RedirectToPage();
    }

    private string Text(string slovak, string english)
        => _language.Pick(HttpContext, slovak, english);

    private async Task LoadAssignmentsAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        ConnectedSystems = user is null
            ? []
            : await LoadAssignmentsAsync(user.Id, cancellationToken);
    }

    private async Task<IReadOnlyList<UserEnergySystemAssignment>> LoadAssignmentsAsync(string userId, CancellationToken cancellationToken)
    {
        return await _dbContext.UserEnergySystems
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.SystemName)
            .ToListAsync(cancellationToken);
    }

    private static void UpdateOnboardingState(ApplicationUser user, IReadOnlyCollection<UserEnergySystemAssignment> assignments)
    {
        var isComplete = !string.IsNullOrWhiteSpace(user.PreferredSystemSn)
                         && assignments.Any(x => string.Equals(x.SnNumber, user.PreferredSystemSn, StringComparison.OrdinalIgnoreCase));

        user.OnboardingCompletedUtc = isComplete ? user.OnboardingCompletedUtc ?? DateTimeOffset.UtcNow : null;
    }

    public sealed class ConnectInput
    {
        [Required]
        [Display(Name = "Seriove cislo")]
        public string SnNumber { get; set; } = string.Empty;

        [Required]
        [MinLength(4)]
        [Display(Name = "Heslo systemu")]
        public string SystemPassword { get; set; } = string.Empty;

        [Display(Name = "Nazov systemu")]
        public string Alias { get; set; } = string.Empty;
    }
}
