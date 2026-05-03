using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace NestStats2.Services;

public class UserPreferences
{
    public string? LegType { get; set; }
    public string? PackageCode { get; set; }
    public string? SystemType { get; set; }
    public int? Consumption { get; set; }
    public string? InstallLocation { get; set; }
    public string? SmartMeterType { get; set; }
    public bool CustomInverter { get; set; }
    public Guid? InverterId { get; set; }
    public Guid? BatteryId { get; set; }
    public bool BackupCircuits { get; set; }
    public long? OptimizerId { get; set; }
    public int? OptimizerQty { get; set; }
    public bool ApplyNepSubsidy { get; set; }
    public decimal? NepSubsidyAmount { get; set; }
    public bool ApplySiaSubsidy { get; set; }
    public decimal? SiaSubsidyAmount { get; set; }
    public bool ApplyZpSubsidy { get; set; }
    public decimal? ZpSubsidyAmount { get; set; }
    public string? ClientName { get; set; }
    public string? ClientAddress { get; set; }
    public bool ExtraStatic { get; set; }
    public bool ExtraFire { get; set; }
    public bool ExtraLps { get; set; }
    public bool ExtraPermit { get; set; }
    public double? InitialInvestment { get; set; }
    public string? CustomerType { get; set; }
    public double? DegradationPercent { get; set; }
    public double? SystemLossesPercent { get; set; }
    public double? InverterEfficiencyPercent { get; set; }
    public double? PricePerson { get; set; }
    public double? PriceCorp { get; set; }
    public double? EnergyInflationPercent { get; set; }
    public double? GeneralInflationPercent { get; set; }
    public int? LifetimeYears { get; set; }
    public List<StringConfigPreference>? SolarStrings { get; set; }
}

public class StringConfigPreference
{
    public int PanelsCount { get; set; }
    public double Orientation { get; set; }
    public double Tilt { get; set; }
    public double PanelPowerWp { get; set; }
    public double PanelEfficiency { get; set; }
    public double PanelArea { get; set; }
}

public interface IUserPreferencesService
{
    Task<UserPreferences> GetUserPreferencesAsync(string userEmail);
    Task SaveUserPreferencesAsync(string userEmail, UserPreferences preferences);
}

public class CookieUserPreferencesService : IUserPreferencesService
{
    private const string PreferencesCookieName = "FtveCalculatorPrefs";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CookieUserPreferencesService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<UserPreferences> GetUserPreferencesAsync(string userEmail)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            return Task.FromResult(new UserPreferences());
        }

        var cookieValue = context.Request.Cookies[PreferencesCookieName];
        if (string.IsNullOrEmpty(cookieValue))
        {
            return Task.FromResult(new UserPreferences());
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cookieValue));
            var preferences = JsonSerializer.Deserialize<UserPreferences>(json);
            return Task.FromResult(preferences ?? new UserPreferences());
        }
        catch
        {
            return Task.FromResult(new UserPreferences());
        }
    }

    public Task SaveUserPreferencesAsync(string userEmail, UserPreferences preferences)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var json = JsonSerializer.Serialize(preferences);
            var cookieValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            context.Response.Cookies.Append(
                PreferencesCookieName,
                cookieValue,
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddDays(30),
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true
                });
        }
        catch
        {
        }

        return Task.CompletedTask;
    }
}
