// File: Services/AnalysisPdfExportService2.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.IO.Font;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace NestStats2.Services
{
    // ==================== MODEL ====================
    public sealed class AnalysisPdfModel
    {
        // Branding / meta
        public string Title { get; set; } = "Správa – 15-min Analýza & Návrh FVE";
        public string CompanyName { get; set; } = "NestStats";
        public string CompanyEmail { get; set; } = "info@neststats.sk";
        public string CompanyPhone { get; set; } = "";
        public string CompanyWeb { get; set; } = "neststats.sk";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? LogoPath { get; set; }

        // Summary
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int Days { get; set; }
        public double AvgPerDayKWh { get; set; }
        public int MissingIntervals { get; set; }

        // Optimal point
        public double? KwP { get; set; }
        public double? BatKWh { get; set; }
        public double? PvKWh { get; set; }
        public double? ImportKWh { get; set; }
        public double? ExportKWh { get; set; }
        public double? AutarkyPct { get; set; }
        public double? SelfConsumptionPct { get; set; }
        public double? NPV { get; set; }
        public double? PaybackYears { get; set; }

        // Simulation params
        public double YearlyYield { get; set; }
        public double PriceImport { get; set; }
        public double PriceExport { get; set; }
        public double CapexPerKwp { get; set; }
        public double CapexPerKwh { get; set; }
        public double CapexFixed { get; set; }
        public double OandMpercent { get; set; }
        public double DiscountRate { get; set; }
        public int HorizonYears { get; set; }
        public double BatteryMaxKW { get; set; }
        public double EffChargeDischargePct { get; set; }
        public double InverterKW { get; set; }
        public double ExportLimitKW { get; set; }
        public string MonthlySharesCsv { get; set; } = "";

        // TOP table
        public List<DesignRow> Top { get; set; } = new();
        public sealed class DesignRow
        {
            public double KwP { get; set; }
            public double BatKWh { get; set; }
            public double AutarkyPct { get; set; }
            public double SelfConsumptionPct { get; set; }
            public double ImportKWh { get; set; }
            public double ExportKWh { get; set; }
            public double PvKWh { get; set; }
            public double NPV { get; set; }
            public double? PaybackYears { get; set; }
        }

        // Charts data
        public double[] AvgDay_Cons { get; set; } = Array.Empty<double>();
        public double[] AvgDay_Pv { get; set; } = Array.Empty<double>();
        public double[] AvgDay_Imp { get; set; } = Array.Empty<double>();

        public double[] Monthly_Cons { get; set; } = new double[12];
        public double[] Monthly_Pv { get; set; } = new double[12];
        public double[] Monthly_Imp { get; set; } = new double[12];
        public double[] Monthly_Exp { get; set; } = new double[12];

        public string[] Daily_Labels { get; set; } = Array.Empty<string>();
        public double[] Daily_Values { get; set; } = Array.Empty<double>();
    }

    // ==================== INTERFACE ====================
    public interface IAnalysisPdfExportService2
    {
        Task<byte[]> GenerateAnalysisPdfAsync(AnalysisPdfModel model);
    }

    // ==================== SERVICE ====================
    public sealed class AnalysisPdfExportService2 : IAnalysisPdfExportService2
    {
        private readonly ILogger<AnalysisPdfExportService2> _logger;
        private readonly IWebHostEnvironment _env;

        private iText.Kernel.Pdf.PdfPage EnsurePage(Document doc)
        {
            var pdf = doc.GetPdfDocument();
            if (pdf.GetNumberOfPages() == 0)
                return pdf.AddNewPage();
            return pdf.GetLastPage();
        }

        public AnalysisPdfExportService2(ILogger<AnalysisPdfExportService2> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
            _env = env;
        }

        // Premium color palette
        private static readonly Color DarkBlue = new DeviceRgb(0, 76, 153);
        private static readonly Color Blue = new DeviceRgb(0, 102, 204);
        private static readonly Color LightBlue = new DeviceRgb(230, 244, 255);
        private static readonly Color Green = new DeviceRgb(25, 160, 82);
        private static readonly Color Red = new DeviceRgb(220, 53, 69);
        private static readonly Color Orange = new DeviceRgb(255, 159, 28);
        private static readonly Color Purple = new DeviceRgb(123, 31, 162);
        private static readonly Color Teal = new DeviceRgb(0, 150, 136);
        private static readonly Color GrayLine = new DeviceRgb(220, 223, 230);
        private static readonly Color Ink = new DeviceRgb(34, 40, 49);
        private static readonly Color LightGray = new DeviceRgb(248, 250, 252);
        private static readonly Color AccentYellow = new DeviceRgb(255, 193, 7);

        public async Task<byte[]> GenerateAnalysisPdfAsync(AnalysisPdfModel m)
        {
            using var ms = new MemoryStream();
            var writer = new PdfWriter(ms);
            var pdf = new PdfDocument(writer);
            var doc = new Document(pdf, PageSize.A4);

            try
            {
                doc.SetMargins(28, 28, 36, 28);
                var (regular, bold) = LoadFonts();

                // PAGE 1: Executive Summary
                AddPremiumHeader(doc, m, bold, regular);
                AddExecutiveSummarySection(doc, m, bold, regular);
                AddKpiDashboard(doc, m, bold, regular);
                AddFinancialSnapshot(doc, m, bold, regular);
                AddPageNumber(doc, regular, 1);

                // PAGE 2: Optimal + Detailná analýza
                doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

                // samostatná sekcia s jasným nadpisom
                AddSectionHeader(doc, "OPTIMÁLNE RIEŠENIE PRE VÁS", bold, regular);
                AddOptimalSolutionHighlight(doc, m, bold, regular);

                // detailná analýza
                AddSectionHeader(doc, "Detailná analýza vašej spotreby", bold, regular);
                Draw_AvgDay_Premium(doc, m, bold, regular, 540, 280);
                Draw_Monthly_Premium(doc, m, bold, regular, 540, 280);
                AddPageNumber(doc, regular, 2);

                // PAGE 3: Energy Balance & Optimization
                doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                AddSectionHeader(doc, "Energetická bilancia a optimalizácia", bold, regular);
                Draw_Daily_Premium(doc, m, bold, regular, 540, 260);
                Draw_Monthly_Balance_Premium(doc, m, bold, regular, 540, 280);
                AddPageNumber(doc, regular, 3);

                // PAGE 4: Advanced Analytics
                doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                AddSectionHeader(doc, "Pokročilá analytika", bold, regular);
                Draw_LoadDuration_Premium(doc, m, bold, regular, 540, 260);
                Draw_Daily_Histogram(doc, m, bold, regular, 540, 220);
                AddPageNumber(doc, regular, 4);

                // PAGE 5: Top Configurations & Technical Details
                doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                Draw_SavingsBreakdown(doc, m, bold, regular, 540, 260);
                AddSectionHeader(doc, "Porovnané konfigurácie (prehľad)", bold, regular);
                Draw_Configs_Scatter(doc, m, bold, regular, 540, 220);   // NOVÉ
                AddTopTablePremium(doc, m, bold, regular);
                AddTechnicalParameters(doc, m, bold, regular);
                AddPageNumber(doc, regular, 5);

                // PAGE 6: Explanation Guide
                doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                AddExplanationGuide(doc, m, bold, regular);
                AddPremiumFooter(doc, m, regular);
                AddPageNumber(doc, regular, 6);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF (Premium Analysis) generation error.");
                throw;
            }
            finally
            {
                doc.Close();
                pdf.Close();
                writer.Close();
            }

            return await Task.FromResult(ms.ToArray());
        }

        // ------------------- Fonts & Assets -------------------
        private (PdfFont regular, PdfFont bold) LoadFonts()
        {
            try
            {
                var (reg, bld) = TryLoadMontserrat();
                if (reg != null && bld != null) return (reg, bld);
            }
            catch { /* fallback */ }

            var regular = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
            var bold = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);
            return (regular, bold);
        }

        private (PdfFont? regular, PdfFont? bold) TryLoadMontserrat()
        {
            var root = _env.WebRootPath ?? _env.ContentRootPath;
            if (string.IsNullOrWhiteSpace(root)) return (null, null);

            var fontsDir = System.IO.Path.Combine(root, "fonts");
            var regPath = System.IO.Path.Combine(fontsDir, "Montserrat-Medium.ttf");
            var bldPath = System.IO.Path.Combine(fontsDir, "Montserrat-ExtraBold.ttf");
            if (!File.Exists(regPath) || !File.Exists(bldPath)) return (null, null);

            var regular = PdfFontFactory.CreateFont(regPath, PdfEncodings.IDENTITY_H);
            var bold = PdfFontFactory.CreateFont(bldPath, PdfEncodings.IDENTITY_H);
            return (regular, bold);
        }

        private string? TryGetLogoPath()
        {
            var root = _env?.WebRootPath ?? _env?.ContentRootPath;
            if (string.IsNullOrWhiteSpace(root)) return null;
            var p = System.IO.Path.Combine(root, "logo.png");
            return System.IO.File.Exists(p) ? p : null;
        }

        // ------------------- Premium Header -------------------
        private void AddPremiumHeader(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular)
        {
            var page = EnsurePage(doc);
            var pageSize = page.GetPageSize();
            var bandH = 64f;

            // farebný pruh cez šírku
            var band = new PdfCanvas(page);
            band.SaveState();
            band.SetFillColor(Blue);
            band.Rectangle(0, pageSize.GetTop() - bandH, pageSize.GetWidth(), bandH);
            band.Fill();
            band.RestoreState();

            // obsah hlavičky – vnútri bieleho kontajnera
            var wrap = new Div()
                .SetBackgroundColor(ColorConstants.WHITE)
                .SetBorder(new SolidBorder(new DeviceRgb(235, 237, 240), 1))
                .SetBorderRadius(new BorderRadius(10))
                .SetPadding(12)
                .SetMarginTop(10)
                .SetMarginBottom(14);

            var grid = new Table(UnitValue.CreatePercentArray(new float[] { 18, 82 })).UseAllAvailableWidth();


            string aut = m.AutarkyPct.HasValue ? $"{Math.Max(0, Math.Min(100, m.AutarkyPct.Value)):0.0} %" : "—";
            string self = m.SelfConsumptionPct.HasValue ? $"{Math.Max(0, Math.Min(100, m.SelfConsumptionPct.Value)):0.0} %" : "—";


            var left = new Cell().SetBorder(Border.NO_BORDER).SetVerticalAlignment(VerticalAlignment.MIDDLE);
            var logo = m.LogoPath ?? TryGetLogoPath();
            if (!string.IsNullOrWhiteSpace(logo) && File.Exists(logo))
            {
                try
                {
                    var img = new Image(ImageDataFactory.Create(logo)).SetAutoScale(true).SetMaxWidth(110);
                    left.Add(img);
                }
                catch { left.Add(PremiumBadgeLogo(bold)); }
            }
            else left.Add(PremiumBadgeLogo(bold));

            var right = new Cell().SetBorder(Border.NO_BORDER).SetVerticalAlignment(VerticalAlignment.MIDDLE);
            right.Add(new Paragraph(m.Title)
                .SetFont(bold).SetFontSize(18).SetFontColor(DarkBlue).SetMarginBottom(4));
            right.Add(new Paragraph($"{m.CompanyName}  |  {m.CompanyPhone}  |  {m.CompanyEmail}")
                .SetFont(regular).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY).SetMarginBottom(2));
            right.Add(new Paragraph($"{m.CompanyWeb}  •  {m.CreatedAt:dd.MM.yyyy HH:mm}")
                .SetFont(regular).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY));

            grid.AddCell(left);
            grid.AddCell(right);
            wrap.Add(grid);

            // prázdny riadok, aby obsah nezačínal nalepený na modrý pruh
            doc.Add(new Paragraph().SetHeight(bandH - 40));
            doc.Add(wrap);
        }


        private Div PremiumBadgeLogo(PdfFont bold)
        {
            return new Div()
                .Add(new Paragraph("NS")
                    .SetFont(bold).SetFontSize(22).SetFontColor(ColorConstants.WHITE)
                    .SetTextAlignment(TextAlignment.CENTER).SetMargin(0))
                .SetBackgroundColor(new DeviceRgb(220, 53, 69))
                .SetPadding(16).SetWidth(100).SetBorderRadius(new BorderRadius(12))
                .SetBorder(new SolidBorder(ColorConstants.WHITE, 3));
        }

        // ------------------- Executive Summary -------------------
        private void AddExecutiveSummarySection(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular)
        {
            var container = new Div()
                .SetBackgroundColor(new DeviceRgb(250, 252, 255))
                .SetBorder(new SolidBorder(Blue, 2))
                .SetBorderRadius(new BorderRadius(14))
                .SetPadding(18)
                .SetMarginBottom(12);

            container.Add(new Paragraph("📊 Exekutívne zhrnutie")
                .SetFont(bold).SetFontSize(16).SetFontColor(DarkBlue).SetMarginBottom(10));

            container.Add(new Paragraph(
                $"Na základe analýzy vašej spotreby za obdobie {m.Days} dní (priemer {m.AvgPerDayKWh:0.0} kWh/deň) " +
                $"sme identifikovali optimálne riešenie fotovoltaickej elektrárne, ktoré zvýši vašu " +
                $"energetickú nezávislosť a mieru vlastnej spotreby.")
                .SetFont(regular).SetFontSize(10).SetFontColor(Ink).SetTextAlignment(TextAlignment.JUSTIFIED));

            doc.Add(container);
        }

        // ------------------- KPI Dashboard -------------------
        private void AddKpiDashboard(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular)
        {
            doc.Add(new Paragraph("Kľúčové ukazovatele")
                .SetFont(bold).SetFontSize(14).SetFontColor(DarkBlue).SetMarginBottom(8).SetMarginTop(6));

            var grid = new Table(UnitValue.CreatePercentArray(new float[] { 25, 25, 25, 25 })).UseAllAvailableWidth();

            string aut = m.AutarkyPct.HasValue ? $"{Math.Max(0, Math.Min(100, m.AutarkyPct.Value)):0.0} %" : "—";
            string self = m.SelfConsumptionPct.HasValue ? $"{Math.Max(0, Math.Min(100, m.SelfConsumptionPct.Value)):0.0} %" : "—";


            grid.AddCell(PremiumKpiCard("", "Analyzované obdobie",
    $"{m.From:dd.MM.yyyy} – {m.To:dd.MM.yyyy}", DarkBlue, regular, bold));
            grid.AddCell(PremiumKpiCard("", "Počet dní", $"{m.Days}", DarkBlue, regular, bold));
            grid.AddCell(PremiumKpiCard("", "Priemerná spotreba", $"{m.AvgPerDayKWh:0.0} kWh/deň", Blue, regular, bold));
            // 2. riadok
            grid.AddCell(PremiumKpiCard("", "Chýbajúce dáta", $"{m.MissingIntervals} intervalov",
                m.MissingIntervals > 100 ? Orange : Green, regular, bold));
            grid.AddCell(PremiumKpiCard("", "Miera nezávislosti", aut, Purple, regular, bold));
            grid.AddCell(PremiumKpiCard("", "Vlastná spotreba", self, Teal, regular, bold));

            doc.Add(grid);
            doc.Add(new Paragraph().SetHeight(12));
        }

        private Cell PremiumKpiCard(string icon, string title, string value, Color accent, PdfFont regular, PdfFont bold)
        {
            var d = new Div()
                .SetBackgroundColor(ColorConstants.WHITE)
                .SetBorder(new SolidBorder(accent, 2))
                .SetBorderRadius(new BorderRadius(12))
                .SetPadding(14);

            d.Add(new Paragraph(icon + " " + title)
                .SetFont(bold).SetFontSize(9).SetFontColor(accent).SetMarginBottom(6));
            d.Add(new Paragraph(value)
                .SetFont(bold).SetFontSize(14).SetFontColor(Ink).SetMargin(0));

            return new Cell().Add(d).SetBorder(Border.NO_BORDER).SetPadding(3);
        }

        // ------------------- Optimal Solution Highlight -------------------
        private void AddOptimalSolutionHighlight(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular)
        {
            var container = new Div()
                .SetBackgroundColor(new DeviceRgb(255, 248, 225))
                .SetBorder(new SolidBorder(AccentYellow, 3))
                .SetBorderRadius(new BorderRadius(14))
                .SetPadding(18);

            container.Add(new Paragraph("⭐ OPTIMÁLNE RIEŠENIE PRE VÁS")
                .SetFont(bold).SetFontSize(15).SetFontColor(new DeviceRgb(230, 126, 34))
                .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(10));

            var grid = new Table(UnitValue.CreatePercentArray(new float[] { 50, 50 })).UseAllAvailableWidth();

            var leftCol = new Cell().SetBorder(Border.NO_BORDER);
            leftCol.Add(new Paragraph(" Fotovoltaická elektráreň")
                .SetFont(bold).SetFontSize(11).SetFontColor(DarkBlue).SetMarginBottom(4));
            leftCol.Add(new Paragraph($"Výkon: {m.KwP:0.0} kWp")
                .SetFont(regular).SetFontSize(10).SetMarginLeft(10));
            leftCol.Add(new Paragraph($"Ročná výroba: {m.PvKWh:0} kWh")
                .SetFont(regular).SetFontSize(10).SetMarginLeft(10).SetMarginBottom(8));

            leftCol.Add(new Paragraph(" Batériový systém")
                .SetFont(bold).SetFontSize(11).SetFontColor(DarkBlue).SetMarginBottom(4));
            leftCol.Add(new Paragraph($"Kapacita: {m.BatKWh:0.0} kWh")
                .SetFont(regular).SetFontSize(10).SetMarginLeft(10));

            var rightCol = new Cell().SetBorder(Border.NO_BORDER);
            rightCol.Add(new Paragraph(" Energetická bilancia")
                .SetFont(bold).SetFontSize(11).SetFontColor(DarkBlue).SetMarginBottom(4));
            rightCol.Add(new Paragraph($"Import zo siete: {m.ImportKWh:0} kWh/rok")
                .SetFont(regular).SetFontSize(10).SetMarginLeft(10));
            rightCol.Add(new Paragraph($"Export do siete: {m.ExportKWh:0} kWh/rok")
                .SetFont(regular).SetFontSize(10).SetMarginLeft(10).SetMarginBottom(8));

            grid.AddCell(leftCol);
            grid.AddCell(rightCol);

            container.Add(grid);
            doc.Add(container);
            doc.Add(new Paragraph().SetHeight(12));
        }

        // ------------------- Financial Snapshot -------------------
        private void AddFinancialSnapshot(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular)
        {

            var totalCapex = m.KwP.GetValueOrDefault() * m.CapexPerKwp +
                            m.BatKWh.GetValueOrDefault() * m.CapexPerKwh +
                            m.CapexFixed;

            var yearlyProduction = m.PvKWh.GetValueOrDefault();
            var yearlySavings = m.ImportKWh.GetValueOrDefault() * m.PriceImport -
                               m.ExportKWh.GetValueOrDefault() * m.PriceExport;

            var grid = new Table(UnitValue.CreatePercentArray(new float[] { 33, 33, 34 })).UseAllAvailableWidth();

            grid.AddCell(FinancialCard(" Ročné odhadované úspory", $"{yearlySavings:0} €",
                "Zníženie nákladov na elektrinu", Green, regular, bold));
            grid.AddCell(FinancialCard(" Ročná výroba", $"{yearlyProduction:0} kWh",
                "Čistá energia zo slnka", Blue, regular, bold));

            doc.Add(grid);
        }

        private Cell FinancialCard(string title, string value, string desc, Color accent, PdfFont regular, PdfFont bold)
        {
            var d = new Div()
                .SetBackgroundColor(LightGray)
                .SetBorder(new SolidBorder(accent, 1.5f))
                .SetBorderRadius(new BorderRadius(10))
                .SetPadding(12);

            d.Add(new Paragraph(title)
                .SetFont(bold).SetFontSize(10).SetFontColor(accent).SetMarginBottom(5));
            d.Add(new Paragraph(value)
                .SetFont(bold).SetFontSize(16).SetFontColor(Ink).SetMarginBottom(4));
            d.Add(new Paragraph(desc)
                .SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY).SetItalic());

            return new Cell().Add(d).SetBorder(Border.NO_BORDER).SetPadding(3);
        }

        // ------------------- Section Headers -------------------
        private void AddSectionHeader(Document doc, string title, PdfFont bold, PdfFont regular)
        {
            var page = EnsurePage(doc);
            var pageSize = page.GetPageSize();
            var canvas = new PdfCanvas(page);
            canvas.SaveState();
            canvas.SetFillColor(new DeviceRgb(235, 238, 243));
            canvas.Rectangle(0, pageSize.GetTop() - 24, pageSize.GetWidth(), 3);
            canvas.Fill();
            canvas.RestoreState();

            doc.Add(new Paragraph(title)
                .SetFont(bold).SetFontSize(16).SetFontColor(DarkBlue)
                .SetMarginTop(6).SetMarginBottom(10));
        }

        // ------------------- Premium Charts -------------------
        private sealed record ChartCtx(Rectangle Plot, PdfCanvas Pdf, iText.Layout.Canvas Text, float PlotW, float PlotH);

        private ChartCtx BeginPremiumChart(
            Document doc, string title, string subtitle, PdfFont bold, PdfFont regular,
            float width, float height, Color accent)
        {
            // hlavička grafu
            doc.Add(new Paragraph(title)
                .SetFont(bold).SetFontSize(12).SetFontColor(DarkBlue)
                .SetMarginTop(8).SetMarginBottom(2));

            if (!string.IsNullOrWhiteSpace(subtitle))
                doc.Add(new Paragraph(subtitle)
                    .SetFont(regular).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                    .SetItalic().SetMarginBottom(6));

            var page = EnsurePage(doc);
            var pageSize = page.GetPageSize();
            var contentLeft = doc.GetLeftMargin();
            var contentRight = pageSize.GetWidth() - doc.GetRightMargin();
            var maxW = contentRight - contentLeft;

            float w = Math.Min(width, maxW);
            float h = height;

            // nakreslíme obalový „card“
            var bbox = doc.GetRenderer().GetCurrentArea().GetBBox();
            float left = contentLeft;
            float top = bbox.GetTop();

            var pdf = new PdfCanvas(page);
            pdf.SaveState();
            pdf.SetFillColor(ColorConstants.WHITE);
            pdf.SetStrokeColor(accent).SetLineWidth(1.6f);
            pdf.RoundRectangle(left, top - h, w, h, 10).FillStroke();
            pdf.RestoreState();

            // vnútorné okraje card
            const float pad = 14f;
            const float axisPadLeft = 34f; // miesto na Y osi text
            const float axisPadBottom = 24f;

            float innerLeft = left + pad + axisPadLeft;
            float innerBottom = top - h + pad + axisPadBottom;
            float innerW = w - pad * 2 - axisPadLeft - 6;   // malý buffer vpravo
            float innerH = h - pad * 2 - axisPadBottom - 10;

            // rezervuj výšku card v layoute
            doc.Add(new Paragraph().SetHeight(h + 6));

            var textCanvas = new iText.Layout.Canvas(pdf, pageSize);
            return new ChartCtx(new Rectangle(innerLeft, innerBottom, innerW, innerH), pdf, textCanvas, innerW, innerH);
        }

        private void DrawPremiumGrid(ChartCtx ctx, int yTicks = 5)
        {
            ctx.Pdf.SaveState();
            var grid = new DeviceRgb(235, 237, 240);
            ctx.Pdf.SetStrokeColor(grid).SetLineWidth(0.5f);

            // horizontálne linky
            for (int i = 0; i <= yTicks; i++)
            {
                float y = ctx.Plot.GetBottom() + (ctx.PlotH / yTicks) * i;
                ctx.Pdf.MoveTo(ctx.Plot.GetLeft(), y).LineTo(ctx.Plot.GetRight(), y).Stroke();
            }

            // rámik
            ctx.Pdf.SetStrokeColor(GrayLine).SetLineWidth(1f);
            ctx.Pdf.Rectangle(ctx.Plot).Stroke();
            ctx.Pdf.RestoreState();
        }

        // ------------------- Chart 1: Premium Average Day -------------------
        private void Draw_AvgDay_Premium(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular, float w, float h)
        {
            var cons = m.AvgDay_Cons; var pv = m.AvgDay_Pv; var imp = m.AvgDay_Imp;
            int n = new[] { cons?.Length ?? 0, pv?.Length ?? 0, imp?.Length ?? 0 }.Min();
            if (n < 2)
            {
                // kartička s informáciou namiesto prázdnej strany
                var info = new Div()
                    .SetBackgroundColor(LightGray)
                    .SetBorder(new SolidBorder(Blue, 1))
                    .SetBorderRadius(new BorderRadius(8))
                    .SetPadding(10)
                    .SetMarginBottom(8);

                info.Add(new Paragraph("Dáta pre priemerný denný profil nie sú dostupné")
                    .SetFont(bold).SetFontSize(11).SetFontColor(DarkBlue).SetMarginBottom(4));
                info.Add(new Paragraph("Skontrolujte, či boli 15-min údaje správne načítané a či pokrývajú aspoň niekoľko dní.")
                    .SetFont(regular).SetFontSize(9).SetFontColor(Ink));

                doc.Add(info);
                return;
            }

            var ctx = BeginPremiumChart(doc, "📈 Priemerný denný profil spotreby a výroby",
                "Tento graf zobrazuje vašu priemernú spotrebu energie počas dňa v 15-minútových intervaloch. " +
                "Modrá krivka reprezentuje celkovú spotrebu, svetlomodrá výrobu z FVE a červená import zo siete.",
                bold, regular, w, h, Blue);

            DrawPremiumGrid(ctx, 5);

            double maxY = Math.Max(0.001, new[] { cons.Take(n).Max(), pv.Take(n).Max(), imp.Take(n).Max() }.Max() * 1.15);

            // X-axis (hours)
            for (int h2 = 0; h2 <= 24; h2 += 3)
            {
                float tx = (float)(ctx.Plot.GetLeft() + (h2 / 24f) * ctx.PlotW);
                if (h2 < 24)
                {
                    ctx.Pdf.SetStrokeColor(new DeviceRgb(240, 242, 245)).SetLineWidth(0.3f)
                        .MoveTo(tx, ctx.Plot.GetBottom()).LineTo(tx, ctx.Plot.GetTop()).Stroke();
                }
                ctx.Text.ShowTextAligned(new Paragraph($"{h2}h").SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY),
                    tx, ctx.Plot.GetBottom() - 14, TextAlignment.CENTER);
            }

            // Y-axis
            for (int i = 0; i <= 5; i++)
            {
                float ty = ctx.Plot.GetBottom() + i * (ctx.PlotH / 5f);
                double val = (maxY / 5.0) * i;
                ctx.Text.ShowTextAligned(new Paragraph(val.ToString("0.00")).SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY),
                    ctx.Plot.GetLeft() - 28, ty - 3, TextAlignment.RIGHT);
            }

            // Y-axis label
            ctx.Text.ShowTextAligned(new Paragraph("kWh/15min").SetFont(bold).SetFontSize(9).SetFontColor(DarkBlue),
                ctx.Plot.GetLeft() - 28, ctx.Plot.GetTop() + 8, TextAlignment.RIGHT);

            void DrawSeriesWithGradient(double[] series, Color topColor, Color bottomColor, bool fill = false)
            {
                ctx.Pdf.SaveState();
                ctx.Pdf.SetLineWidth(2.5f).SetStrokeColor(topColor);

                for (int i = 0; i < n; i++)
                {
                    float px = (float)(ctx.Plot.GetLeft() + (i / (double)(n - 1)) * ctx.PlotW);
                    float py = (float)(ctx.Plot.GetBottom() + (series[i] / maxY) * ctx.PlotH);
                    if (i == 0) ctx.Pdf.MoveTo(px, py); else ctx.Pdf.LineTo(px, py);
                }
                ctx.Pdf.Stroke();

                if (fill)
                {
                    ctx.Pdf.SetFillColor(bottomColor);
                    ctx.Pdf.SetStrokeColor(bottomColor);
                    for (int i = 0; i < n; i++)
                    {
                        float px = (float)(ctx.Plot.GetLeft() + (i / (double)(n - 1)) * ctx.PlotW);
                        float py = (float)(ctx.Plot.GetBottom() + (series[i] / maxY) * ctx.PlotH);
                        if (i == 0) ctx.Pdf.MoveTo(px, ctx.Plot.GetBottom());
                        if (i == 0) ctx.Pdf.LineTo(px, py);
                        else ctx.Pdf.LineTo(px, py);
                    }
                    ctx.Pdf.LineTo((float)(ctx.Plot.GetLeft() + ctx.PlotW), ctx.Plot.GetBottom());
                    ctx.Pdf.LineTo(ctx.Plot.GetLeft(), ctx.Plot.GetBottom());
                    ctx.Pdf.FillStroke();
                }
                ctx.Pdf.RestoreState();
            }

            // Draw filled area for PV
            DrawSeriesWithGradient(pv, new DeviceRgb(100, 181, 246), new DeviceRgb(200, 230, 255), true);
            // Draw lines
            DrawSeriesWithGradient(cons, DarkBlue, DarkBlue);
            DrawSeriesWithGradient(imp, Red, Red);

            // Enhanced legend with boxes
            float lx = ctx.Plot.GetLeft() + 10;
            float ly = ctx.Plot.GetTop() + 16;
            PremiumLegend(ctx, regular, lx, ly, DarkBlue, "Celková spotreba");
            PremiumLegend(ctx, regular, lx + 130, ly, new DeviceRgb(100, 181, 246), "Výroba FVE");
            PremiumLegend(ctx, regular, lx + 250, ly, Red, "Import zo siete");
        }

        private void PremiumLegend(ChartCtx ctx, PdfFont regular, float x, float y, Color color, string text)
        {
            ctx.Pdf.SaveState();
            ctx.Pdf.SetFillColor(color);
            ctx.Pdf.RoundRectangle(x, y - 2, 12, 6, 2).Fill();
            ctx.Pdf.RestoreState();

            ctx.Text.ShowTextAligned(
                new Paragraph(text).SetFont(regular).SetFontSize(9).SetFontColor(Ink),
                x + 18, y - 0.5f, TextAlignment.LEFT);
        }

        // ------------------- Chart 2: Premium Monthly -------------------
        private void Draw_Monthly_Premium(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular, float w, float h)
        {
            var cons = m.Monthly_Cons; var pv = m.Monthly_Pv; var imp = m.Monthly_Imp; var exp = m.Monthly_Exp;
            if (cons.Length < 12 || pv.Length < 12) return;

            var ctx = BeginPremiumChart(doc, "📊 Mesačný prehľad energetickej bilancie",
                "Porovnanie mesačnej spotreby, výroby z FVE, importu zo siete a exportu prebytkov. " +
                "Tento graf vám pomôže pochopiť sezónne zmeny vo výrobe a spotrebe.",
                bold, regular, w, h, Purple);

            DrawPremiumGrid(ctx, 5);

            double maxY = new[] { cons.Max(), pv.Max(), imp.Max(), exp.Max(), 0.001 }.Max() * 1.20;
            string[] mLabels = { "Jan", "Feb", "Mar", "Apr", "Máj", "Jún", "Júl", "Aug", "Sep", "Okt", "Nov", "Dec" };

            int N = 12;
            float monthW = ctx.PlotW / N;
            float barW = monthW * 0.16f;
            float gap = barW * 0.25f;

            void PremiumBar(int mIndex, int pos, double val, Color color)
            {
                float bx = (float)(ctx.Plot.GetLeft() + mIndex * monthW + pos * (barW + gap) +
                          (monthW - (4 * barW + 3 * gap)) / 2f);
                float by = ctx.Plot.GetBottom();
                float bh = (float)((val / maxY) * ctx.PlotH);

                // Shadow
                ctx.Pdf.SaveState();
                ctx.Pdf.SetFillColor(new DeviceRgb(230, 230, 230));
                ctx.Pdf.Rectangle(bx + 1, by, barW, bh).Fill();
                ctx.Pdf.RestoreState();

                // Bar with gradient effect
                ctx.Pdf.SetFillColor(color);
                ctx.Pdf.RoundRectangle(bx, by, barW, bh, 2).Fill();
            }

            for (int i = 0; i < N; i++)
            {
                PremiumBar(i, 0, cons[i], DarkBlue);
                PremiumBar(i, 1, pv[i], new DeviceRgb(0, 150, 136));
                PremiumBar(i, 2, imp[i], new DeviceRgb(255, 159, 28));
                PremiumBar(i, 3, exp[i], Red);

                ctx.Text.ShowTextAligned(new Paragraph(mLabels[i]).SetFont(regular).SetFontSize(8).SetFontColor(Ink),
                    (float)(ctx.Plot.GetLeft() + i * monthW + monthW / 2), ctx.Plot.GetBottom() - 14, TextAlignment.CENTER);
            }

            // Y-axis
            for (int i = 0; i <= 5; i++)
            {
                float ty = ctx.Plot.GetBottom() + i * (ctx.PlotH / 5f);
                double val = (maxY / 5.0) * i;
                ctx.Text.ShowTextAligned(new Paragraph(val.ToString("0")).SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY),
                    ctx.Plot.GetLeft() - 22, ty - 3, TextAlignment.RIGHT);
            }

            ctx.Text.ShowTextAligned(new Paragraph("kWh").SetFont(bold).SetFontSize(9).SetFontColor(DarkBlue),
                ctx.Plot.GetLeft() - 22, ctx.Plot.GetTop() + 8, TextAlignment.RIGHT);

            float lx = ctx.Plot.GetLeft() + 10;
            float ly = ctx.Plot.GetTop() + 16;
            PremiumLegend(ctx, regular, lx, ly, DarkBlue, "Spotreba");
            PremiumLegend(ctx, regular, lx + 90, ly, new DeviceRgb(0, 150, 136), "Výroba FVE");
            PremiumLegend(ctx, regular, lx + 180, ly, Orange, "Import");
            PremiumLegend(ctx, regular, lx + 250, ly, Red, "Export");
        }

        // ------------------- Chart 3: Premium Daily Bars -------------------
        private void Draw_Daily_Premium(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular, float w, float h)
        {
            var vals = m.Daily_Values ?? Array.Empty<double>();
            var labels = m.Daily_Labels ?? Array.Empty<string>();
            int n = Math.Min(vals.Length, labels.Length);
            if (n == 0) return;

            var ctx = BeginPremiumChart(doc, "Denné súčty spotreby",
                "Tento graf zobrazuje vašu celkovú dennú spotrebu. Pomáha identifikovať dni s najvyššou spotrebou " +
                "a potenciál pre úspory energie. Nižšie je graf - kWh/ deň.",
                bold, regular, w, h, Teal);

            DrawPremiumGrid(ctx, 5);

            double maxY = Math.Max(0.001, vals.Max()) * 1.20;
            double avgY = vals.Average();

            float step = ctx.PlotW / n;
            float barW = Math.Max(1.2f, step * 0.65f);

            for (int i = 0; i < n; i++)
            {
                float bx = ctx.Plot.GetLeft() + i * step + (step - barW) / 2;
                float bh = (float)((vals[i] / maxY) * ctx.PlotH);

                // Color gradient based on value
                Color barColor = vals[i] > avgY * 1.2 ? Red :
                                vals[i] < avgY * 0.8 ? Green : DarkBlue;

                ctx.Pdf.SetFillColor(barColor);
                ctx.Pdf.RoundRectangle(bx, ctx.Plot.GetBottom(), barW, bh, 1).Fill();
            }

            // Average line
            float avgLine = (float)(ctx.Plot.GetBottom() + (avgY / maxY) * ctx.PlotH);
            ctx.Pdf.SaveState();
            ctx.Pdf.SetStrokeColor(Orange).SetLineWidth(1.5f);
            ctx.Pdf.SetLineDash(3, 3);
            ctx.Pdf.MoveTo(ctx.Plot.GetLeft(), avgLine).LineTo(ctx.Plot.GetRight(), avgLine).Stroke();
            ctx.Pdf.RestoreState();

            ctx.Text.ShowTextAligned(new Paragraph($"Priemer: {avgY:0.0} kWh").SetFont(regular).SetFontSize(8)
                .SetFontColor(Orange).SetBackgroundColor(ColorConstants.WHITE).SetPadding(2),
                ctx.Plot.GetRight() - 5, avgLine + 2, TextAlignment.RIGHT);

            // Sparse X labels
            int tickCount = Math.Min(15, n);
            int stride = Math.Max(1, n / tickCount);
            for (int i = 0; i < n; i += stride)
            {
                float tx = ctx.Plot.GetLeft() + i * step + step / 2;
                ctx.Text.ShowTextAligned(new Paragraph(labels[i]).SetFont(regular).SetFontSize(7).SetFontColor(Ink),
                    tx, ctx.Plot.GetBottom() - 14, TextAlignment.CENTER);
            }

            // Y-axis
            for (int i = 0; i <= 5; i++)
            {
                float ty = ctx.Plot.GetBottom() + i * (ctx.PlotH / 5f);
                double val = (maxY / 5.0) * i;
                ctx.Text.ShowTextAligned(new Paragraph(val.ToString("0")).SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY),
                    ctx.Plot.GetLeft() - 22, ty - 3, TextAlignment.RIGHT);
            }

        }

        // ------------------- Chart 4: Monthly Balance Stacked -------------------
        private void Draw_Monthly_Balance_Premium(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular, float w, float h)
        {
            var cons = m.Monthly_Cons; var pv = m.Monthly_Pv; var imp = m.Monthly_Imp; var exp = m.Monthly_Exp;
            if (cons.Length < 12 || pv.Length < 12) return;

            var selfPv = new double[12];
            for (int i = 0; i < 12; i++) selfPv[i] = Math.Max(0, pv[i] - exp[i]);

            var ctx = BeginPremiumChart(doc, "🔋 Mesačná bilancia – Pôvod energie",
                "Tento stĺpcový graf ukazuje, koľko energie pochádzalo z vlastnej výroby FVE (zelená) " +
                "a koľko ste museli nakúpiť zo siete (oranžová). Cieľom je maximalizovať zelenú časť.",
                bold, regular, w, h, Green);

            DrawPremiumGrid(ctx, 5);
            string[] mLabels = { "Jan", "Feb", "Mar", "Apr", "Máj", "Jún", "Júl", "Aug", "Sep", "Okt", "Nov", "Dec" };

            var stackMax = new double[12];
            for (int i = 0; i < 12; i++) stackMax[i] = selfPv[i] + imp[i];
            double maxY = Math.Max(0.001, stackMax.Max()) * 1.15;

            int N = 12;
            float monthW = ctx.PlotW / N;
            float barW = monthW * 0.55f;

            for (int i = 0; i < N; i++)
            {
                float bx = ctx.Plot.GetLeft() + i * monthW + (monthW - barW) / 2;

                float hSelf = (float)((selfPv[i] / maxY) * ctx.PlotH);
                float hImp = (float)((imp[i] / maxY) * ctx.PlotH);

                // Self consumption (green with gradient)
                ctx.Pdf.SaveState();
                ctx.Pdf.SetFillColor(new DeviceRgb(46, 204, 113));
                ctx.Pdf.RoundRectangle(bx, ctx.Plot.GetBottom(), barW, hSelf, 3).Fill();
                ctx.Pdf.RestoreState();

                // Import (orange)
                ctx.Pdf.SetFillColor(new DeviceRgb(255, 159, 28));
                ctx.Pdf.RoundRectangle(bx, ctx.Plot.GetBottom() + hSelf, barW, hImp, 3).Fill();

                // Percentage label
                double pct = stackMax[i] > 0 ? (selfPv[i] / stackMax[i]) * 100 : 0;
                if (hSelf > 15)
                {
                    ctx.Text.ShowTextAligned(new Paragraph($"{pct:0}%").SetFont(bold).SetFontSize(7)
                        .SetFontColor(ColorConstants.WHITE),
                        bx + barW / 2, ctx.Plot.GetBottom() + hSelf / 2, TextAlignment.CENTER);
                }

                ctx.Text.ShowTextAligned(new Paragraph(mLabels[i]).SetFont(regular).SetFontSize(8).SetFontColor(Ink),
                    bx + barW / 2, ctx.Plot.GetBottom() - 14, TextAlignment.CENTER);
            }

            // Y-axis
            for (int i = 0; i <= 5; i++)
            {
                float ty = ctx.Plot.GetBottom() + i * (ctx.PlotH / 5f);
                double val = (maxY / 5.0) * i;
                ctx.Text.ShowTextAligned(new Paragraph(val.ToString("0")).SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY),
                    ctx.Plot.GetLeft() - 22, ty - 3, TextAlignment.RIGHT);
            }

            ctx.Text.ShowTextAligned(new Paragraph("kWh").SetFont(bold).SetFontSize(9).SetFontColor(DarkBlue),
                ctx.Plot.GetLeft() - 22, ctx.Plot.GetTop() + 8, TextAlignment.RIGHT);

            float lx = ctx.Plot.GetLeft() + 10;
            float ly = ctx.Plot.GetTop() + 16;
            PremiumLegend(ctx, regular, lx, ly, new DeviceRgb(46, 204, 113), "Vlastná výroba FVE");
            PremiumLegend(ctx, regular, lx + 150, ly, Orange, "Import zo siete");
        }

        // ------------------- Chart 5: Load Duration Curve -------------------
        private void Draw_LoadDuration_Premium(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular, float w, float h)
        {
            var vals = m.Daily_Values ?? Array.Empty<double>();
            if (vals.Length == 0) return;

            var sorted = vals.OrderByDescending(x => x).ToArray();
            int n = sorted.Length;

            var ctx = BeginPremiumChart(doc, " Krivka trvania zaťaženia",
                "Táto krivka zoraďuje všetky dni podľa spotreby od najvyššej po najnižšiu. " +
                "Pomáha určiť základné zaťaženie a špeciálne požiadavky na spotrebu. - Nižšie je graf - kWh/ deň",
                bold, regular, w, h, Purple);

            DrawPremiumGrid(ctx, 5);

            double maxY = Math.Max(0.001, sorted.Max()) * 1.10;

            // Fill area under curve
            ctx.Pdf.SaveState();
            ctx.Pdf.SetFillColor(new DeviceRgb(220, 210, 240));
            ctx.Pdf.MoveTo(ctx.Plot.GetLeft(), ctx.Plot.GetBottom());
            for (int i = 0; i < n; i++)
            {
                float px = (float)(ctx.Plot.GetLeft() + (i / (double)(n - 1)) * ctx.PlotW);
                float py = (float)(ctx.Plot.GetBottom() + (sorted[i] / maxY) * ctx.PlotH);
                ctx.Pdf.LineTo(px, py);
            }
            ctx.Pdf.LineTo(ctx.Plot.GetRight(), ctx.Plot.GetBottom());
            ctx.Pdf.LineTo(ctx.Plot.GetLeft(), ctx.Plot.GetBottom());
            ctx.Pdf.Fill();
            ctx.Pdf.RestoreState();

            // Line
            ctx.Pdf.SetStrokeColor(Purple).SetLineWidth(2.5f);
            for (int i = 0; i < n; i++)
            {
                float px = (float)(ctx.Plot.GetLeft() + (i / (double)(n - 1)) * ctx.PlotW);
                float py = (float)(ctx.Plot.GetBottom() + (sorted[i] / maxY) * ctx.PlotH);
                if (i == 0) ctx.Pdf.MoveTo(px, py); else ctx.Pdf.LineTo(px, py);
            }
            ctx.Pdf.Stroke();

            // Percentile markers
            int[] percentiles = { 10, 25, 50, 75, 90 };
            foreach (var pct in percentiles)
            {
                int idx = (int)((pct / 100.0) * (n - 1));
                float px = (float)(ctx.Plot.GetLeft() + (idx / (double)(n - 1)) * ctx.PlotW);
                float py = (float)(ctx.Plot.GetBottom() + (sorted[idx] / maxY) * ctx.PlotH);

                ctx.Pdf.SaveState();
                ctx.Pdf.SetFillColor(Red);
                ctx.Pdf.Circle(px, py, 3).Fill();
                ctx.Pdf.RestoreState();
            }

            // X-axis
            int steps = 5;
            for (int i = 0; i <= steps; i++)
            {
                double p = i / (double)steps;
                float tx = (float)(ctx.Plot.GetLeft() + p * ctx.PlotW);
                ctx.Text.ShowTextAligned(new Paragraph($"{(int)(p * 100)}%").SetFont(regular).SetFontSize(8).SetFontColor(Ink),
                    tx, ctx.Plot.GetBottom() - 14, TextAlignment.CENTER);
            }

            ctx.Text.ShowTextAligned(new Paragraph("Percentil dní").SetFont(bold).SetFontSize(9).SetFontColor(DarkBlue),
                ctx.Plot.GetLeft() + ctx.PlotW / 2, ctx.Plot.GetBottom() - 26, TextAlignment.CENTER);

            // Y-axis
            for (int i = 0; i <= 5; i++)
            {
                float ty = ctx.Plot.GetBottom() + i * (ctx.PlotH / 5f);
                double val = (maxY / 5.0) * i;
                ctx.Text.ShowTextAligned(new Paragraph(val.ToString("0")).SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY),
                    ctx.Plot.GetLeft() - 22, ty - 3, TextAlignment.RIGHT);
            }

        }

        // ------------------- Chart 6: Savings Breakdown -------------------
        private void Draw_SavingsBreakdown(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular, float w, float h)
        {
            var ctx = BeginPremiumChart(doc, "Rozpad úspor – Odkiaľ pochádzajú vaše úspory",
                "Vizualizácia zdrojov finančných úspor: vlastná spotreba z FVE, predaj prebytkov a zníženie importu.",
                bold, regular, w, h, Green);

            var selfConsValue = (m.PvKWh.GetValueOrDefault() - m.ExportKWh.GetValueOrDefault()) * m.PriceImport;
            var exportValue = m.ExportKWh.GetValueOrDefault() * m.PriceExport;
            var totalSavings = selfConsValue + exportValue;

            if (totalSavings <= 0) return;

            DrawPremiumGrid(ctx, 5);

            double[] values = { selfConsValue, exportValue };
            string[] labels = { "Vlastná spotreba FVE", "Predaj prebytkov" };
            Color[] colors = { new DeviceRgb(46, 204, 113), new DeviceRgb(52, 152, 219) };

            double maxY = values.Max() * 1.20;

            float barW = ctx.PlotW * 0.25f;
            float spacing = ctx.PlotW * 0.15f;

            for (int i = 0; i < values.Length; i++)
            {
                float bx = ctx.Plot.GetLeft() + spacing + i * (barW + spacing * 2);
                float bh = (float)((values[i] / maxY) * ctx.PlotH);

                // 3D effect
                ctx.Pdf.SaveState();
                ctx.Pdf.SetFillColor(new DeviceRgb(200, 200, 200));
                ctx.Pdf.Rectangle(bx + 3, ctx.Plot.GetBottom(), barW, bh).Fill();
                ctx.Pdf.RestoreState();

                ctx.Pdf.SetFillColor(colors[i]);
                ctx.Pdf.RoundRectangle(bx, ctx.Plot.GetBottom(), barW, bh, 4).Fill();

                // Value label on top
                ctx.Text.ShowTextAligned(new Paragraph($"{values[i]:0} €").SetFont(bold).SetFontSize(10)
                    .SetFontColor(colors[i]),
                    bx + barW / 2, ctx.Plot.GetBottom() + bh + 6, TextAlignment.CENTER);

                // Percentage
                double pct = (values[i] / totalSavings) * 100;
                ctx.Text.ShowTextAligned(new Paragraph($"({pct:0}%)").SetFont(regular).SetFontSize(8)
                    .SetFontColor(ColorConstants.DARK_GRAY),
                    bx + barW / 2, ctx.Plot.GetBottom() + bh / 2, TextAlignment.CENTER);

                // X label
                ctx.Text.ShowTextAligned(new Paragraph(labels[i]).SetFont(regular).SetFontSize(9)
                    .SetFontColor(Ink),
                    bx + barW / 2, ctx.Plot.GetBottom() - 14, TextAlignment.CENTER);
            }

            // Y-axis
            for (int i = 0; i <= 5; i++)
            {
                float ty = ctx.Plot.GetBottom() + i * (ctx.PlotH / 5f);
                double val = (maxY / 5.0) * i;
                ctx.Text.ShowTextAligned(new Paragraph(val.ToString("0")).SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY),
                    ctx.Plot.GetLeft() - 22, ty - 3, TextAlignment.RIGHT);
            }

            ctx.Text.ShowTextAligned(new Paragraph("€/rok").SetFont(bold).SetFontSize(9).SetFontColor(DarkBlue),
                ctx.Plot.GetLeft() - 22, ctx.Plot.GetTop() + 8, TextAlignment.RIGHT);

            // Total box
            var totalBox = new Div()
                .SetBackgroundColor(new DeviceRgb(255, 248, 225))
                .SetBorder(new SolidBorder(AccentYellow, 2))
                .SetBorderRadius(new BorderRadius(8))
                .SetPadding(10)
                .SetWidth(180);

            totalBox.Add(new Paragraph($"🎯 Celkové ročné úspory: {totalSavings:0} €")
                .SetFont(bold).SetFontSize(10).SetFontColor(new DeviceRgb(230, 126, 34))
                .SetTextAlignment(TextAlignment.CENTER));

            var row = new Table(UnitValue.CreatePercentArray(new float[] { 60, 40 })).UseAllAvailableWidth();
            row.AddCell(new Cell().SetBorder(Border.NO_BORDER)); // prázdna ľavá
            row.AddCell(new Cell().Add(totalBox).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT));
            doc.Add(row);
        }

        private void Draw_Daily_Histogram(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular, float w, float h)
        {
            var vals = m.Daily_Values ?? Array.Empty<double>();
            if (vals.Length == 0) return;

            var ctx = BeginPremiumChart(doc, "📦 Histogram denných súčtov spotreby",
                "Rozdelenie dní podľa celkovej dennej spotreby. Pomáha identifikovať typické rozsahy a extrémy.",
                bold, regular, w, h, DarkBlue);

            DrawPremiumGrid(ctx, 5);

            int bins = 20;
            double min = vals.Min(), max = vals.Max();
            if (max <= min) max = min + 1;
            double bw = (max - min) / bins;
            var counts = new int[bins];

            foreach (var v in vals)
            {
                int bi = (int)Math.Min(bins - 1, Math.Floor((v - min) / bw));
                counts[bi]++;
            }
            int maxCount = Math.Max(1, counts.Max());

            float step = ctx.PlotW / bins;
            for (int i = 0; i < bins; i++)
            {
                float bx = ctx.Plot.GetLeft() + i * step + step * 0.15f;
                float barW = step * 0.7f;
                float bh = (float)((counts[i] / (double)maxCount) * ctx.PlotH);

                ctx.Pdf.SetFillColor(new DeviceRgb(52, 152, 219));
                ctx.Pdf.RoundRectangle(bx, ctx.Plot.GetBottom(), barW, bh, 2).Fill();
            }

            // Y-labels
            for (int i = 0; i <= 5; i++)
            {
                float ty = ctx.Plot.GetBottom() + i * (ctx.PlotH / 5f);
                int val = (int)Math.Round((maxCount / 5.0) * i);
                ctx.Text.ShowTextAligned(new Paragraph(val.ToString()).SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY),
                    ctx.Plot.GetLeft() - 22, ty - 3, TextAlignment.RIGHT);
            }
            DrawYAxisLabel(ctx, "počet dní", bold, DarkBlue);
        }
        private void Draw_Configs_Scatter(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular, float w, float h)
        {
            var rows = m.Top ?? new List<AnalysisPdfModel.DesignRow>();
            if (rows.Count == 0) return;

            var ctx = BeginPremiumChart(doc, "🟢 Porovnanie konfigurácií (kWp vs. Bat kWh)",
                "Každý bod je jedna konfigurácia; farba ~ autarkia, väčšie body ~ vyššia vlastná spotreba.",
                bold, regular, w, h, Green);

            DrawPremiumGrid(ctx, 5);

            double minX = rows.Min(r => r.KwP), maxX = rows.Max(r => r.KwP);
            double minY = rows.Min(r => r.BatKWh), maxY = rows.Max(r => r.BatKWh);
            if (maxX <= minX) maxX = minX + 1; if (maxY <= minY) maxY = minY + 1;

            foreach (var r in rows)
            {
                float px = (float)(ctx.Plot.GetLeft() + ((r.KwP - minX) / (maxX - minX)) * ctx.PlotW);
                float py = (float)(ctx.Plot.GetBottom() + ((r.BatKWh - minY) / (maxY - minY)) * ctx.PlotH);

                // farba podľa autarkie (0–100%)
                double a = Math.Max(0, Math.Min(100, r.AutarkyPct));
                var col = new DeviceRgb(
                    (int)(255 - a * 1.5),           // menej červenej pri vyššej autarkii
                    (int)(100 + a * 1.5),
                    100);

                // veľkosť podľa vlastnej spotreby (min 4, max 10)
                double sc = Math.Max(4, Math.Min(10, (r.SelfConsumptionPct / 100.0) * 10.0));
                ctx.Pdf.SaveState();
                ctx.Pdf.SetFillColor(col);
                ctx.Pdf.Circle(px, py, (float)sc).Fill();
                ctx.Pdf.RestoreState();
            }

            // osi
            for (int i = 0; i <= 5; i++)
            {
                double vx = minX + (maxX - minX) * i / 5.0;
                float tx = ctx.Plot.GetLeft() + (float)(ctx.PlotW * i / 5.0);
                ctx.Text.ShowTextAligned(new Paragraph(vx.ToString("0.0")).SetFont(regular).SetFontSize(8).SetFontColor(Ink),
                    tx, ctx.Plot.GetBottom() - 14, TextAlignment.CENTER);

                double vy = minY + (maxY - minY) * i / 5.0;
                float ty = ctx.Plot.GetBottom() + (float)(ctx.PlotH * i / 5.0);
                ctx.Text.ShowTextAligned(new Paragraph(vy.ToString("0.0")).SetFont(regular).SetFontSize(8).SetFontColor(Ink),
                    ctx.Plot.GetLeft() - 22, ty - 3, TextAlignment.RIGHT);
            }

            // popisy osí
            ctx.Text.ShowTextAligned(new Paragraph("kWp").SetFont(bold).SetFontSize(9).SetFontColor(DarkBlue),
                ctx.Plot.GetLeft() + ctx.PlotW / 2, ctx.Plot.GetBottom() - 26, TextAlignment.CENTER);
            DrawYAxisLabel(ctx, "Batéria (kWh)", bold, DarkBlue);
        }

        // ▼▼▼ PRIDAJ DO TRIEDY AnalysisPdfExportService2 ▼▼▼
        private void DrawYAxisLabel(ChartCtx ctx, string text, PdfFont bold, Color color)
        {
            // Otočený popis osi Y (čitateľný a konzistentný)
            var p = new Paragraph(text)
                .SetFont(bold)
                .SetFontSize(9)
                .SetFontColor(color)
                .SetRotationAngle(Math.PI / 2); // 90°

            // Umiestnenie: doľava od plochy grafu, zvisle centrované
            float x = ctx.Plot.GetLeft() - 38f;
            float y = ctx.Plot.GetBottom() + ctx.PlotH / 2f;

            ctx.Text.ShowTextAligned(p, x, y, TextAlignment.CENTER);
        }
        // ▲▲▲ KONIEC METÓDY ▲▲▲


        // ------------------- Top Table Premium -------------------
        private void AddTopTablePremium(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular)
        {
            var altInfo = "Porovnané alternatívy: rôzne kombinácie výkonu FVE (kWp) a kapacity batérie (kWh). " +
              "Tabuľka zobrazuje energetické parametre (autarkia, vlastná spotreba, import/export, výroba).";
            var note = new Paragraph(altInfo)
                .SetFont(regular).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                .SetMarginTop(-6).SetMarginBottom(6);
            doc.Add(note);

            var headers = new[] { "kWp", "Bat kWh", "Autarky %", "Vlastná spotreba %", "Import kWh", "Export kWh", "Výroba PV kWh" };
            var widths = new float[] { 12, 14, 14, 16, 16, 16, 12 };

            var table = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth();

            foreach (var h in headers)
            {
                table.AddHeaderCell(
                    new Cell().Add(new Paragraph(h).SetFont(bold).SetFontSize(9).SetFontColor(ColorConstants.WHITE))
                              .SetBackgroundColor(DarkBlue)
                              .SetBorder(Border.NO_BORDER)
                              .SetPadding(6));
            }

            bool alt = false;
            Cell CellTxt(string s, Color bg, PdfFont font)
            {
                return new Cell()
                    .Add(new Paragraph(s).SetFont(font).SetFontSize(9).SetFontColor(Ink))
                    .SetBackgroundColor(bg)
                    .SetBorder(new SolidBorder(new DeviceRgb(232, 235, 239), 1))
                    .SetPadding(7);
            }
            foreach (var r in m.Top ?? Enumerable.Empty<AnalysisPdfModel.DesignRow>())
            {
                Color bg = alt ? new DeviceRgb(247, 249, 252) : ColorConstants.WHITE;
                table.AddCell(CellTxt($"{r.KwP:0.0}", bg, regular));
                table.AddCell(CellTxt($"{r.BatKWh:0.0}", bg, regular));
                table.AddCell(CellTxt($"{r.AutarkyPct:0.0}", bg, regular));
                table.AddCell(CellTxt($"{r.SelfConsumptionPct:0.0}", bg, regular));
                table.AddCell(CellTxt($"{r.ImportKWh:0}", bg, regular));
                table.AddCell(CellTxt($"{r.ExportKWh:0}", bg, regular));
                table.AddCell(CellTxt($"{r.PvKWh:0}", bg, regular));
                alt = !alt;
            }

            doc.Add(table);
            doc.Add(new Paragraph().SetHeight(10));
        }

        private void AddTechnicalParameters(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular)
        {
            doc.Add(new Paragraph("Technické parametre simulácie")
                .SetFont(bold).SetFontSize(14).SetFontColor(DarkBlue).SetMarginBottom(8));

            var grid = new Table(UnitValue.CreatePercentArray(new float[] { 42, 58 })).UseAllAvailableWidth();

            void Row(string k, string v)
            {
                grid.AddCell(new Cell().Add(new Paragraph(k).SetFont(bold).SetFontSize(9).SetFontColor(DarkBlue))
                    .SetBorder(Border.NO_BORDER).SetPadding(3));
                grid.AddCell(new Cell().Add(new Paragraph(v).SetFont(regular).SetFontSize(9))
                    .SetBorder(Border.NO_BORDER).SetPadding(3));
            }

            Row("Ročný výnos (kWh/kWp/rok)", $"{m.YearlyYield:0}");
            Row("Ceny: Import / Export (€/kWh)", $"{m.PriceImport:0.000} / {m.PriceExport:0.000}");
            Row("Limity: Batéria kW / Menič kW / Export kW", $"{m.BatteryMaxKW:0.0} / {m.InverterKW:0.0} / {m.ExportLimitKW:0.0}");
            Row("Účinnosť (nab./vyb.)", $"{m.EffChargeDischargePct:0.0}% / {m.EffChargeDischargePct:0.0}%");
            Row("Mesačné podiely výroby", m.MonthlySharesCsv);

            var card = new Div()
                .SetBackgroundColor(ColorConstants.WHITE)
                .SetBorder(new SolidBorder(GrayLine, 1))
                .SetBorderRadius(new BorderRadius(10))
                .SetPadding(12)
                .Add(grid);

            doc.Add(card);
        }

        private void AddExplanationGuide(Document doc, AnalysisPdfModel m, PdfFont bold, PdfFont regular)
        {
            doc.Add(new Paragraph("Ako čítať túto správu")
                .SetFont(bold).SetFontSize(16).SetFontColor(DarkBlue).SetMarginBottom(10));

            var dl = new List().SetSymbolIndent(8).SetFont(regular).SetFontSize(10);

            dl.Add(new ListItem("KPI: rýchly prehľad kľúčových čísel – obdobie, priemer, chýbajúce dáta, miera nezávislosti a vlastná spotreba."));
            dl.Add(new ListItem("Priemerný deň: 15-min profil ukazuje, kedy typicky vyrábate/spotrebúvate/ťaháte zo siete."));
            dl.Add(new ListItem("Mesačný prehľad: sezónnosť spotreby a výroby; import/export pomáha dimenzovať FVE a batériu."));
            dl.Add(new ListItem("Denné súčty: odhalí extrémy – vysoké špičky môžu indikovať flexibilné zaťaženie."));
            dl.Add(new ListItem("Bilancia (stacked): koľko pokryje vlastná výroba vs. dovoz zo siete – cieľom je maximalizovať „zelenú“ časť."));
            dl.Add(new ListItem("Krivka trvania: zoradené dni podľa spotreby – pomáha dimenzovať batériu či rezervu meniča."));
            dl.Add(new ListItem("Top konfigurácie: porovnanie variant podľa energetických parametrov (autarkia, vlastná spotreba, import/export)."));

            var info = new Div()
                .SetBackgroundColor(LightGray)
                .SetBorder(new SolidBorder(Blue, 1))
                .SetBorderRadius(new BorderRadius(10))
                .SetPadding(12);

            info.Add(new Paragraph("Poznámka k dátam")
                .SetFont(bold).SetFontSize(11).SetFontColor(DarkBlue).SetMarginBottom(6));
            info.Add(new Paragraph(
                $"Analýza vychádza zo {m.Days} dní merania. Vplyv chýbajúcich intervalov: {m.MissingIntervals}. " +
                "Ak sú výpadky výrazné alebo sezónne skreslené, odporúčame doplniť dáta pre presnejšiu optimalizáciu.")
                .SetFont(regular).SetFontSize(9).SetFontColor(Ink));

            doc.Add(dl);
            doc.Add(new Paragraph().SetHeight(8));
            doc.Add(info);
        }

        private void AddPremiumFooter(Document doc, AnalysisPdfModel m, PdfFont regular)
        {
            doc.Add(new Paragraph().SetMarginTop(10));
            doc.Add(new Paragraph().SetHeight(1.2f).SetBackgroundColor(Blue));

            var footer = new Table(UnitValue.CreatePercentArray(new float[] { 60, 40 })).UseAllAvailableWidth();
            footer.AddCell(new Cell().Add(new Paragraph($"Vygenerované: {DateTime.Now.AddHours(1):dd. MM. yyyy HH:mm}"))
                                     .SetBorder(Border.NO_BORDER).SetFont(regular).SetFontSize(9)
                                     .SetFontColor(ColorConstants.DARK_GRAY));
            footer.AddCell(new Cell().Add(new Paragraph("NestStats – analytická správa"))
                                     .SetBorder(Border.NO_BORDER).SetFont(regular).SetFontSize(9)
                                     .SetTextAlignment(TextAlignment.RIGHT).SetFontColor(DarkBlue));
            doc.Add(footer);
        }

        private void AddPageNumber(Document doc, PdfFont regular, int pageNumber)
        {
            var page = EnsurePage(doc);
            var pageSize = page.GetPageSize();
            var pdfCanvas = new PdfCanvas(page);

            var p = new Paragraph($"Strana {pageNumber}")
                .SetFont(regular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY);

            var layoutCanvas = new iText.Layout.Canvas(pdfCanvas, pageSize);
            layoutCanvas.ShowTextAligned(p, pageSize.GetWidth() - 40, 18, TextAlignment.RIGHT);
            layoutCanvas.Close();
        }

        // ==================== DI Registration ====================
    }

    public static class PdfAnalysis2DI
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddAnalysisPdfExportService2(
            this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            services.AddScoped<IAnalysisPdfExportService2, AnalysisPdfExportService2>();
            return services;
        }
    }
}
