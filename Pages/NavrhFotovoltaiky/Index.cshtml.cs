using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NestStats2.Services;

namespace NestStats2.Pages.NavrhFotovoltaiky
{
    public class IndexModel : PageModel
    {
        private readonly IUserPreferencesService _userPreferencesService;
        private readonly INestStatsPvPdfExportService _pdfExportService;

        public IndexModel(
            IUserPreferencesService userPreferencesService,
            INestStatsPvPdfExportService pdfExportService)
        {
            _userPreferencesService = userPreferencesService;
            _pdfExportService = pdfExportService;
        }

        /* ───────────────────────────── INPUT-MODELOVÉ TRIEDY ───────────────────────────── */

        [BindProperty(SupportsGet = true, Name = "amount")]
        [DisplayFormat(DataFormatString = "{0:0.00}", ApplyFormatInEditMode = true)]
        public decimal Amount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string LegType { get; set; } = "";

        [BindProperty(SupportsGet = true)]
        public int? Panels { get; set; }

        [BindProperty(SupportsGet = true)]
        public double? PanelTotalEfficiency { get; set; }

        [BindProperty(SupportsGet = true)]
        public double? PanelTotalSurface { get; set; }

        public class StringConfig
        {
            public int PanelsCount { get; set; }
            public double Orientation { get; set; }
            public double Tilt { get; set; }
            public double PanelPowerWp { get; set; }
            public double PanelEfficiency { get; set; }
            public double PanelArea { get; set; }
        }

        public class InputParameters
        {
            public List<StringConfig> Strings { get; set; } = new();
            
            public string? ClientName { get; set; }
            public double DegradationPercent { get; set; }
            public double SystemLossesPercent { get; set; }
            public double InverterEfficiencyPercent { get; set; } = 96;
            public double PricePerson { get; set; }
            public double PriceCorp { get; set; }
            public string CustomerType { get; set; } = "person";
            public double EnergyInflationPercent { get; set; }
            public double GeneralInflationPercent { get; set; }
            public double InitialInvestment { get; set; }
            public int LifetimeYears { get; set; }
            
            public string BillMode { get; set; } = "monthly"; // "monthly" | "annual"
            public double MonthlyBill { get; set; } = 0;      // € / mesiac
            public double AnnualBill { get; set; } = 0;       // € / rok
            public double SelfConsumptionPercent { get; set; } = 85; // 0..100

        }

        public class AnnualResults
        {
            public int Year { get; set; }
            public double ProductionKwh { get; set; }
            public double CumulativeProductionKwh { get; set; }
            public double PricePerKwh { get; set; }
            public double ValueEuros { get; set; }
            public double CumulativeValueEuros { get; set; }
            
            public double BillBeforeEuros { get; set; }            // účet bez FV v danom roku
            public double BillAfterEuros { get; set; }             // účet s FV v danom roku
            public double SavingsEuros { get; set; }               // úspora v danom roku
            public double CumulativeSavingsEuros { get; set; }     // kumulatívna úspora
        }

        public class CalculationResult
        {
            public List<double> MonthlyProduction { get; set; } = new();
            public List<AnnualResults> AnnualResults { get; set; } = new();
            public double Npv { get; set; }
            public double PaybackYears { get; set; }
            public double TotalProduction { get; set; }
            public double InstalledPowerKw { get; set; }
            public double AverageAnnualProduction { get; set; }
            
            public double BaselineAnnualBill { get; set; }               // € / rok (dnes)
            public double BaselineAnnualConsumptionKwh { get; set; }     // kWh / rok (odhad z účtu)
            public double SelfConsumptionPercent { get; set; }
            public double TotalSavings { get; set; }

        }

        /* ───────────────────────────── URL-PARAMETRE (prefill) ───────────────────────────── */

        [BindProperty(SupportsGet = true)]
        public double? initInvestment { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? custType { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? panels { get; set; }

        [BindProperty(SupportsGet = true)]
        public double? panelWp { get; set; }

        public InputParameters Prefill { get; private set; } = new();
        
        private const string ClientNameCookie = "nep_client_name";

        private string? ReadClientNameCookie()
        {
            if (Request.Cookies.TryGetValue(ClientNameCookie, out var v))
                return string.IsNullOrWhiteSpace(v) ? null : v;
            return null;
        }

        private void WriteClientNameCookie(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Response.Cookies.Delete(ClientNameCookie);
                return;
            }

            Response.Cookies.Append(
                ClientNameCookie,
                value.Trim(),
                new CookieOptions
                {
                    Expires = DateTimeOffset.Now.AddDays(180),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps
                });
        }


        /* ───────────────────────────── GET ───────────────────────────── */

        public async Task OnGetAsync()
        {
            // Load user preferences
            var userEmail = GetUserEmail();
            var preferences = await _userPreferencesService.GetUserPreferencesAsync(userEmail);
            
            var clientFromPrefs = string.IsNullOrWhiteSpace(preferences.ClientName) ? null : preferences.ClientName.Trim();
            var clientFromCookie = ReadClientNameCookie();

            var client = clientFromPrefs ?? clientFromCookie;

            ViewData["ClientName"] = client;
            Prefill.ClientName = client;



            // Check if URL parameters are provided (indicating navigation from index page)
            bool hasUrlParams = Amount > 0 || !string.IsNullOrEmpty(LegType) || Panels.HasValue || panels.HasValue;

            // 1. Initial Investment - Priority: URL > Preferences > Default
            if (Amount > 0)
            {
                Prefill.InitialInvestment = (double)Amount;
            }
            else if (preferences.InitialInvestment.HasValue)
            {
                Prefill.InitialInvestment = preferences.InitialInvestment.Value;
            }
            else
            {
                Prefill.InitialInvestment = 0; // or your default value
            }

            // 2. Customer Type - Priority: URL > Preferences > Default
            if (!string.IsNullOrEmpty(LegType))
            {
                Prefill.CustomerType = string.Equals(LegType, "PO", StringComparison.OrdinalIgnoreCase) ? "corp" : "person";
            }
            else if (!string.IsNullOrEmpty(preferences.CustomerType))
            {
                Prefill.CustomerType = preferences.CustomerType;
            }
            else
            {
                Prefill.CustomerType = "person"; // default
            }

            // 3. Solar Strings Configuration - Priority: URL > Preferences > Defaults
            if (Panels.HasValue || panels.HasValue || PanelTotalEfficiency.HasValue || PanelTotalSurface.HasValue || panelWp.HasValue)
            {
                // URL parameters provided - use them
                Prefill.Strings.Add(new StringConfig
                {
                    PanelsCount = Panels ?? panels ?? 22, // default if still null
                    PanelPowerWp = panelWp ?? 455,
                    Orientation = 180,
                    Tilt = 30,
                    PanelEfficiency = (PanelTotalEfficiency ?? 228) / 10, // Convert from your format
                    PanelArea = (PanelTotalSurface ?? 2171570) / 1000000 // Convert from your format
                });
            }
            else if (preferences.SolarStrings?.Any() == true)
            {
                // No URL params, but preferences exist - use preferences
                Prefill.Strings = preferences.SolarStrings.Select(s => new StringConfig
                {
                    PanelsCount = s.PanelsCount,
                    PanelPowerWp = s.PanelPowerWp,
                    Orientation = s.Orientation,
                    Tilt = s.Tilt,
                    PanelEfficiency = s.PanelEfficiency,
                    PanelArea = s.PanelArea
                }).ToList();
            }
            else
            {
                // No URL params, no preferences - use defaults
                Prefill.Strings.Add(new StringConfig
                {
                    PanelsCount = 20,
                    PanelPowerWp = 455,
                    Orientation = 180,
                    Tilt = 30,
                    PanelEfficiency = 22.8,
                    PanelArea = 2.171572
                });
            }

            // after completely built Prefill
            if (Prefill.Strings.Any())
            {
                var s0 = Prefill.Strings[0];

                // panels / panelCount 
                if (!Panels.HasValue) Panels = s0.PanelsCount;

                // panels / panelWp  
                if (!panels.HasValue) panels = s0.PanelsCount;
                if (!panelWp.HasValue) panelWp = s0.PanelPowerWp;

                // panel efficiency (%)
                if (!PanelTotalEfficiency.HasValue)
                    PanelTotalEfficiency = s0.PanelEfficiency * 10;      

                // panel area (mm²)
                if (!PanelTotalSurface.HasValue)
                    PanelTotalSurface = s0.PanelArea * 1_000_000;         

                // amount 
                if (Amount == 0 && Prefill.InitialInvestment > 0)
                    Amount = (decimal)Prefill.InitialInvestment;
                // customer type
                if (string.IsNullOrEmpty(LegType))
                    LegType = Prefill.CustomerType.Equals("corp", StringComparison.OrdinalIgnoreCase)
                              ? "PO" : "FO";

                if (Prefill.InitialInvestment > 0)
                {
                    // URL => Amount, inak cookie/default
                    if (Amount <= 0)                    // decimal
                        Amount = (decimal)Prefill.InitialInvestment;

                    // Ak v .cshtml používaš initInvestment (double?)
                    if (!initInvestment.HasValue)       // double?
                        initInvestment = Prefill.InitialInvestment;
                }

            }

            // 4. Other parameters - Priority: Preferences > Defaults (these typically don't come from URL)
            Prefill.PricePerson = preferences.PricePerson ?? 0.18;
            Prefill.PriceCorp = preferences.PriceCorp ?? 0.22;
            Prefill.DegradationPercent = preferences.DegradationPercent ?? 0.4;
            Prefill.SystemLossesPercent = preferences.SystemLossesPercent ?? 10;
            Prefill.InverterEfficiencyPercent = preferences.InverterEfficiencyPercent ?? 96;
            Prefill.EnergyInflationPercent = preferences.EnergyInflationPercent ?? 5;
            Prefill.GeneralInflationPercent = preferences.GeneralInflationPercent ?? 3;
            Prefill.LifetimeYears = preferences.LifetimeYears ?? 30;

            ModelState.Remove(nameof(Amount));
        }

        /* ───────────────────────────── POST (AJAX JSON) ───────────────────────────── */

        public async Task<IActionResult> OnPostCalculateAsync([FromBody] InputParameters input)
        {
            WriteClientNameCookie(input.ClientName);
            try
            {
                // Save user preferences
                await SaveUserPreferencesAsync(input);

                var result = Calculate(input);
                return new JsonResult(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        public IActionResult OnPostExportPdf([FromBody] InputParameters input)
        {
            WriteClientNameCookie(input.ClientName);
            try
            {
                // Nepotrebujeme to počítať na FE – tu si to spočítame znova,
                // aby PDF vždy sedelo s aktuálnymi vstupmi.
                var result = Calculate(input);

                var bytes = _pdfExportService.GenerateSavingsReport(input, result);

                var fileName = $"FV_kalkulacka_uspory_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

                return File(bytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }


        /* ───────────────────────────── USER PREFERENCES HANDLING ───────────────────────────── */

        private async Task SaveUserPreferencesAsync(InputParameters input)
        {
            try
            {
                var userEmail = GetUserEmail();
                var preferences = await _userPreferencesService.GetUserPreferencesAsync(userEmail);

                // Update preferences with current input
                preferences.InitialInvestment = input.InitialInvestment;
                preferences.CustomerType = input.CustomerType;
                preferences.DegradationPercent = input.DegradationPercent;
                preferences.SystemLossesPercent = input.SystemLossesPercent;
                preferences.InverterEfficiencyPercent = input.InverterEfficiencyPercent;
                preferences.PricePerson = input.PricePerson;
                preferences.PriceCorp = input.PriceCorp;
                preferences.EnergyInflationPercent = input.EnergyInflationPercent;
                preferences.GeneralInflationPercent = input.GeneralInflationPercent;
                preferences.LifetimeYears = input.LifetimeYears;
                // NEW:
                preferences.ClientName = string.IsNullOrWhiteSpace(input.ClientName) ? null : input.ClientName.Trim();

                // Save solar strings configuration
                preferences.SolarStrings = input.Strings?.Select(s => new StringConfigPreference
                {
                    PanelsCount = s.PanelsCount,
                    Orientation = s.Orientation,
                    Tilt = s.Tilt,
                    PanelPowerWp = s.PanelPowerWp,
                    PanelEfficiency = s.PanelEfficiency,
                    PanelArea = s.PanelArea
                }).ToList() ?? new List<StringConfigPreference>();

                await _userPreferencesService.SaveUserPreferencesAsync(userEmail, preferences);
            }
            catch
            {
                // Log error but don't fail the calculation
                // You can add logging here if needed
            }
        }

        private string GetUserEmail()
        {
            // Implement this method based on your authentication system
            // For example, if using cookies or session:
            return HttpContext.Request.Cookies["user_email"] ?? "anonymous";

            // Or if using claims-based authentication:
            // return User.FindFirst(ClaimTypes.Email)?.Value ?? "anonymous";

            // Or use a session-based approach:
            // return HttpContext.Session.GetString("user_email") ?? HttpContext.Connection.Id;
        }

        /* ───────────────────────────── VÝPOČET ───────────────────────────── */

        private CalculationResult Calculate(InputParameters p)
        {
            /* ===== Inštalovaný výkon ===== */
            double totalInstalledPower = p.Strings.Sum(s => s.PanelsCount * s.PanelPowerWp / 1000.0);

            /* ===== Mesačná výroba ===== */
            var monthly = new double[12];   // index 0 = január …

            for (int m = 0; m < 12; m++)
            {
                double monthSum = 0;

                foreach (var s in p.Strings)
                {
                    double stringPowerKw = s.PanelsCount * s.PanelPowerWp / 1000.0;
                    double orientationCoef = GetOrientationEfficiency(s.Orientation, s.Tilt);

                    double totalEff = (p.InverterEfficiencyPercent / 100.0) *
                                      ((100 - p.SystemLossesPercent) / 100.0);

                    double peakSun = MonthlyPeakSunHours[m];
                    double days = DateTime.DaysInMonth(DateTime.Today.Year, m + 1);

                    double prod = stringPowerKw * peakSun * days * orientationCoef * totalEff;
                    monthSum += Math.Max(0, prod);
                }

                monthly[m] = Math.Round(monthSum, 2);
            }

            /* ===== Ročné výsledky ===== */
var annuals = new List<AnnualResults>();
double cumProd = 0;
double cumValue = 0;

double basePrice = p.CustomerType == "person" ? p.PricePerson : p.PriceCorp;
basePrice = Math.Max(0.000001, basePrice);

double billModeAnnual = 0;
if (!string.IsNullOrWhiteSpace(p.BillMode) && p.BillMode.Equals("annual", StringComparison.OrdinalIgnoreCase))
    billModeAnnual = p.AnnualBill > 0 ? p.AnnualBill : p.MonthlyBill * 12.0;
else
    billModeAnnual = p.MonthlyBill > 0 ? p.MonthlyBill * 12.0 : p.AnnualBill;

billModeAnnual = Math.Max(0, billModeAnnual);

// odhad spotreby (kWh/rok) z dnešnej ceny
double baselineAnnualConsumptionKwh = billModeAnnual > 0 ? (billModeAnnual / basePrice) : 0;

// self-consumption (koľko vyrobenej energie reálne šetrí účet)
double selfConsPct = Math.Clamp(p.SelfConsumptionPercent, 0, 100);
double selfCons = selfConsPct / 100.0;

double cumSavings = 0;

for (int y = 1; y <= p.LifetimeYears; y++)
{
    double degrFactor = Math.Pow(1 - p.DegradationPercent / 100, y - 1);
    double yearlyProd = monthly.Sum() * degrFactor;
    cumProd += yearlyProd;

    double inflFactor = Math.Pow(1 + p.EnergyInflationPercent / 100, y - 1);
    double priceNow = basePrice * inflFactor;

    double yearlyValue = yearlyProd * priceNow;          // hodnota vyrobenej energie
    cumValue += yearlyValue;

    // účet bez FV (ak zadaný)
    double billBefore = baselineAnnualConsumptionKwh > 0 ? baselineAnnualConsumptionKwh * priceNow : 0;

// „potenciál úspory“ (limitovaný vlastnou spotrebou)
    double potentialSavings = yearlyValue * selfCons;

// reálna úspora iba keď je zadaný účet, inak 0 (len doplnkový údaj)
    double savings = billBefore > 0 ? Math.Min(billBefore, potentialSavings) : 0;
    double billAfter = billBefore > 0 ? (billBefore - savings) : 0;

    cumSavings += savings;

    annuals.Add(new AnnualResults
    {
        Year = y,
        ProductionKwh = Math.Round(yearlyProd, 2),
        CumulativeProductionKwh = Math.Round(cumProd, 2),
        PricePerKwh = Math.Round(priceNow, 3),
        ValueEuros = Math.Round(yearlyValue, 2),
        CumulativeValueEuros = Math.Round(cumValue, 2),

        BillBeforeEuros = Math.Round(billBefore, 2),
        BillAfterEuros = Math.Round(billAfter, 2),
        SavingsEuros = Math.Round(savings, 2),
        CumulativeSavingsEuros = Math.Round(cumSavings, 2),
    });
}

/* ===== NPV & Payback (ako predtým: z hodnoty vyrobenej energie) ===== */
double discount = p.GeneralInflationPercent / 100.0;
double npv = -p.InitialInvestment;

foreach (var a in annuals)
{
    npv += a.ValueEuros / Math.Pow(1 + discount, a.Year);
}

// exact (fractional) payback z cumulativeValue
double payback = 0;
double prevCumValue = 0;

foreach (var a in annuals)
{
    if (a.CumulativeValueEuros >= p.InitialInvestment)
    {
        double toRecoup = p.InitialInvestment - prevCumValue;
        double fraction = a.ValueEuros > 0 ? (toRecoup / a.ValueEuros) : 0;
        payback = (a.Year - 1) + fraction;
        break;
    }
    prevCumValue = a.CumulativeValueEuros;
}


return new CalculationResult
{
    MonthlyProduction = monthly.ToList(),
    AnnualResults = annuals,
    Npv = Math.Round(npv, 2),
    PaybackYears = payback,
    TotalProduction = Math.Round(cumProd, 2),
    InstalledPowerKw = Math.Round(totalInstalledPower, 2),
    AverageAnnualProduction = Math.Round(monthly.Sum(), 0),

    BaselineAnnualBill = Math.Round(billModeAnnual, 2),
    BaselineAnnualConsumptionKwh = Math.Round(baselineAnnualConsumptionKwh, 0),
    SelfConsumptionPercent = selfConsPct,
    TotalSavings = Math.Round(cumSavings, 2)
};

        }

        /* ───────────────────────────── Pomocné funkcie ───────────────────────────── */

        // Peak-sun-hours pre Slovensko (január … december)
        private static readonly double[] MonthlyPeakSunHours =
        {
            1.5, 2.5, 3.5, 4.5, 5.5, 6.0, 6.5, 5.5, 4.0, 2.5, 1.5, 1.0
        };

        // Účinnosti pre zadané orientácie/sklony
        private static readonly Dictionary<(int ori, int tilt), double> OrientationEfficiency = new()
        {
            {(90,  0), 0.87}, {(90, 15), 0.91}, {(90, 30), 0.93}, {(90, 45), 0.91}, {(90, 60), 0.85}, {(90, 90), 0.67},
            {(135, 0), 0.87}, {(135,15), 0.93}, {(135,30), 0.97}, {(135,45), 0.98}, {(135,60), 0.94}, {(135,90), 0.77},
            {(180, 0), 0.87}, {(180,15), 0.94}, {(180,30), 1.00}, {(180,45), 1.02}, {(180,60), 0.99}, {(180,90), 0.82},
            {(225, 0), 0.87}, {(225,15), 0.93}, {(225,30), 0.97}, {(225,45), 0.98}, {(225,60), 0.94}, {(225,90), 0.77},
            {(270, 0), 0.87}, {(270,15), 0.91}, {(270,30), 0.93}, {(270,45), 0.91}, {(270,60), 0.85}, {(270,90), 0.67}
        };

        private double GetOrientationEfficiency(double orientation, double tilt)
        {
            orientation = (orientation % 360 + 360) % 360;
            tilt = Math.Clamp(tilt, 0, 90);

            var oris = new[] { 90, 135, 180, 225, 270 };
            var tilts = new[] { 0, 15, 30, 45, 60, 90 };

            if (orientation >= 315 || orientation <= 45)
                return GetNorthernEfficiency(tilt);

            int loIdx = -1, hiIdx = -1;
            for (int i = 0; i < oris.Length - 1; i++)
                if (orientation >= oris[i] && orientation <= oris[i + 1]) { loIdx = i; hiIdx = i + 1; break; }

            if (loIdx == -1)
            {
                if (orientation >= 270)
                    return InterpolateEfficiency(270, 90, tilt, orientation);
                if (orientation <= 90)
                    return InterpolateEfficiency(270, 90, tilt, orientation + 360);
            }

            return InterpolateEfficiency(oris[loIdx], oris[hiIdx], tilt, orientation);
        }

        private double InterpolateEfficiency(double ori1, double ori2, double tilt, double targetOri)
        {
            var tilts = new[] { 0, 15, 30, 45, 60, 90 };

            int loTilt = 0, hiTilt = tilts.Length - 1;
            for (int i = 0; i < tilts.Length - 1; i++)
                if (tilt >= tilts[i] && tilt <= tilts[i + 1]) { loTilt = i; hiTilt = i + 1; break; }

            double e11 = GetEfficiency(ori1, tilts[loTilt]);
            double e12 = GetEfficiency(ori1, tilts[hiTilt]);
            double e21 = GetEfficiency(ori2, tilts[loTilt]);
            double e22 = GetEfficiency(ori2, tilts[hiTilt]);

            double tOri = (targetOri - ori1) / (ori2 - ori1);
            double tTilt = (tilt - tilts[loTilt]) / (tilts[hiTilt] - tilts[loTilt]);

            double e1 = e11 * (1 - tOri) + e21 * tOri;
            double e2 = e12 * (1 - tOri) + e22 * tOri;

            return e1 * (1 - tTilt) + e2 * tTilt;
        }

        private double GetEfficiency(double ori, double tilt) =>
            OrientationEfficiency.TryGetValue(((int)Math.Round(ori), (int)Math.Round(tilt)), out var eff)
                ? eff
                : GetNorthernEfficiency(tilt);

        private double GetNorthernEfficiency(double tilt)
        {
            var north = new Dictionary<int, double>
            {
                { 0, 0.65 }, {15, 0.70}, {30, 0.75},
                {45, 0.72 }, {60, 0.65}, {90, 0.45}
            };

            var tilts = new[] { 0, 15, 30, 45, 60, 90 };
            int lo = 0, hi = tilts.Length - 1;
            for (int i = 0; i < tilts.Length - 1; i++)
                if (tilt >= tilts[i] && tilt <= tilts[i + 1]) { lo = tilts[i]; hi = tilts[i + 1]; break; }

            double t = (tilt - lo) / (hi - lo);
            return north[lo] + (north[hi] - north[lo]) * t;
        }
    }
}
