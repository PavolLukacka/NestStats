using Microsoft.AspNetCore.Identity;

namespace NestStats2.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public string PreferredProviderKey { get; set; } = string.Empty;

    public string PreferredTariffKey { get; set; } = string.Empty;

    public string PreferredSystemSn { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSeenUtc { get; set; }

    public DateTimeOffset? OnboardingCompletedUtc { get; set; }

    public ICollection<UserEnergySystemAssignment> EnergySystems { get; set; } = [];
}
