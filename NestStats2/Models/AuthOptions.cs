namespace NestStats2.Models;

public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    public bool RequireConfirmedAccount { get; set; } = false;

    public ExternalLoginOptions Google { get; set; } = new();

    public ExternalLoginOptions Facebook { get; set; } = new();
}

public sealed class ExternalLoginOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
