using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NestStats2.Data;
using NestStats2.Models;
using NestStats2.Services;

namespace NestStats2.Pages.Admin;

[Authorize(Roles = IdentitySeeder.AdminRole)]
public sealed class AssignmentsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEnergyDashboardService _dashboardService;
    private readonly ISystemCredentialProtector _credentialProtector;
    private readonly UserManager<ApplicationUser> _userManager;

    public AssignmentsModel(
        ApplicationDbContext dbContext,
        IEnergyDashboardService dashboardService,
        ISystemCredentialProtector credentialProtector,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _dashboardService = dashboardService;
        _credentialProtector = credentialProtector;
        _userManager = userManager;
    }

    [BindProperty]
    public AssignmentInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string Search { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Segment { get; set; } = "all";

    public IReadOnlyList<UserOption> Users { get; private set; } = [];

    public IReadOnlyList<AdminAssignmentRow> Assignments { get; private set; } = [];

    public AssignmentOverview Overview { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAssignAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.Users.FirstOrDefaultAsync(x => x.Id == Input.UserId, cancellationToken);
        if (user is null)
        {
            ModelState.AddModelError(nameof(Input.UserId), "Vyber platneho klienta.");
            return Page();
        }

        var systems = await _dashboardService.GetSystemsAsync(cancellationToken);
        var matchedSystem = systems.FirstOrDefault(x => string.Equals(x.sn_number, Input.SnNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        if (matchedSystem is null)
        {
            ModelState.AddModelError(nameof(Input.SnNumber), "SN sa v databaze nenaslo.");
            return Page();
        }

        var existing = await _dbContext.UserEnergySystems
            .FirstOrDefaultAsync(x => x.UserId == user.Id && x.SnNumber == matchedSystem.sn_number, cancellationToken);

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

        existing.SystemName = string.IsNullOrWhiteSpace(Input.Alias) ? matchedSystem.system_name : Input.Alias.Trim();
        existing.SystemAddress = matchedSystem.system_address;
        existing.EncryptedPassword = _credentialProtector.Protect(Input.SystemPassword.Trim());
        existing.LastVerifiedUtc = DateTimeOffset.UtcNow;

        var userAssignments = await _dbContext.UserEnergySystems
            .Where(x => x.UserId == user.Id)
            .ToListAsync(cancellationToken);

        if (Input.SetPrimary || userAssignments.Count == 0)
        {
            foreach (var assignment in userAssignments)
            {
                assignment.IsPrimary = assignment.Id == existing.Id;
            }

            existing.IsPrimary = true;
            user.PreferredSystemSn = existing.SnNumber;
        }

        if (!await _userManager.IsInRoleAsync(user, IdentitySeeder.ClientRole))
        {
            await _userManager.AddToRoleAsync(user, IdentitySeeder.ClientRole);
        }

        UpdateOnboardingState(user, userAssignments.Append(existing).DistinctBy(x => x.Id).ToArray());
        await _dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = $"System {existing.SnNumber} bol priradeny klientovi {user.Email}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, CancellationToken cancellationToken)
    {
        var assignment = await _dbContext.UserEnergySystems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (assignment is null)
        {
            return NotFound();
        }

        var user = await _userManager.Users.FirstOrDefaultAsync(x => x.Id == assignment.UserId, cancellationToken);
        _dbContext.UserEnergySystems.Remove(assignment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (user is not null)
        {
            var remaining = await _dbContext.UserEnergySystems
                .Where(x => x.UserId == user.Id)
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.SystemName)
                .ToListAsync(cancellationToken);

            if (remaining.Count > 0 && remaining.All(x => !x.IsPrimary))
            {
                remaining[0].IsPrimary = true;
            }

            user.PreferredSystemSn = remaining.FirstOrDefault(x => x.IsPrimary)?.SnNumber ?? string.Empty;
            UpdateOnboardingState(user, remaining);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        TempData["StatusMessage"] = "Priradenie systemu bolo odstranene.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var users = await _userManager.Users
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);

        Users = users
            .Select(x => new UserOption
            {
                Id = x.Id,
                DisplayName = GetUserLabel(x),
                Email = x.Email ?? "-"
            })
            .ToArray();

        var assignments = await _dbContext.UserEnergySystems
            .ToListAsync(cancellationToken);
        assignments = assignments
            .OrderByDescending(x => x.ConnectedUtc)
            .ToList();

        var systems = await _dashboardService.GetSystemsAsync(cancellationToken);
        var catalogSn = systems
            .Select(x => x.sn_number)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userIndex = users.ToDictionary(
            x => x.Id,
            x => new UserLookup(GetUserLabel(x), x.Email ?? "-", x.PreferredSystemSn ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);
        var assignedUserIds = assignments.Select(x => x.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignedSn = assignments
            .Select(x => x.SnNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableCatalogSystems = catalogSn.Count(sn => !assignedSn.Contains(sn));
        var usersMissingPrimary = users.Count(user =>
        {
            var userAssignments = assignments.Where(x => x.UserId == user.Id).ToArray();
            return userAssignments.Length > 0 && userAssignments.All(x => !x.IsPrimary);
        });
        var multiSystemUsers = assignments
            .GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Select(x => x.SnNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        var recentCutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var recentlyConnected = assignments.Count(x => x.ConnectedUtc >= recentCutoff);
        var staleVerificationCutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var staleCredentials = assignments.Count(x => !x.LastVerifiedUtc.HasValue || x.LastVerifiedUtc.Value < staleVerificationCutoff);

        var rows = assignments
            .Select(x => new AdminAssignmentRow
            {
                Id = x.Id,
                UserId = x.UserId,
                UserLabel = userIndex.TryGetValue(x.UserId, out var user) ? user.Label : x.UserId,
                UserEmail = userIndex.TryGetValue(x.UserId, out user) ? user.Email : string.Empty,
                PreferredSystemSn = userIndex.TryGetValue(x.UserId, out user) ? user.PreferredSystemSn : string.Empty,
                SnNumber = x.SnNumber,
                SystemName = x.SystemName,
                SystemAddress = x.SystemAddress ?? string.Empty,
                ConnectedUtc = x.ConnectedUtc,
                LastVerifiedUtc = x.LastVerifiedUtc,
                IsPrimary = x.IsPrimary,
                IsInCatalog = catalogSn.Contains(x.SnNumber)
            })
            .ToArray();

        Assignments = rows
            .Where(MatchesSearch)
            .Where(MatchesSegment)
            .ToArray();

        Overview = new AssignmentOverview
        {
            TotalUsers = users.Count,
            AssignableUsers = Users.Count,
            UsersWithAssignments = assignedUserIds.Count,
            UsersWithoutAssignments = Math.Max(0, users.Count - assignedUserIds.Count),
            TotalAssignments = assignments.Count,
            PrimaryAssignments = assignments.Count(x => x.IsPrimary),
            UsersMissingPrimary = usersMissingPrimary,
            MultiSystemUsers = multiSystemUsers,
            CatalogSystems = catalogSn.Count,
            UniqueAssignedSystems = assignedSn.Count,
            AvailableCatalogSystems = availableCatalogSystems,
            RecentlyConnected = recentlyConnected,
            StaleCredentials = staleCredentials,
            FilteredAssignments = Assignments.Count,
            FilteredSegment = string.IsNullOrWhiteSpace(Segment) ? "all" : Segment,
            RecentAssignments = rows.Take(6).ToArray()
        };
    }

    private bool MatchesSearch(AdminAssignmentRow row)
    {
        if (string.IsNullOrWhiteSpace(Search))
        {
            return true;
        }

        var search = Search.Trim();
        return row.SystemName.Contains(search, StringComparison.OrdinalIgnoreCase)
               || row.SnNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
               || row.UserLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
               || row.UserEmail.Contains(search, StringComparison.OrdinalIgnoreCase)
               || row.SystemAddress.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSegment(AdminAssignmentRow row)
    {
        return (Segment ?? "all").Trim().ToLowerInvariant() switch
        {
            "primary" => row.IsPrimary,
            "secondary" => !row.IsPrimary,
            "missing-catalog" => !row.IsInCatalog,
            "stale" => !row.LastVerifiedUtc.HasValue || row.LastVerifiedUtc.Value < DateTimeOffset.UtcNow.AddDays(-30),
            _ => true
        };
    }

    private static string GetUserLabel(ApplicationUser user)
    {
        return string.IsNullOrWhiteSpace(user.DisplayName)
            ? (user.Email ?? user.UserName ?? "Pouzivatel")
            : user.DisplayName;
    }

    private static void UpdateOnboardingState(ApplicationUser user, IReadOnlyCollection<UserEnergySystemAssignment> assignments)
    {
        var isComplete = !string.IsNullOrWhiteSpace(user.PreferredSystemSn)
                         && assignments.Any(x => string.Equals(x.SnNumber, user.PreferredSystemSn, StringComparison.OrdinalIgnoreCase));

        user.OnboardingCompletedUtc = isComplete ? user.OnboardingCompletedUtc ?? DateTimeOffset.UtcNow : null;
    }

    public sealed class AssignmentInput
    {
        [Required]
        [Display(Name = "Klient")]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [Display(Name = "SN systemu")]
        public string SnNumber { get; set; } = string.Empty;

        [Required]
        [MinLength(4)]
        [Display(Name = "Heslo systemu")]
        public string SystemPassword { get; set; } = string.Empty;

        [Display(Name = "Alias")]
        public string Alias { get; set; } = string.Empty;

        [Display(Name = "Nastavit ako primarny")]
        public bool SetPrimary { get; set; } = true;
    }

    public sealed class UserOption
    {
        public string Id { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;
    }

    public sealed class AdminAssignmentRow
    {
        public int Id { get; init; }

        public string UserId { get; init; } = string.Empty;

        public string UserLabel { get; init; } = string.Empty;

        public string UserEmail { get; init; } = string.Empty;

        public string PreferredSystemSn { get; init; } = string.Empty;

        public string SnNumber { get; init; } = string.Empty;

        public string SystemName { get; init; } = string.Empty;

        public string SystemAddress { get; init; } = string.Empty;

        public DateTimeOffset ConnectedUtc { get; init; }

        public DateTimeOffset? LastVerifiedUtc { get; init; }

        public bool IsPrimary { get; init; }

        public bool IsInCatalog { get; init; }
    }

    public sealed class AssignmentOverview
    {
        public int TotalUsers { get; init; }

        public int AssignableUsers { get; init; }

        public int UsersWithAssignments { get; init; }

        public int UsersWithoutAssignments { get; init; }

        public int TotalAssignments { get; init; }

        public int PrimaryAssignments { get; init; }

        public int UsersMissingPrimary { get; init; }

        public int MultiSystemUsers { get; init; }

        public int CatalogSystems { get; init; }

        public int UniqueAssignedSystems { get; init; }

        public int AvailableCatalogSystems { get; init; }

        public int RecentlyConnected { get; init; }

        public int StaleCredentials { get; init; }

        public int FilteredAssignments { get; init; }

        public string FilteredSegment { get; init; } = "all";

        public IReadOnlyList<AdminAssignmentRow> RecentAssignments { get; init; } = [];
    }

    private sealed record UserLookup(string Label, string Email, string PreferredSystemSn);
}
