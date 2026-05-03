namespace NestStats2.Models;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string FromName { get; set; } = "NestStats";

    public string FromAddress { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    public string SmtpUser { get; set; } = string.Empty;

    public string SmtpPassword { get; set; } = string.Empty;

    public bool UseSsl { get; set; } = true;

    public bool SavePreviewToDiskWhenDisabled { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(FromAddress) &&
        !string.IsNullOrWhiteSpace(SmtpHost) &&
        !string.IsNullOrWhiteSpace(SmtpUser) &&
        !string.IsNullOrWhiteSpace(SmtpPassword);
}
