namespace NestStats2.Models;

public sealed class AdminBootstrapOptions
{
    public const string SectionName = "AdminBootstrap";

    public string Email { get; set; } = "admin@neststats.local";

    public string Password { get; set; } = "ChangeMe123!";

    public string DisplayName { get; set; } = "NestStats Admin";
}
