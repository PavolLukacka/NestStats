using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using NestStats2.Data;
using NestStats2.Models;
using NestStats2.Services;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

var sqlitePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "neststats-auth.db");
Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath)!);
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
    options.Conventions.AllowAnonymousToPage("/Privacy");
    options.Conventions.AllowAnonymousToPage("/SpoznajNas");
    options.Conventions.AllowAnonymousToPage("/GettingStarted/Index");
    options.Conventions.AllowAnonymousToPage("/Nastroje/Index");
    options.Conventions.AllowAnonymousToPage("/NavrhFotovoltaiky/Index");
    options.Conventions.AllowAnonymousToPage("/NavrhTC/Index");
    options.Conventions.AllowAnonymousToPage("/IntervalovaAnalyza/Index");
    options.Conventions.AddPageRoute("/SpoznajNas", "/about-us");
    options.Conventions.AddPageRoute("/GettingStarted/Index", "/getting-started");
    options.Conventions.AddPageRoute("/Nastroje/Index", "/tools");
    options.Conventions.AddPageRoute("/NavrhFotovoltaiky/Index", "/pv-design");
    options.Conventions.AddPageRoute("/NavrhTC/Index", "/heat-pump-design");
    options.Conventions.AddPageRoute("/IntervalovaAnalyza/Index", "/15-minute-interval-analysis");
});
builder.Services.AddLocalization();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "image/svg+xml",
        "application/json"
    ]);
});
builder.Services.AddSingleton<AppLanguageService>();
builder.Services.AddSingleton<StartupInitializationState>();
builder.Services.AddHostedService<StartupInitializationHostedService>();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? $"Data Source={sqlitePath}"));
builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = authOptions.RequireConfirmedAccount;
        options.User.RequireUniqueEmail = true;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services
    .AddOptions<SupabaseOptions>()
    .Bind(builder.Configuration.GetSection(SupabaseOptions.SectionName));
builder.Services
    .AddOptions<DashboardCatalogOptions>()
    .Bind(builder.Configuration.GetSection(DashboardCatalogOptions.SectionName));
builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services
    .AddOptions<AdminBootstrapOptions>()
    .Bind(builder.Configuration.GetSection(AdminBootstrapOptions.SectionName));
builder.Services
    .AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddHttpClient<IEnergyDashboardService, SupabaseEnergyDashboardService>();
builder.Services.AddHttpClient<IWeatherForecastService, OpenMeteoWeatherForecastService>();
builder.Services.AddHttpClient<ISpotMarketPriceService, OkteSpotMarketPriceService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".NestStats.Tools.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(2);
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddSingleton<IDashboardLoadCoordinator, DashboardLoadCoordinator>();
builder.Services.AddScoped<IdentitySchemaUpdater>();
builder.Services.AddScoped<IdentitySeeder>();
builder.Services.AddScoped<ISystemCredentialProtector, SystemCredentialProtector>();
builder.Services.AddScoped<INestStatsPvPdfExportService, NestStatsPvPdfExportService>();
builder.Services.AddScoped<IAnalysisPdfExportService2, AnalysisPdfExportService2>();
builder.Services.AddScoped<IUserPreferencesService, CookieUserPreferencesService>();
builder.Services.AddTransient<SmtpIdentityEmailSender>();
builder.Services.AddTransient<IEmailSender, SmtpIdentityEmailSender>();
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>, SmtpIdentityEmailSender>();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = AppLanguageService.SupportedCultures
        .Select(code => new CultureInfo(code))
        .ToList();

    options.DefaultRequestCulture = new RequestCulture(AppLanguageService.Slovak);
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.RequestCultureProviders =
    [
        new CustomRequestCultureProvider(context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/about-us", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/getting-started", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(AppLanguageService.English));
            }

            if (path.StartsWithSegments("/tools", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(AppLanguageService.English));
            }

            if (path.StartsWithSegments("/spoznaj-nas", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(AppLanguageService.Slovak));
            }

            if (path.StartsWithSegments("/ako-zacat", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/nastroje", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/navrh-fotovoltaiky", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/pv-design", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/navrh-tc", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/heat-pump-design", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/analyza-15-minutovych-intervalov", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/15-minute-interval-analysis", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(AppLanguageService.Slovak));
            }

            return Task.FromResult<ProviderCultureResult?>(null);
        }),
        new CookieRequestCultureProvider()
    ];
});
var authenticationBuilder = builder.Services.AddAuthentication();
if (!string.IsNullOrWhiteSpace(authOptions.Google.ClientId) &&
    !string.IsNullOrWhiteSpace(authOptions.Google.ClientSecret))
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = authOptions.Google.ClientId;
        options.ClientSecret = authOptions.Google.ClientSecret;
    });
}

if (!string.IsNullOrWhiteSpace(authOptions.Facebook.ClientId) &&
    !string.IsNullOrWhiteSpace(authOptions.Facebook.ClientSecret))
{
    authenticationBuilder.AddFacebook(options =>
    {
        options.AppId = authOptions.Facebook.ClientId;
        options.AppSecret = authOptions.Facebook.ClientSecret;
        // Meta can reject the default ASP.NET Core "email" scope for some app setups.
        // We only ask for public profile data and let the app collect email if needed.
        options.Scope.Clear();
        options.Scope.Add("public_profile");
    });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var cacheControl = app.Environment.IsDevelopment()
            ? "no-cache"
            : "public,max-age=31536000,immutable";

        context.Context.Response.Headers.CacheControl = cacheControl;
    }
});
app.MapGet("/startup-status", (StartupInitializationState state) => Results.Json(state.GetSnapshot()));

app.MapGet("/firmwares/download", (IWebHostEnvironment environment) =>
{
    var firmwareRoot = Path.Combine(environment.ContentRootPath, "Firmwares");
    if (!Directory.Exists(firmwareRoot))
    {
        return Results.NotFound("Firmware folder was not found.");
    }

    using var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var filePath in Directory.EnumerateFiles(firmwareRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(firmwareRoot, filePath).Replace('\\', '/');
            var fileName = Path.GetFileName(filePath);
            if (FirmwareDownloadHelper.ShouldSkip(relativePath, fileName))
            {
                continue;
            }

            FirmwareDownloadHelper.AddSanitizedFile(archive, filePath, relativePath);
        }
    }

    return Results.File(stream.ToArray(), "application/zip", "neststats-firmwares.zip");
});

app.MapGet("/firmwares/download/{fileName}", (IWebHostEnvironment environment, string fileName) =>
{
    if (!fileName.EndsWith(".ino", StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }

    var safeFileName = Path.GetFileName(fileName);
    var filePath = Path.Combine(environment.ContentRootPath, "Firmwares", safeFileName);
    if (!File.Exists(filePath))
    {
        return Results.NotFound();
    }

    var sanitized = FirmwareDownloadHelper.SanitizeSource(File.ReadAllText(filePath));
    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(sanitized),
        "text/x-arduino",
        safeFileName);
});

app.Use(async (context, next) =>
{
    static bool IsStaticOrFrameworkRequest(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        return path.StartsWithSegments("/css", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/js", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/logo.png", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/SK.svg", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/EN.svg", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase);
    }

    static bool WantsHtml(HttpContext httpContext)
    {
        if (HttpMethods.IsGet(httpContext.Request.Method) is false)
        {
            return false;
        }

        var accept = httpContext.Request.Headers.Accept.ToString();
        return string.IsNullOrWhiteSpace(accept) ||
               accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
               accept.Contains("*/*", StringComparison.OrdinalIgnoreCase);
    }

    var path = context.Request.Path;
    if (path.StartsWithSegments("/startup-status", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/language/set", StringComparison.OrdinalIgnoreCase) ||
        IsStaticOrFrameworkRequest(path))
    {
        await next();
        return;
    }

    var startupState = context.RequestServices.GetRequiredService<StartupInitializationState>();
    var snapshot = startupState.GetSnapshot();
    if (snapshot.IsReady)
    {
        await next();
        return;
    }

    if (!WantsHtml(context))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(snapshot);
        return;
    }

    var requestPath = (context.Request.PathBase + context.Request.Path + context.Request.QueryString).ToString();
    static bool IsEnglishCulture(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(CookieRequestCultureProvider.DefaultCookieName, out var cookieValue) ||
            string.IsNullOrWhiteSpace(cookieValue))
        {
            return false;
        }

        var parsed = CookieRequestCultureProvider.ParseCookieValue(cookieValue);
        return string.Equals(parsed?.UICultures.FirstOrDefault().Value, "en", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(parsed?.Cultures.FirstOrDefault().Value, "en", StringComparison.OrdinalIgnoreCase);
    }

    var isEnglish = IsEnglishCulture(context);
    context.Response.StatusCode = snapshot.HasFailed
        ? StatusCodes.Status503ServiceUnavailable
        : StatusCodes.Status202Accepted;
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(startupState.RenderHtml(
        string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath,
        isEnglish));
});
app.UseRequestLocalization();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/language/set", (HttpContext context, AppLanguageService languageService, string culture, string? returnUrl) =>
{
    var normalizedCulture = languageService.Normalize(culture);
    var cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(normalizedCulture));

    context.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        cookieValue,
        new CookieOptions
        {
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            Path = "/"
        });

    if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
    {
        return Results.LocalRedirect(returnUrl);
    }

    return Results.LocalRedirect("/");
});
app.MapRazorPages();
app.Run();

static class FirmwareDownloadHelper
{
    private static readonly Regex SensitiveDefineRegex = new(
        @"^(\s*#define\s+(?:WIFI_SSID|WIFI_PASS|WIFI_PASSWORD|_SSID|_PASSWORD|PROJECT_URL|SUPABASE_URL|API_KEY|SUPABASE_KEY)\s+)""[^""]*""(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SensitiveConstRegex = new(
        @"^(\s*const\s+char\s*\*\s*(?:ssid|password|supabaseUrl|apiKey)\s*=\s*)""[^""]*""(\s*;.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ShouldSkip(string relativePath, string fileName)
    {
        return relativePath.Contains("/build/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.Contains("/managed_components/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.Contains("/.pio/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase);
    }

    public static void AddSanitizedFile(ZipArchive archive, string filePath, string relativePath)
    {
        if (IsTextSource(filePath))
        {
            var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), System.Text.Encoding.UTF8);
            writer.Write(SanitizeSource(File.ReadAllText(filePath)));
            return;
        }

        archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Fastest);
    }

    public static string SanitizeSource(string source)
    {
        var sanitized = new List<string>();
        var skipContinuation = false;

        foreach (var rawLine in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine;
            var trimmed = line.Trim();

            if (skipContinuation)
            {
                skipContinuation = trimmed.EndsWith("\\", StringComparison.Ordinal);
                continue;
            }

            if (line.Contains("SUPABASE_KEY", StringComparison.OrdinalIgnoreCase) &&
                trimmed.StartsWith("#define", StringComparison.OrdinalIgnoreCase))
            {
                sanitized.Add("#define SUPABASE_KEY \"PASTE_SUPABASE_ANON_KEY_HERE\"");
                skipContinuation = trimmed.EndsWith("\\", StringComparison.Ordinal);
                continue;
            }

            line = SensitiveDefineRegex.Replace(line, match =>
            {
                var name = match.Groups[1].Value;
                var suffix = match.Groups[2].Value;
                return name + GetPlaceholder(name) + suffix;
            });

            line = SensitiveConstRegex.Replace(line, match =>
            {
                var name = match.Groups[1].Value;
                var suffix = match.Groups[2].Value;
                return name + GetPlaceholder(name) + suffix;
            });

            sanitized.Add(line);
        }

        return string.Join(Environment.NewLine, sanitized);
    }

    private static bool IsTextSource(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".ino", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".c", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".h", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(filePath).Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPlaceholder(string identifier)
    {
        if (identifier.Contains("SSID", StringComparison.OrdinalIgnoreCase) ||
            identifier.Contains("ssid", StringComparison.OrdinalIgnoreCase))
        {
            return "\"YOUR_WIFI_NAME\"";
        }

        if (identifier.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
            identifier.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return "\"YOUR_WIFI_PASSWORD\"";
        }

        if (identifier.Contains("URL", StringComparison.OrdinalIgnoreCase) ||
            identifier.Contains("supabaseUrl", StringComparison.OrdinalIgnoreCase) ||
            identifier.Contains("PROJECT_URL", StringComparison.OrdinalIgnoreCase))
        {
            return "\"https://YOUR_PROJECT.supabase.co\"";
        }

        return "\"PASTE_SUPABASE_ANON_KEY_HERE\"";
    }
}
