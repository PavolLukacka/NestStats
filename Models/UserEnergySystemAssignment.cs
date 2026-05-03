namespace NestStats2.Models;

public sealed class UserEnergySystemAssignment
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public string SnNumber { get; set; } = string.Empty;

    public string SystemName { get; set; } = string.Empty;

    public string? SystemAddress { get; set; }

    public string EncryptedPassword { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public DateTimeOffset ConnectedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastVerifiedUtc { get; set; }
}
