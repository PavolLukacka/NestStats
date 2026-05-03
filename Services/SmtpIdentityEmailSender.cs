using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using NestStats2.Models;

namespace NestStats2.Services;

public sealed class SmtpIdentityEmailSender :
    IEmailSender,
    Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>
{
    private readonly EmailOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SmtpIdentityEmailSender> _logger;

    public SmtpIdentityEmailSender(
        IOptions<EmailOptions> options,
        IWebHostEnvironment environment,
        ILogger<SmtpIdentityEmailSender> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var html = $"""
            <p>Ahoj {WebUtility.HtmlEncode(user.DisplayName is { Length: > 0 } ? user.DisplayName : user.Email ?? "pouzivatel")}!</p>
            <p>Pre aktivaciu uctu v NestStats potvrd svoju emailovu adresu:</p>
            <p><a href="{WebUtility.HtmlEncode(confirmationLink)}">Potvrdit ucet</a></p>
            <p>Ak si si ucet nevytvoril ty, tento email ignoruj.</p>
            """;

        return SendEmailAsync(email, "Potvrdenie uctu v NestStats", html);
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var html = $"""
            <p>Ahoj {WebUtility.HtmlEncode(user.DisplayName is { Length: > 0 } ? user.DisplayName : user.Email ?? "pouzivatel")}!</p>
            <p>Na obnovenie hesla do NestStats pouzi tento odkaz:</p>
            <p><a href="{WebUtility.HtmlEncode(resetLink)}">Obnovit heslo</a></p>
            <p>Ak si reset hesla nevyziadal ty, tento email ignoruj.</p>
            """;

        return SendEmailAsync(email, "Reset hesla v NestStats", html);
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var html = $"""
            <p>Ahoj {WebUtility.HtmlEncode(user.DisplayName is { Length: > 0 } ? user.DisplayName : user.Email ?? "pouzivatel")}!</p>
            <p>Tvoj reset kod pre NestStats je:</p>
            <p><strong>{WebUtility.HtmlEncode(resetCode)}</strong></p>
            """;

        return SendEmailAsync(email, "Reset kod pre NestStats", html);
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (!_options.IsConfigured)
        {
            await SavePreviewAsync(email, subject, htmlMessage);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlMessage,
            IsBodyHtml = true
        };
        message.To.Add(email);

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.UseSsl,
            Credentials = new NetworkCredential(_options.SmtpUser, _options.SmtpPassword)
        };

        await client.SendMailAsync(message);
    }

    private async Task SavePreviewAsync(string email, string subject, string htmlMessage)
    {
        _logger.LogWarning(
            "Email settings are not configured. Writing Identity email preview for {Email} to disk instead of sending.",
            email);

        if (!_options.SavePreviewToDiskWhenDisabled)
        {
            return;
        }

        var previewDirectory = Path.Combine(_environment.ContentRootPath, "App_Data", "email-preview");
        Directory.CreateDirectory(previewDirectory);

        var safeFileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.html";
        var previewPath = Path.Combine(previewDirectory, safeFileName);
        var html = $"""
            <html>
            <body style="font-family: Arial, sans-serif;">
                <h2>{WebUtility.HtmlEncode(subject)}</h2>
                <p><strong>To:</strong> {WebUtility.HtmlEncode(email)}</p>
                <hr />
                {htmlMessage}
            </body>
            </html>
            """;

        await File.WriteAllTextAsync(previewPath, html);
    }
}
