using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NestStats2.Models;

namespace NestStats2.Data;

public sealed class IdentitySeeder
{
    public const string AdminRole = "Admin";
    public const string ClientRole = "Client";

    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IdentitySchemaUpdater _schemaUpdater;
    private readonly AdminBootstrapOptions _adminOptions;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IdentitySchemaUpdater schemaUpdater,
        IOptions<AdminBootstrapOptions> adminOptions,
        ILogger<IdentitySeeder> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _schemaUpdater = schemaUpdater;
        _adminOptions = adminOptions.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await _schemaUpdater.EnsureSchemaAsync(cancellationToken);

        foreach (var roleName in new[] { AdminRole, ClientRole })
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await _roleManager.CreateAsync(new IdentityRole(roleName));
                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to create role '{roleName}': {string.Join(", ", roleResult.Errors.Select(x => x.Description))}");
                }
            }
        }

        var adminEmail = _adminOptions.Email.Trim();
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            _logger.LogInformation("Admin bootstrap email is empty. Skipping admin seed.");
            return;
        }

        var adminUser = await _userManager.Users.FirstOrDefaultAsync(x => x.Email == adminEmail, cancellationToken);
        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(_adminOptions.DisplayName) ? "NestStats Admin" : _adminOptions.DisplayName.Trim()
            };

            var createResult = await _userManager.CreateAsync(adminUser, _adminOptions.Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create admin user '{adminEmail}': {string.Join(", ", createResult.Errors.Select(x => x.Description))}");
            }
        }

        if (!await _userManager.IsInRoleAsync(adminUser, AdminRole))
        {
            var roleResult = await _userManager.AddToRoleAsync(adminUser, AdminRole);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to add admin role to '{adminEmail}': {string.Join(", ", roleResult.Errors.Select(x => x.Description))}");
            }
        }

        if (!await _userManager.IsInRoleAsync(adminUser, ClientRole))
        {
            await _userManager.AddToRoleAsync(adminUser, ClientRole);
        }
    }
}
