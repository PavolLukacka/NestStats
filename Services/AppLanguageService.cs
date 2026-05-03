using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace NestStats2.Services;

public sealed class AppLanguageService
{
    public const string Slovak = "sk";
    public const string English = "en";

    public static IReadOnlyList<string> SupportedCultures { get; } = [Slovak, English];

    public string Normalize(string? culture)
    {
        var candidate = culture?.Trim().ToLowerInvariant();
        return candidate is English or "en-us" or "en-gb"
            ? English
            : Slovak;
    }

    public string GetCurrentCulture(HttpContext? httpContext)
    {
        var requestCulture = httpContext?.Features.Get<IRequestCultureFeature>()?.RequestCulture;
        var uiCulture = requestCulture?.UICulture?.TwoLetterISOLanguageName;
        return Normalize(uiCulture);
    }

    public bool IsEnglish(HttpContext? httpContext)
        => string.Equals(GetCurrentCulture(httpContext), English, StringComparison.OrdinalIgnoreCase);

    public string Pick(HttpContext? httpContext, string slovak, string english)
        => IsEnglish(httpContext) ? english : slovak;

    public string Locale(HttpContext? httpContext)
        => IsEnglish(httpContext) ? "en-US" : "sk-SK";

    public string HtmlLang(HttpContext? httpContext)
        => IsEnglish(httpContext) ? "en" : "sk";
}
