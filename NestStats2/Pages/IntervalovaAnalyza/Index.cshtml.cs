using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NestStats2.Services;
using System.IO;

namespace NestStats2.Pages.IntervalovaAnalyza;

public class IndexModel : PageModel
{
    private readonly IWebHostEnvironment _env;
    // ===== Injecty =====
    private readonly IAnalysisPdfExportService2 _pdfExportService2;

    public IndexModel(IWebHostEnvironment env, IAnalysisPdfExportService2 pdfExportService2)
    {
        _env = env;
        _pdfExportService2 = pdfExportService2;
    }

    // ===== Inputs =====
    [BindProperty] public IFormFile? File { get; set; }
    [BindProperty] public int YearlyYield { get; set; } = 1050;

    [BindProperty] public double KwMin { get; set; } = 2.0;
    [BindProperty] public double KwMax { get; set; } = 15.0;
    [BindProperty] public double KwStep { get; set; } = 0.5;

    [BindProperty] public double BatMin { get; set; } = 0.0;
    [BindProperty] public double BatMax { get; set; } = 20.0;
    [BindProperty] public double BatStep { get; set; } = 1.0;

    [BindProperty] public double PriceImport { get; set; } = 0.18; // €/kWh
    [BindProperty] public double PriceExport { get; set; } = 0.06; // €/kWh
    [BindProperty] public double CapexPerKwp { get; set; } = 1200; // €/kWp
    [BindProperty] public double CapexPerKwh { get; set; } = 500;  // €/kWh
    [BindProperty] public double CapexFixed { get; set; } = 1500;  // €
    [BindProperty] public double OandMpercent { get; set; } = 1.0; // % CAPEX per year
    [BindProperty] public double DiscountRate { get; set; } = 6.0;  // %
    [BindProperty] public int HorizonYears { get; set; } = 15;

    [BindProperty] public double BatteryMaxKW { get; set; } = 5.0;  // kW power limit batérie (nab./vyb.)
    [BindProperty] public double EffChargeDischargePct { get; set; } = 95.0; // per-direction eff

    [BindProperty] public double InverterKW { get; set; } = 10.0;   // AC výkon meniča
    [BindProperty] public double ExportLimitKW { get; set; } = -1.0; // -1 => použije sa InverterKW
    [BindProperty] public bool TouEnabled { get; set; } = false;     // default vypnuté

    [BindProperty]
    public string MonthlySharesCsv { get; set; } =
        "0.02,0.04,0.08,0.11,0.13,0.14,0.14,0.12,0.10,0.07,0.03,0.02";

    // ===== Outputs =====
    public AnalysisResult? Result { get; private set; }
    public DesignPoint? Opt { get; private set; }
    public List<DesignPoint>? Top { get; private set; }

    // === charts (JSON do JS) ===
    public bool HasCharts { get; private set; }
    public string? JsonLabels96 { get; private set; }
    public string? JsonAvgCons { get; private set; }
    public string? JsonAvgPv { get; private set; }
    public string? JsonAvgImport { get; private set; }
    public string? JsonMonthlyLabels { get; private set; }
    public string? JsonMonthlyCons { get; private set; }
    public string? JsonMonthlyPv { get; private set; }
    public string? JsonMonthlyImp { get; private set; }
    public string? JsonMonthlyExp { get; private set; }

    // === NEW: denné súčty spotreby (graf)
    public string? JsonDailyLabels { get; private set; }
    public string? JsonDailyConsumption { get; private set; }

    public void OnGet() { }

    // ===== Session persist =====
    private const string SessionPointsKey = "ZSDis:Points:v1";
    private const string SessionConfigKey = "ZSDis:Config:v1";
    private sealed record PointDto(long Ticks, double E);

    private static void SavePointsToSession(ISession s, List<IntervalPoint> pts)
    {
        var dto = pts.Select(p => new PointDto(p.Timestamp.Ticks, p.EnergyKWh)).ToList();
        var json = JsonSerializer.Serialize(dto);
        s.SetString(SessionPointsKey, json);
    }
    private static List<IntervalPoint>? LoadPointsFromSession(ISession s)
    {
        var json = s.GetString(SessionPointsKey);
        if (string.IsNullOrWhiteSpace(json)) return null;
        var dto = JsonSerializer.Deserialize<List<PointDto>>(json) ?? new();
        return dto.Select(x => new IntervalPoint(new DateTime(x.Ticks, DateTimeKind.Unspecified), x.E)).ToList();
    }
    private void SaveConfigToSession()
    {
        var cfg = new
        {
            YearlyYield,
            KwMin,
            KwMax,
            KwStep,
            BatMin,
            BatMax,
            BatStep,
            PriceImport,
            PriceExport,
            CapexPerKwp,
            CapexPerKwh,
            CapexFixed,
            OandMpercent,
            DiscountRate,
            HorizonYears,
            BatteryMaxKW,
            EffChargeDischargePct,
            MonthlySharesCsv,
            InverterKW,
            ExportLimitKW,
            TouEnabled
        };
        HttpContext.Session.SetString(SessionConfigKey, JsonSerializer.Serialize(cfg));
    }

    // ===== Handlers =====
    public async Task<IActionResult> OnPostAnalyzeAsync()
    {
        List<IntervalPoint>? points = null;

        // 1) ak je priložený nový súbor → parsuj, validuj, ulož do Session
        if (File != null && File.Length > 0)
        {
            try
            {
                points = await ParseAsync(File);
                if (points.Count == 0)
                {
                    ModelState.AddModelError("", "V súbore sa nenašli 15-min intervaly.");
                    return Page();
                }
                SavePointsToSession(HttpContext.Session, points);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Chyba parsingu: {ex.Message}");
                return Page();
            }
        }

        // 2) inak načítaj z Session
        points ??= LoadPointsFromSession(HttpContext.Session);
        if (points == null || points.Count == 0)
        {
            ModelState.AddModelError("", "Najprv nahráj export (.xls/.xlsx/.csv).");
            return Page();
        }

        try
        {
            // Spotrebné KPI
            Result = AnalyzeConsumption(points);

            // Denné súčty pre nový graf
            var dLabels = Result.PerDay.Select(d => d.Date.ToString("d.M.", CultureInfo.GetCultureInfo("sk-SK"))).ToArray();
            var dVals = Result.PerDay.Select(d => d.TotalKWh).ToArray();
            JsonDailyLabels = JsonSerializer.Serialize(dLabels);
            JsonDailyConsumption = JsonSerializer.Serialize(dVals);

            // Simulácia & optimalizácia
            var shares = ParseShares(MonthlySharesCsv);
            if (Math.Abs(shares.Sum() - 1.0) > 1e-6)
            {
                var s = shares.Sum();
                if (s > 0) for (int i = 0; i < 12; i++) shares[i] /= s;
            }

            var sim = new Simulator(points, YearlyYield, shares);
            var eff = Math.Clamp(EffChargeDischargePct / 100.0, 0.5, 1.0);
            var pv1 = sim.BuildPvSeries(1.0); // 1 kWp profil

            var candidates = new List<DesignPoint>(1024);
            double exportLimitKW = (ExportLimitKW > 0 ? ExportLimitKW : InverterKW);

            for (double kw = KwMin; kw <= KwMax + 1e-9; kw += KwStep)
            {
                var pvk = pv1.Select(v => v * kw).ToArray();
                var totalPvKwh = pvk.Sum();

                for (double bat = BatMin; bat <= BatMax + 1e-9; bat += BatStep)
                {
                    var flow = sim.RunBattery(pvk, bat, BatteryMaxKW, eff, exportLimitKW);
                    var econ = EvaluateEconomics(kw, bat, totalPvKwh, flow, PriceImport, PriceExport,
                                                 CapexPerKwp, CapexPerKwh, CapexFixed, OandMpercent / 100.0,
                                                 DiscountRate / 100.0, HorizonYears);

                    candidates.Add(new DesignPoint
                    {
                        KwP = Math.Round(kw, 2),
                        BatKWh = Math.Round(bat, 2),
                        PvKWh = Math.Round(totalPvKwh, 0),
                        ImportKWh = Math.Round(flow.ImportKWh, 0),
                        ExportKWh = Math.Round(flow.ExportKWh, 0),
                        SelfConsumptionPct = SafePct(totalPvKwh - flow.ExportKWh, totalPvKwh),
                        AutarkyPct = SafePct(totalPvKwh - flow.ExportKWh, Result!.PerDay.Sum(d => d.TotalKWh)),
                        NPV = Math.Round(econ.NPV, 0),
                        PaybackYears = econ.PaybackYears
                    });
                }
            }

            // Výber najlepšieho a top zoznam
            var bestByNPV = candidates.OrderByDescending(c => c.NPV).ToList();
            Opt = bestByNPV.FirstOrDefault();

            // Poistka proti „triviálne malej“ voľbe pri všeobecne zlých NPV
            if (Opt != null
                && Math.Abs(Opt.KwP - Math.Round(KwMin, 2)) < 1e-6
                && Math.Abs(Opt.BatKWh - Math.Round(BatMin, 2)) < 1e-6)
            {
                var withPayback = candidates.Where(c => c.PaybackYears is not null).OrderBy(c => c.PaybackYears).FirstOrDefault();
                if (withPayback is not null) Opt = withPayback;
                else
                {
                    var byAutarky = candidates.OrderByDescending(c => c.AutarkyPct).FirstOrDefault();
                    if (byAutarky is not null) Opt = byAutarky;
                }
            }

            Top = bestByNPV.Take(20).ToList();

            // === detailné dáta pre grafy pre zvolenú Opt ===
            if (Opt != null)
            {
                var pvk = pv1.Select(v => v * Opt.KwP).ToArray();
                var det = sim.RunBatteryDetailed(pvk, Opt.BatKWh, BatteryMaxKW, eff, exportLimitKW);

                // priemerný 15-min profil (naprieč dňami): spotreba / PV / import
                var len = det.GridImport.Length;
                var avgCons = new double[96]; var avgPv = new double[96]; var avgImp = new double[96]; var counts = new int[96];
                for (int i = 0; i < len; i++)
                {
                    int slot = (i % 96);
                    avgCons[slot] += points[i].EnergyKWh;
                    avgPv[slot] += pvk[i];
                    avgImp[slot] += det.GridImport[i];
                    counts[slot]++;
                }
                for (int s = 0; s < 96; s++)
                {
                    var c = Math.Max(1, counts[s]);
                    avgCons[s] /= c; avgPv[s] /= c; avgImp[s] /= c;
                }

                // mesačné sumy
                var mCons = new double[12]; var mPv = new double[12]; var mImp = new double[12]; var mExp = new double[12];
                for (int i = 0; i < len; i++)
                {
                    int m = points[i].Timestamp.Month - 1;
                    mCons[m] += points[i].EnergyKWh;
                    mPv[m] += pvk[i];
                    mImp[m] += det.GridImport[i];
                    mExp[m] += det.GridExport[i];
                }

                var labels96 = Enumerable.Range(0, 96).Select(i => $"{(i / 4):00}:{(i % 4) * 15:00}").ToArray();
                var mLabels = new[] { "Jan", "Feb", "Mar", "Apr", "Máj", "Jún", "Júl", "Aug", "Sep", "Okt", "Nov", "Dec" };
                var opts = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

                JsonLabels96 = JsonSerializer.Serialize(labels96, opts);
                JsonAvgCons = JsonSerializer.Serialize(avgCons, opts);
                JsonAvgPv = JsonSerializer.Serialize(avgPv, opts);
                JsonAvgImport = JsonSerializer.Serialize(avgImp, opts);

                JsonMonthlyLabels = JsonSerializer.Serialize(mLabels, opts);
                JsonMonthlyCons = JsonSerializer.Serialize(mCons, opts);
                JsonMonthlyPv = JsonSerializer.Serialize(mPv, opts);
                JsonMonthlyImp = JsonSerializer.Serialize(mImp, opts);
                JsonMonthlyExp = JsonSerializer.Serialize(mExp, opts);

                HasCharts = true;
            }

            // ulož poslednú konfiguráciu do Session (pre PDF)
            SaveConfigToSession();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Chyba: {ex.Message}");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostExportPdfAsync()
    {
        var points = LoadPointsFromSession(HttpContext.Session);
        if (points == null || points.Count == 0)
        {
            ModelState.AddModelError("", "Nie sú uložené dáta. Najprv nahráj a spusti analýzu.");
            return Page();
        }

        // Pre konzistenciu PDF sprav ten istý výpočet ako v Analyze
        Result = AnalyzeConsumption(points);

        var dLabels = Result.PerDay.Select(d => d.Date.ToString("d.M.", CultureInfo.GetCultureInfo("sk-SK"))).ToArray();
        var dVals = Result.PerDay.Select(d => d.TotalKWh).ToArray();
        JsonDailyLabels = JsonSerializer.Serialize(dLabels);
        JsonDailyConsumption = JsonSerializer.Serialize(dVals);

        var shares = ParseShares(MonthlySharesCsv);
        if (Math.Abs(shares.Sum() - 1.0) > 1e-6)
        {
            var s = shares.Sum();
            if (s > 0) for (int i = 0; i < 12; i++) shares[i] /= s;
        }

        var sim = new Simulator(points, YearlyYield, shares);
        var eff = Math.Clamp(EffChargeDischargePct / 100.0, 0.5, 1.0);
        var pv1 = sim.BuildPvSeries(1.0);

        var candidates = new List<DesignPoint>(1024);
        double exportLimitKW = (ExportLimitKW > 0 ? ExportLimitKW : InverterKW);

        for (double kw = KwMin; kw <= KwMax + 1e-9; kw += KwStep)
        {
            var pvk = pv1.Select(v => v * kw).ToArray();
            var totalPvKwh = pvk.Sum();

            for (double bat = BatMin; bat <= BatMax + 1e-9; bat += BatStep)
            {
                var flow = sim.RunBattery(pvk, bat, BatteryMaxKW, eff, exportLimitKW);
                var econ = EvaluateEconomics(kw, bat, totalPvKwh, flow, PriceImport, PriceExport,
                                             CapexPerKwp, CapexPerKwh, CapexFixed, OandMpercent / 100.0,
                                             DiscountRate / 100.0, HorizonYears);

                candidates.Add(new DesignPoint
                {
                    KwP = Math.Round(kw, 2),
                    BatKWh = Math.Round(bat, 2),
                    PvKWh = Math.Round(totalPvKwh, 0),
                    ImportKWh = Math.Round(flow.ImportKWh, 0),
                    ExportKWh = Math.Round(flow.ExportKWh, 0),
                    SelfConsumptionPct = SafePct(totalPvKwh - flow.ExportKWh, totalPvKwh),
                    AutarkyPct = SafePct(totalPvKwh - flow.ExportKWh, Result!.PerDay.Sum(d => d.TotalKWh)),
                    NPV = Math.Round(econ.NPV, 0),
                    PaybackYears = econ.PaybackYears
                });
            }
        }

        var bestByNPV = candidates.OrderByDescending(c => c.NPV).ToList();
        Opt = bestByNPV.FirstOrDefault();
        Opt = bestByNPV.FirstOrDefault();
        if (Opt != null
            && Math.Abs(Opt.KwP - Math.Round(KwMin, 2)) < 1e-6
            && Math.Abs(Opt.BatKWh - Math.Round(BatMin, 2)) < 1e-6)
        {
            var withPayback = candidates.Where(c => c.PaybackYears is not null).OrderBy(c => c.PaybackYears).FirstOrDefault();
            if (withPayback is not null) Opt = withPayback;
            else Opt = candidates.OrderByDescending(c => c.AutarkyPct).FirstOrDefault() ?? Opt;
        }
        Top = bestByNPV.Take(20).ToList();

        // --- jednoduchá HTML správa (prispôsob podľa tvojho brandingu)
        var sb = new StringBuilder();
        sb.AppendLine("<h2>Správa – 15-min Analýza &amp; Návrh FVE</h2>");
        sb.AppendLine($"<p><b>Obdobie:</b> {Result.From:d} – {Result.To:d} &nbsp; | &nbsp; <b>Dní:</b> {Result.Days} &nbsp; | &nbsp; <b>Priemer/deň:</b> {Result.AvgPerDayKWh} kWh</p>");
        if (Opt != null)
        {
            sb.AppendLine("<h3>Návrh (max NPV)</h3><ul>");
            sb.AppendLine($"<li><b>FVE:</b> {Opt.KwP} kWp</li>");
            sb.AppendLine($"<li><b>Batéria:</b> {Opt.BatKWh} kWh</li>");
            sb.AppendLine($"<li><b>Autarkia/Vlastná spotreba:</b> {Opt.AutarkyPct:0.0} % / {Opt.SelfConsumptionPct:0.0} %</li>");
            sb.AppendLine($"<li><b>Import/Export:</b> {Opt.ImportKWh:0} / {Opt.ExportKWh:0} kWh</li>");
            sb.AppendLine($"<li><b>Ročná výroba:</b> {Opt.PvKWh:0} kWh</li>");
            sb.AppendLine($"<li><b>NPV:</b> {Opt.NPV:0} € &nbsp; | &nbsp; <b>Payback:</b> {(Opt.PaybackYears is null ? "—" : Opt.PaybackYears.Value.ToString("0.0 r."))}</li>");
            sb.AppendLine($"<li><b>Menič/Export limit:</b> {InverterKW} kW / {(ExportLimitKW > 0 ? ExportLimitKW : InverterKW)} kW</li>");
            sb.AppendLine("</ul>");
        }
        sb.AppendLine("<h3>Top konfigurácie podľa NPV</h3>");
        sb.AppendLine("<table border='1' cellspacing='0' cellpadding='4'><tr><th>kWp</th><th>Bat kWh</th><th>Autarky %</th><th>Self-cons %</th><th>Import</th><th>Export</th><th>PV</th><th>NPV €</th><th>Payback</th></tr>");
        foreach (var r in Top ?? Enumerable.Empty<DesignPoint>())
        {
            sb.AppendLine($"<tr><td>{r.KwP}</td><td>{r.BatKWh}</td><td>{r.AutarkyPct:0.0}</td><td>{r.SelfConsumptionPct:0.0}</td><td>{r.ImportKWh:0}</td><td>{r.ExportKWh:0}</td><td>{r.PvKWh:0}</td><td>{r.NPV:0}</td><td>{(r.PaybackYears is null ? "—" : r.PaybackYears.Value.ToString("0.0"))}</td></tr>");
        }
        sb.AppendLine("</table>");

        var analysisModel = BuildAnalysisPdfModel();
        byte[] pdfBytes = await _pdfExportService2.GenerateAnalysisPdfAsync(analysisModel);
        return File(pdfBytes, "application/pdf", $"Sprava-15m-{DateTime.Now:yyyyMMdd-HHmm}.pdf");
    }

    // =================== Helpers ===================
    private static double SafePct(double num, double den) => den <= 1e-9 ? 0 : num / den * 100.0;

    private static double[] ParseShares(string csv)
    {
        var parts = (csv ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var arr = new double[12];
        for (int i = 0; i < 12 && i < parts.Length; i++)
            arr[i] = double.TryParse(parts[i].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var sum = arr.Sum(); if (sum == 0) return new double[] { 0.02, 0.04, 0.08, 0.11, 0.13, 0.14, 0.14, 0.12, 0.10, 0.07, 0.03, 0.02 };
        return arr;
    }

    // =================== EXPORT PDF ===================

    private AnalysisPdfModel BuildAnalysisPdfModel()
    {
        // Export limit už rozhodni: ak <=0 => InverterKW
        double exportLimitKW = (ExportLimitKW > 0 ? ExportLimitKW : InverterKW);


        var mdl = new AnalysisPdfModel
        {
            // Branding a meta (LogoPath je voliteľný – zadaj cestu ak máš)
            Title = "Správa – 15-min Analýza & Návrh FVE",
            CompanyName = "NestStats",
            CompanyEmail = "info@neststats.sk",
            CompanyPhone = "",
            CompanyWeb = "neststats.sk",
            CreatedAt = DateTime.Now,
            LogoPath = TryGetLogoPath(),

            // Summary
            From = Result!.From,
            To = Result!.To,
            Days = Result!.Days,
            AvgPerDayKWh = Result!.AvgPerDayKWh,
            MissingIntervals = Result!.MissingIntervals,

            // Opt (môže byť null – ošetrené v službe textom)
            KwP = Opt?.KwP,
            BatKWh = Opt?.BatKWh,
            PvKWh = Opt?.PvKWh,
            ImportKWh = Opt?.ImportKWh,
            ExportKWh = Opt?.ExportKWh,
            AutarkyPct = Opt?.AutarkyPct,
            SelfConsumptionPct = Opt?.SelfConsumptionPct,
            NPV = Opt?.NPV,
            PaybackYears = Opt?.PaybackYears,

            // Sim params
            YearlyYield = YearlyYield,
            PriceImport = PriceImport,
            PriceExport = PriceExport,
            CapexPerKwp = CapexPerKwp,
            CapexPerKwh = CapexPerKwh,
            CapexFixed = CapexFixed,
            OandMpercent = OandMpercent,
            DiscountRate = DiscountRate,
            HorizonYears = HorizonYears,
            BatteryMaxKW = BatteryMaxKW,
            EffChargeDischargePct = EffChargeDischargePct,
            InverterKW = InverterKW,
            ExportLimitKW = exportLimitKW,
            MonthlySharesCsv = MonthlySharesCsv
        };

        // TOP
        mdl.Top = (Top ?? new List<DesignPoint>()).Select(r => new AnalysisPdfModel.DesignRow
        {
            KwP = r.KwP,
            BatKWh = r.BatKWh,
            AutarkyPct = r.AutarkyPct,
            SelfConsumptionPct = r.SelfConsumptionPct,
            ImportKWh = r.ImportKWh,
            ExportKWh = r.ExportKWh,
            PvKWh = r.PvKWh,
            NPV = r.NPV,
            PaybackYears = r.PaybackYears
        }).ToList();

        // Grafové dáta – používame tie, ktoré už v handleri vypočítavaš
        // (ak ich máš len v JSON, jednoducho ich znovu vyrátaj ako v Analyze; v OnPostExportPdfAsync už to robíš)
        mdl.AvgDay_Cons = System.Text.Json.JsonSerializer.Deserialize<double[]>(JsonAvgCons ?? "[]") ?? Array.Empty<double>();
        mdl.AvgDay_Pv = System.Text.Json.JsonSerializer.Deserialize<double[]>(JsonAvgPv ?? "[]") ?? Array.Empty<double>();
        mdl.AvgDay_Imp = System.Text.Json.JsonSerializer.Deserialize<double[]>(JsonAvgImport ?? "[]") ?? Array.Empty<double>();

        mdl.Monthly_Cons = System.Text.Json.JsonSerializer.Deserialize<double[]>(JsonMonthlyCons ?? "[]") ?? new double[12];
        mdl.Monthly_Pv = System.Text.Json.JsonSerializer.Deserialize<double[]>(JsonMonthlyPv ?? "[]") ?? new double[12];
        mdl.Monthly_Imp = System.Text.Json.JsonSerializer.Deserialize<double[]>(JsonMonthlyImp ?? "[]") ?? new double[12];
        mdl.Monthly_Exp = System.Text.Json.JsonSerializer.Deserialize<double[]>(JsonMonthlyExp ?? "[]") ?? new double[12];

        mdl.Daily_Labels = System.Text.Json.JsonSerializer.Deserialize<string[]>(JsonDailyLabels ?? "[]") ?? Array.Empty<string>();
        mdl.Daily_Values = System.Text.Json.JsonSerializer.Deserialize<double[]>(JsonDailyConsumption ?? "[]") ?? Array.Empty<double>();

        return mdl;
    }
    private string? TryGetLogoPath()
    {
        var root = _env?.WebRootPath ?? _env?.ContentRootPath;
        if (string.IsNullOrWhiteSpace(root)) return null;
        var p = Path.Combine(root, "logo.png");
        return System.IO.File.Exists(p) ? p : null;
    }

    // =================== Parsing ZSDis (CSV/XLS bez hlavičky) ===================
    private async Task<List<IntervalPoint>> ParseAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        return ext switch
        {
            ".xls" or ".xlsx" => ParseExcel(ms),
            ".csv" or ".txt" => ParseCsv_ZSDis(ms),
            _ => throw new InvalidOperationException("Podporované formáty: .xls, .xlsx, .csv")
        };
    }

    private List<IntervalPoint> ParseExcel(Stream s)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // 1) ZSDis-riadky bez hlavičky: 1=dátum, 2=čas, 3=kW avg/15m
        s.Position = 0;
        var rowsTry = ParseExcel_ZSDisRows(s);
        if (rowsTry.Count > 0) return rowsTry;

        // 2) generický Excel s hlavičkou (kWh)
        s.Position = 0;
        using var reader = ExcelReaderFactory.CreateReader(s);
        var ds = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
        });

        var table = ds.Tables.Cast<DataTable>().OrderByDescending(t => t.Rows.Count).FirstOrDefault();
        if (table == null) return new();

        var cols = table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName.Trim(), c => c, StringComparer.OrdinalIgnoreCase);
        string[] timeCandidates = { "Čas", "Datum", "Dátum", "Date", "Timestamp", "Time", "Interval" };
        string[] eCandidates = { "Spotreba [kWh]", "E (kWh)", "Energy", "kWh", "Spotreba" };

        var timeCol = PickCol(timeCandidates, cols);
        var eCol = PickCol(eCandidates, cols, allowNull: true);

        var list = new List<IntervalPoint>(table.Rows.Count);
        foreach (DataRow row in table.Rows)
        {
            if (!TryParseDateTime(row[timeCol], out var ts)) continue;
            double? e = TryNum(row, eCol);
            if (e is null) continue;
            list.Add(new IntervalPoint(Norm15(ts), e.Value));
        }
        return list;
    }

    private List<IntervalPoint> ParseExcel_ZSDisRows(Stream s)
    {
        var list = new List<IntervalPoint>(4096);
        s.Position = 0;
        using var reader = ExcelReaderFactory.CreateReader(s);

        var dateRe = new Regex(@"^\d{2}\.\d{2}\.\d{4}$");
        var timeRe = new Regex(@"^\d{2}:\d{2}:\d{2}$");

        do
        {
            while (reader.Read())
            {
                string c1 = Val(reader, 1);
                string c2 = Val(reader, 2);
                string c3 = Val(reader, 3);

                if (c1.Equals("Čas vytvorenia", StringComparison.OrdinalIgnoreCase)) continue;
                if (c1.StartsWith("Obdobie", StringComparison.OrdinalIgnoreCase)) continue;

                if (!dateRe.IsMatch(c1) || !timeRe.IsMatch(c2)) continue;
                if (!TryParseSkDateTime(c1, c2, out var ts)) continue;
                if (!TryParseNumFlexible(c3, out var kw)) continue;

                list.Add(new IntervalPoint(Norm15(ts), kw / 4.0)); // kWh
            }
        } while (reader.NextResult());

        return list;

        static string Val(IExcelDataReader r, int idx)
        {
            try { return r.FieldCount > idx ? r.GetValue(idx)?.ToString()?.Trim() ?? "" : ""; }
            catch { return ""; }
        }
    }

    private List<IntervalPoint> ParseCsv_ZSDis(Stream s)
    {
        s.Position = 0;
        var list = new List<IntervalPoint>(4096);
        var cfg = new CsvConfiguration(CultureInfo.GetCultureInfo("sk-SK"))
        {
            HasHeaderRecord = false,
            DetectDelimiter = true,
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim
        };
        var dateRe = new Regex(@"^\d{2}\.\d{2}\.\d{4}$");
        var timeRe = new Regex(@"^\d{2}:\d{2}:\d{2}$");

        using var sr = new StreamReader(s, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(sr, cfg);
        while (csv.Read())
        {
            string c1 = Safe(csv, 1);
            string c2 = Safe(csv, 2);
            string c3 = Safe(csv, 3);

            if (c1.Equals("Čas vytvorenia", StringComparison.OrdinalIgnoreCase)) continue;
            if (c1.StartsWith("Obdobie", StringComparison.OrdinalIgnoreCase)) continue;
            if (!dateRe.IsMatch(c1) || !timeRe.IsMatch(c2)) continue;

            if (!TryParseSkDateTime(c1, c2, out var ts)) continue;
            if (!TryParseNumFlexible(c3, out var kw)) continue;

            list.Add(new IntervalPoint(Norm15(ts), kw / 4.0)); // kWh
        }
        return list;

        static string Safe(CsvReader r, int i) { try { return r.GetField(i)?.Trim() ?? ""; } catch { return ""; } }
    }

    // =================== Consumption KPI ===================
    private AnalysisResult AnalyzeConsumption(List<IntervalPoint> pts)
    {
        var ordered = pts.Where(p => p.Timestamp.Minute % 15 == 0).OrderBy(p => p.Timestamp).ToList();
        if (ordered.Count == 0) throw new InvalidOperationException("Žiadne 15-min intervaly po normalizácii.");

        var from = ordered.First().Timestamp.Date;
        var to = ordered.Last().Timestamp.Date;

        var perDay = ordered.GroupBy(p => p.Timestamp.Date)
            .Select(g => new DaySummary
            {
                Date = g.Key,
                TotalKWh = Round(g.Sum(x => x.EnergyKWh)),
                DaytimeKWh = Round(g.Where(x => InRange(x.Timestamp, 8, 16)).Sum(x => x.EnergyKWh)),
                EveningKWh = Round(g.Where(x => InRange(x.Timestamp, 16, 23)).Sum(x => x.EnergyKWh)),
                NightKWh = Round(g.Where(x => !InRange(x.Timestamp, 8, 23)).Sum(x => x.EnergyKWh)),
                MissingIntervals = Math.Max(0, 96 - g.Count())
            })
            .OrderBy(d => d.Date)
            .ToList();

        return new AnalysisResult
        {
            From = from,
            To = to,
            Days = perDay.Count,
            TotalIntervals = ordered.Count,
            MissingIntervals = perDay.Sum(d => d.MissingIntervals),
            AvgPerDayKWh = Math.Round(perDay.Average(d => d.TotalKWh), 2),
            PerDay = perDay
        };

        static bool InRange(DateTime ts, int h1, int h2) => ts.TimeOfDay >= TimeSpan.FromHours(h1) && ts.TimeOfDay < TimeSpan.FromHours(h2);
        static double Round(double v) => Math.Round(v, 3);
    }

    // =================== PV + Battery Simulator ===================
    private sealed class Simulator
    {
        private readonly List<IntervalPoint> _cons;
        private readonly int _yearlyYield;
        private readonly double[] _shares; // 12

        public Simulator(List<IntervalPoint> consumption, int yearlyYield, double[] monthlyShares)
        {
            _cons = consumption.OrderBy(p => p.Timestamp).ToList();
            _yearlyYield = yearlyYield;
            _shares = monthlyShares;
        }

        public double[] BuildPvSeries(double kWpForScaling)
        {
            var ss = MonthlyWindows;
            var list = new List<double>(_cons.Count);

            var daysInMonth = new int[13];
            for (int m = 1; m <= 12; m++) daysInMonth[m] = DateTime.DaysInMonth(2024, m);
            var dailyTarget = new double[13];
            for (int m = 1; m <= 12; m++)
            {
                var monthEnergy = _yearlyYield * _shares[m - 1];
                dailyTarget[m] = monthEnergy / daysInMonth[m];
            }

            DateTime? curDay = null; double[] dayShapes = Array.Empty<double>(); int shapeIdx = 0; double daySumShape = 0; double dayScale = 0;

            foreach (var p in _cons)
            {
                if (curDay != p.Timestamp.Date)
                {
                    curDay = p.Timestamp.Date;
                    var m = p.Timestamp.Month;
                    var (start, end) = ss[m];
                    dayShapes = BuildDayShape(start, end);
                    daySumShape = dayShapes.Sum();
                    dayScale = daySumShape <= 1e-9 ? 0 : dailyTarget[m] / daySumShape;
                    shapeIdx = 0;
                }
                var shapeVal = shapeIdx < dayShapes.Length ? dayShapes[shapeIdx] : 0;
                var pvPer1Kwp = shapeVal * dayScale;
                list.Add(pvPer1Kwp * kWpForScaling);
                shapeIdx++; if (shapeIdx >= 96) shapeIdx = 0;
            }

            return list.ToArray();
        }

        public BatteryResult RunBattery(double[] pvKwh, double batKWh, double batteryMaxKW, double effPerDirection, double exportLimitKW)
        {
            var det = RunBatteryDetailed(pvKwh, batKWh, batteryMaxKW, effPerDirection, exportLimitKW);
            return new BatteryResult { ImportKWh = det.GridImport.Sum(), ExportKWh = det.GridExport.Sum() };
        }

        // === detailed per-interval flows for charts
        public DetailedFlows RunBatteryDetailed(double[] pvKwh, double batKWh, double batteryMaxKW, double effPerDirection, double exportLimitKW)
        {
            double soc = batKWh * 0.5;
            double cap = batKWh;
            double effC = effPerDirection, effD = effPerDirection;
            double maxStepKWh = batteryMaxKW / 4.0;
            double maxExportStepKWh = Math.Max(0, (exportLimitKW > 0 ? exportLimitKW : batteryMaxKW)) / 4.0;

            int n = pvKwh.Length;
            var gridImp = new double[n];
            var gridExp = new double[n];

            for (int i = 0; i < n; i++)
            {
                double c = _cons[i].EnergyKWh;
                double g = pvKwh[i];
                double net = g - c;

                if (net >= 0)
                {
                    double excess = net;

                    // charge to battery (limit SoC room + power)
                    double room = cap - soc;
                    double canCharge = Math.Min(excess * effC, room);
                    double pwrPossible = Math.Min(maxStepKWh, room);
                    if (canCharge > pwrPossible) canCharge = pwrPossible;
                    double takenFromExcess = canCharge / Math.Max(effC, 1e-9);

                    soc += canCharge;
                    excess -= takenFromExcess;

                    // export limited per step
                    if (excess > 1e-9)
                        gridExp[i] = Math.Min(excess, maxExportStepKWh);
                    // nad limit je curtailment
                }
                else
                {
                    double need = -net;
                    double avail = soc;
                    double maxDischargeToLoad = Math.Min(avail * effD, maxStepKWh * effD);
                    double usedFromBatToLoad = Math.Min(maxDischargeToLoad, need);
                    double takenFromBat = usedFromBatToLoad / Math.Max(effD, 1e-9);

                    soc -= takenFromBat;
                    double rest = need - usedFromBatToLoad;
                    if (rest > 1e-9) gridImp[i] = rest;
                }

                if (soc < 0) soc = 0;
                if (soc > cap) soc = cap;
            }

            return new DetailedFlows { GridImport = gridImp, GridExport = gridExp };
        }

        private static double[] BuildDayShape(double startHour, double endHour)
        {
            var arr = new double[96];
            if (endHour <= startHour) return arr;
            double dur = endHour - startHour;

            for (int i = 0; i < 96; i++)
            {
                double h = i / 4.0;
                if (h < startHour || h >= endHour) { arr[i] = 0; continue; }
                double t = (h - startHour) / dur;
                double val = Math.Pow(Math.Sin(Math.PI * t), 1.6);
                arr[i] = Math.Max(0, val);
            }
            return arr;
        }

        private static readonly (double, double)[] MonthlyWindows = new (double, double)[13] {
            (0,0),
            (8.0,16.0), (7.5,17.5), (6.5,18.5), (6.0,19.5), (5.5,20.5), (5.0,21.0),
            (5.5,20.5), (6.0,19.5), (6.5,18.5), (7.5,17.5), (7.5,16.5), (8.0,16.0)
        };
    }

    private static Economics EvaluateEconomics(
        double kwp, double batkwh, double pvKwh, BatteryResult flow,
        double priceImport, double priceExport,
        double capexPerKwp, double capexPerKwh, double capexFixed,
        double oandmRate, double disc, int years)
    {
        double capex = capexFixed + kwp * capexPerKwp + batkwh * capexPerKwh;
        double saved = flow.ImportKWh * priceImport;
        double earn = flow.ExportKWh * priceExport;
        double annualCash = saved + earn;
        double oandm = capex * oandmRate;

        double npv = -capex;
        double cum = 0;
        double? paybackYear = null;
        for (int y = 1; y <= years; y++)
        {
            double cf = annualCash - oandm;
            cum += cf;
            if (paybackYear is null && cum >= capex) paybackYear = y;
            npv += cf / Math.Pow(1.0 + disc, y);
        }

        return new Economics { NPV = npv, PaybackYears = paybackYear };
    }

    // =================== Utils / Models ===================
    private static bool TryParseSkDateTime(string d, string t, out DateTime ts)
    {
        var s = $"{d} {t}";
        return DateTime.TryParseExact(s, "dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("sk-SK"),
                                      DateTimeStyles.None, out ts)
               || DateTime.TryParse(s, out ts);
    }
    private static bool TryParseNumFlexible(string s, out double val)
    {
        s = (s ?? "").Replace(" ", "").Replace(',', '.');
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out val);
    }
    private static DataColumn PickCol(string[] names, Dictionary<string, DataColumn> dict, bool allowNull = false)
    {
        foreach (var n in names)
            if (dict.TryGetValue(n, out var col)) return col;
        if (allowNull) return null!;
        throw new InvalidOperationException($"Neviem nájsť stĺpec: {string.Join("/", names)}");
    }
    private static bool TryParseDateTime(object v, out DateTime ts)
    {
        if (v is DateTime d) { ts = d; return true; }
        var s = v?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) { ts = default; return false; }
        string[] fmts = {
            "d.M.yyyy H:mm","dd.MM.yyyy H:mm","yyyy-MM-dd H:mm","dd.MM.yyyy HH:mm",
            "M/d/yyyy H:mm","yyyy-MM-ddTHH:mm","H:mm d.M.yyyy","dd.M.yyyy H:mm"
        };
        if (DateTime.TryParseExact(s, fmts, new CultureInfo("sk-SK"), DateTimeStyles.None, out ts)) return true;
        return DateTime.TryParse(s, out ts);
    }
    private static double? TryNum(DataRow row, DataColumn? col)
    {
        if (col == null) return null;
        var o = row[col]; if (o == null || o == DBNull.Value) return null;
        if (o is double dx) return dx;
        if (o is float fx) return fx;
        var str = o.ToString()?.Replace(',', '.');
        return double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
    private static DateTime Norm15(DateTime ts)
    {
        var m = (ts.Minute / 15) * 15;
        return new DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, m, 0, DateTimeKind.Unspecified);
    }

    public sealed record IntervalPoint(DateTime Timestamp, double EnergyKWh);
    public sealed class DaySummary
    {
        public DateTime Date { get; set; }
        public double TotalKWh { get; set; }
        public double DaytimeKWh { get; set; }
        public double EveningKWh { get; set; }
        public double NightKWh { get; set; }
        public int MissingIntervals { get; set; }
    }
    public sealed class AnalysisResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int Days { get; set; }
        public int TotalIntervals { get; set; }
        public int MissingIntervals { get; set; }
        public double AvgPerDayKWh { get; set; }
        public List<DaySummary> PerDay { get; set; } = new();
    }
    public sealed class BatteryResult { public double ImportKWh { get; set; } public double ExportKWh { get; set; } }
    public sealed class DetailedFlows { public required double[] GridImport { get; init; } public required double[] GridExport { get; init; } }
    public sealed class Economics { public double NPV { get; set; } public double? PaybackYears { get; set; } }
    public sealed class DesignPoint
    {
        public double KwP { get; set; }
        public double BatKWh { get; set; }
        public double PvKWh { get; set; }
        public double ImportKWh { get; set; }
        public double ExportKWh { get; set; }
        public double SelfConsumptionPct { get; set; }
        public double AutarkyPct { get; set; }
        public double NPV { get; set; }
        public double? PaybackYears { get; set; }
    }
}
