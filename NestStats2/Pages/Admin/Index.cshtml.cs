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
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEnergyDashboardService _dashboardService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        ApplicationDbContext dbContext,
        IEnergyDashboardService dashboardService,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _dashboardService = dashboardService;
        _userManager = userManager;
    }

    public AdminOverview Overview { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string Search { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Filter { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int ActivityDays { get; set; } = 14;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ActivityDays = ActivityDays is < 1 or > 365 ? 14 : ActivityDays;
        var users = await _userManager.Users
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);
        var assignments = await _dbContext.UserEnergySystems
            .ToListAsync(cancellationToken);
        assignments = assignments
            .OrderByDescending(x => x.ConnectedUtc)
            .ToList();
        var systems = await _dashboardService.GetSystemsAsync(cancellationToken);
        var inactivityCutoff = DateTimeOffset.UtcNow.AddDays(-ActivityDays);

        var userRows = new List<AdminUserRow>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var userAssignments = assignments.Where(x => x.UserId == user.Id).ToArray();
            var isInactive = !user.LastSeenUtc.HasValue || user.LastSeenUtc.Value < inactivityCutoff;
            userRows.Add(new AdminUserRow
            {
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? (user.Email ?? user.UserName ?? "User") : user.DisplayName,
                Email = user.Email ?? "-",
                Roles = roles.ToArray(),
                ConnectedSystems = userAssignments.Length,
                LastSeenUtc = user.LastSeenUtc,
                Systems = userAssignments,
                OnboardingCompleted = user.OnboardingCompletedUtc.HasValue,
                IsInactive = isInactive
            });
        }

        var filteredUsers = userRows
            .Where(MatchesSearch)
            .Where(MatchesFilter)
            .ToArray();

        Overview = new AdminOverview
        {
            TotalSystems = systems.Count,
            ConnectedSystems = assignments.Select(x => x.SnNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TotalUsers = users.Count,
            AdminUsers = userRows.Count(x => x.Roles.Contains(IdentitySeeder.AdminRole, StringComparer.OrdinalIgnoreCase)),
            Users = filteredUsers,
            NeedsOnboardingUsers = userRows.Count(x => !x.OnboardingCompleted),
            UsersWithoutSystems = userRows.Count(x => x.ConnectedSystems == 0),
            InactiveUsers = userRows.Count(x => x.IsInactive),
            FilteredUsersCount = filteredUsers.Length,
            RecentAssignments = assignments.Take(12).ToArray()
        };
    }

    private bool MatchesSearch(AdminUserRow row)
    {
        if (string.IsNullOrWhiteSpace(Search))
        {
            return true;
        }

        var search = Search.Trim();
        return row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
               || row.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
               || row.Systems.Any(x =>
                   x.SystemName.Contains(search, StringComparison.OrdinalIgnoreCase)
                   || x.SnNumber.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesFilter(AdminUserRow row)
    {
        return Filter.ToLowerInvariant() switch
        {
            "needs-onboarding" => !row.OnboardingCompleted,
            "no-systems" => row.ConnectedSystems == 0,
            "inactive" => row.IsInactive,
            "admins" => row.Roles.Contains(IdentitySeeder.AdminRole, StringComparer.OrdinalIgnoreCase),
            _ => true
        };
    }

    public sealed class AdminOverview
    {
        public int TotalSystems { get; init; }

        public int ConnectedSystems { get; init; }

        public int TotalUsers { get; init; }

        public int AdminUsers { get; init; }

        public int NeedsOnboardingUsers { get; init; }

        public int UsersWithoutSystems { get; init; }

        public int InactiveUsers { get; init; }

        public int FilteredUsersCount { get; init; }

        public IReadOnlyList<AdminUserRow> Users { get; init; } = [];

        public IReadOnlyList<UserEnergySystemAssignment> RecentAssignments { get; init; } = [];
    }

    public sealed class AdminUserRow
    {
        public string DisplayName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public IReadOnlyList<string> Roles { get; init; } = [];

        public int ConnectedSystems { get; init; }
        public DateTimeOffset? LastSeenUtc { get; init; }

        public IReadOnlyList<UserEnergySystemAssignment> Systems { get; init; } = [];

        public bool OnboardingCompleted { get; init; }

        public bool IsInactive { get; init; }
    }
}
