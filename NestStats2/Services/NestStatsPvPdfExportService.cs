using System;
using System.Globalization;
using System.IO;
using System.Linq;

using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.IO.Image;

using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;

using Microsoft.AspNetCore.Hosting;
using NestStats2.Pages.NavrhFotovoltaiky;

namespace NestStats2.Services
{
    public interface INestStatsPvPdfExportService
    {
        byte[] GenerateSavingsReport(
            IndexModel.InputParameters input,
            IndexModel.CalculationResult result);
    }

    public class NestStatsPvPdfExportService : INestStatsPvPdfExportService
    {
        private readonly IWebHostEnvironment _env;

        // NEP brand farby (jemné, čisté)
        private static readonly Color NEP_BLUE = new DeviceRgb(0, 102, 204);          // #0066CC
        private static readonly Color NEP_DARK_BLUE = new DeviceRgb(0, 76, 153);      // #004C99
        private static readonly Color NEP_LIGHT_BLUE = new DeviceRgb(230, 244, 255);  // #E6F4FF

        private static readonly Color NEP_RED = new DeviceRgb(220, 53, 69);           // #DC3545
        private static readonly Color NEP_LIGHT_RED = new DeviceRgb(255, 235, 238);  // #FFEBEE

        private static readonly Color ACCENT_GREEN = new DeviceRgb(40, 167, 69);      // #28A745
        private static readonly Color ACCENT_ORANGE = new DeviceRgb(255, 193, 7);     // #FFC107

        private static readonly Color PANEL_BG = new DeviceRgb(248, 249, 252);        // #F8F9FC
        private static readonly Color GRID_BORDER = new DeviceRgb(229, 231, 235);     // #E5E7EB

        private readonly string _logoPath;
        private readonly string _fontsDir;

        public NestStatsPvPdfExportService(IWebHostEnvironment env)
        {
            _env = env;
            _logoPath = System.IO.Path.Combine(env.WebRootPath, "logo.png");
            _fontsDir = System.IO.Path.Combine(env.WebRootPath, "fonts");
        }

        public byte[] GenerateSavingsReport(
            IndexModel.InputParameters input,
            IndexModel.CalculationResult result)
        {
            var sk = new CultureInfo("sk-SK");

            using var ms = new MemoryStream();
            using var writer = new PdfWriter(ms);
            using var pdf = new PdfDocument(writer);

            var (font, fontBold) = GetFonts();

            // Hlavička + pätička na každej strane (event handler)
            var hf = new NepHeaderFooterHandler(_logoPath, font, fontBold);
            pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, hf);

            // Väčšie horné/ spodné okraje kvôli hlavičke/pätičke
            using var doc = new Document(pdf, PageSize.A4);
            doc.SetMargins(topMargin: 92, rightMargin: 30, bottomMargin: 55, leftMargin: 30);

            // ──────────────────────────────────────────────────────────────
            // Prepočty a „oficiálne“ doplnenia (bill series – rovnaká logika ako FE)
            // ──────────────────────────────────────────────────────────────
            var customerLabel = input.CustomerType == "corp" ? "Právnická osoba" : "Fyzická osoba";

            double totalPanels = input.Strings?.Sum(s => (double)s.PanelsCount) ?? 0;
            double totalArea = input.Strings?.Sum(s => s.PanelsCount * s.PanelArea) ?? 0;
            double installedPowerKw = result.InstalledPowerKw;
            double powerDensity = totalArea > 0 ? installedPowerKw * 1000.0 / totalArea : 0.0;

            // CO2 odhad (tak ako si mal)
            double co2KgPerYear = result.AverageAnnualProduction * 0.4; // kg/rok
            double co2TonsPerYear = co2KgPerYear / 1000.0;

            // Payback text
            string paybackText = result.PaybackYears > 0
                ? $"{result.PaybackYears.ToString("N2", sk)} rokov"
                : "Viac ako životnosť systému";

            // Bill inputs
            var bill = ComputeBillSeriesIfAvailable(input, result);

            // Kľúčové sumy
            var annualResults = result.AnnualResults ?? new();
            var annualCount = annualResults.Count;
            double totalProductionKwh = annualCount > 0 ? annualResults.Last().CumulativeProductionKwh : 0;
            double totalValueEuros = annualCount > 0 ? annualResults.Last().CumulativeValueEuros : 0;
            double netBenefit = totalValueEuros - input.InitialInvestment;

            // Ak je vyplnený účet, dopočítaj aj „celkové náklady bez FV vs. s FV“
            var billTotals = bill.HasBill
                ? ComputeBillTotalsOverHorizon(input, result, bill, horizonYears: Math.Min(input.LifetimeYears, annualCount))
                : (has: false, totalWithout: 0.0, totalWith: 0.0, totalSavings: 0.0);

            // ──────────────────────────────────────────────────────────────
            // STRANA 1: Executive summary (pre klienta)
            // ──────────────────────────────────────────────────────────────
            AddHeroTitle(doc, input, font, fontBold, sk);


            AddExecutiveSummaryCards(
                doc, input, result,
                customerLabel,
                paybackText,
                installedPowerKw,
                powerDensity,
                co2TonsPerYear,
                totalProductionKwh,
                totalValueEuros,
                netBenefit,
                bill,
                billTotals,
                font, fontBold, sk
            );

            AddAssumptionsCard(doc, input, customerLabel, font, fontBold, sk);

            // ──────────────────────────────────────────────────────────────
            // STRANA 2: Výroba & hodnota (1/10/20/30 + mesačný profil)
            // ──────────────────────────────────────────────────────────────
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            AddSectionHeader(doc, "Výroba energie a hodnota", "Prehľad očakávanej výroby, celkových hodnôt a profilu počas roka.", font, fontBold);

            AddSummary132030(doc, result, font, fontBold, sk);

            // Mesačný profil (ak ho vieme získať – buď result.MonthlyProduction alebo result.MonthlyProductionKwh)
            var monthly = TryGetMonthlyProduction(result);
            if (monthly != null && monthly.Length == 12 && monthly.Any(v => v > 0))
            {
                AddMiniMonthlyBarChart(doc, monthly, font, fontBold, sk);
            }
            else
            {
                AddInfoNote(doc,
                    "Mesačný profil výroby nie je v tomto výpočte k dispozícii. Ak kalkulačka vráti mesačné dáta, graf sa zobrazí automaticky.",
                    font);
            }

            AddSystemConfigurationOverview(doc, input, installedPowerKw, totalPanels, totalArea, font, fontBold, sk);

            // ──────────────────────────────────────────────────────────────
            // STRANA 3: Účet za elektrinu (ak zadaný) + úspory z účtu (bez externých grafov)
            // ──────────────────────────────────────────────────────────────
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            AddSectionHeader(doc, "Účet za elektrinu a úspora", "Porovnanie ročných nákladov bez FV vs. s FV na základe zadaného účtu a vlastnej spotreby.", font, fontBold);

            if (bill.HasBill)
            {
                AddBillKpiRow(doc, bill, font, fontBold, sk);
                AddBillMilestonesTable(doc, bill, font, fontBold, sk);
                AddBillMiniComparisonBars(doc, bill, font, fontBold, sk);
            }
            else
            {
                AddWarningNote(doc,
                    "Účet za elektrinu nie je vyplnený (ročná platba = 0 €). Po doplnení mesačnej alebo ročnej platby a vlastnej spotreby sa tu automaticky zobrazí porovnanie „pred FV vs. po FV“ aj odhad kumulatívnej úspory.",
                    font);
            }

            // ──────────────────────────────────────────────────────────────
            // STRANA 4: Detailná tabuľka (landscape) – kompletné dáta
            // ──────────────────────────────────────────────────────────────
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            AddSectionHeader(doc, "Detailná ročná tabuľka", "Kompletné ročné údaje výpočtu (výroba, cena, hodnota, cash-flow, ROI + účet ak je zadaný).", font, fontBold);

            AddAnnualDetailsTablePortrait(doc, input, result, bill, font, fontBold, sk);


            // ──────────────────────────────────────────────────────────────
            // STRANA 5: Stringy (A4 portrait) + interpretácia
            // ──────────────────────────────────────────────────────────────
            doc.Add(new AreaBreak(PageSize.A4));
            AddSectionHeader(doc, "Konfigurácia FV polí (stringovanie)", "Zadané FV polia a ich parametre. Slúži ako technický prehľad pre návrh a kontrolu.", font, fontBold);

            AddStringsConfiguration(doc, input, font, fontBold, sk);

            AddInterpretationSection(doc, input, result, paybackText, co2TonsPerYear, bill, font, fontBold, sk);

            doc.Close();
            return ms.ToArray();
        }

        // =====================================================================
        // 1) FONTY
        // =====================================================================
        private (PdfFont regular, PdfFont bold) GetFonts()
        {
            try
            {
                var regularFontPath = System.IO.Path.Combine(_fontsDir, "Montserrat-Medium.ttf");
                var boldFontPath = System.IO.Path.Combine(_fontsDir, "Montserrat-ExtraBold.ttf");

                if (File.Exists(regularFontPath) && File.Exists(boldFontPath))
                {
                    var regular = PdfFontFactory.CreateFont(regularFontPath, PdfEncodings.IDENTITY_H);
                    var bold = PdfFontFactory.CreateFont(boldFontPath, PdfEncodings.IDENTITY_H);
                    return (regular, bold);
                }
            }
            catch { /* ignore */ }

            var fallback = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fallbackBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            return (fallback, fallbackBold);
        }

        // =====================================================================
        // 2) HERO + SEKCIE
        // =====================================================================
        private void AddHeroTitle(Document doc, IndexModel.InputParameters input, PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            var titleWrap = new Div()
                .SetBackgroundColor(NEP_LIGHT_BLUE)
                .SetBorder(new SolidBorder(NEP_BLUE, 1.2f))
                .SetBorderRadius(new BorderRadius(14))
                .SetPadding(14)
                .SetMarginBottom(14);

            titleWrap.Add(new Paragraph("Vaša cesta k nižším nákladom na elektrinu")
                .SetFont(fontBold).SetFontSize(18).SetFontColor(NEP_DARK_BLUE)
                .SetMargin(0));

            titleWrap.Add(new Paragraph("Prehľad úspor a prínosov fotovoltického systému od NestStats – súhrn očakávanej produkcie energie, ekonomického prínosu investície, doby návratnosti a dopadu na náklady za elektrinu. Dokument slúži ako prehľadný podklad na posúdenie celkovej finančnej efektívnosti projektu.")
                .SetFont(font).SetFontSize(10).SetFontColor(ColorConstants.DARK_GRAY)
                .SetMarginTop(4).SetMarginBottom(0));
            
            // Klient (nepovinné)
            var client = (input?.ClientName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(client))
            {
                titleWrap.Add(new Paragraph($"Klient: {client}")
                    .SetFont(fontBold).SetFontSize(10).SetFontColor(NEP_DARK_BLUE)
                    .SetMarginTop(6).SetMarginBottom(0));
            }


            titleWrap.Add(new Paragraph($"Vygenerované: {DateTime.Now.AddHours(1):dd. MM. yyyy HH:mm}"
                )
                .SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                .SetMarginTop(8).SetMarginBottom(0));

            doc.Add(titleWrap);
        }

        private void AddSectionHeader(Document doc, string title, string subtitle, PdfFont font, PdfFont fontBold)
        {
            doc.Add(new Paragraph(title)
                .SetFont(fontBold).SetFontSize(16).SetFontColor(NEP_DARK_BLUE)
                .SetMarginTop(0).SetMarginBottom(2));

            doc.Add(new Paragraph(subtitle)
                .SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                .SetMarginTop(0).SetMarginBottom(10));

            doc.Add(new Paragraph()
                .SetHeight(2)
                .SetBackgroundColor(NEP_BLUE)
                .SetMarginTop(2)
                .SetMarginBottom(12));
        }

        private void AddInfoNote(Document doc, string text, PdfFont font)
        {
            var box = new Div()
                .SetBackgroundColor(new DeviceRgb(245, 247, 250))
                .SetBorder(new SolidBorder(GRID_BORDER, 1f))
                .SetBorderRadius(new BorderRadius(10))
                .SetPadding(10)
                .SetMarginTop(10);

            box.Add(new Paragraph(text)
                .SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                .SetMargin(0));

            doc.Add(box);
        }

        private void AddWarningNote(Document doc, string text, PdfFont font)
        {
            var box = new Div()
                .SetBackgroundColor(NEP_LIGHT_RED)
                .SetBorder(new SolidBorder(NEP_RED, 1.2f))
                .SetBorderRadius(new BorderRadius(10))
                .SetPadding(10)
                .SetMarginTop(10);

            box.Add(new Paragraph(text)
                .SetFont(font).SetFontSize(9).SetFontColor(NEP_RED)
                .SetMargin(0));

            doc.Add(box);
        }

        // =====================================================================
        // 3) EXEC SUMMARY (KPI dashboard “pre klienta”)
        // =====================================================================
        private void AddExecutiveSummaryCards(
            Document doc,
            IndexModel.InputParameters input,
            IndexModel.CalculationResult result,
            string customerLabel,
            string paybackText,
            double installedPowerKw,
            double powerDensity,
            double co2TonsPerYear,
            double totalProductionKwh,
            double totalValueEuros,
            double netBenefit,
            BillSeries bill,
            (bool has, double totalWithout, double totalWith, double totalSavings) billTotals,
            PdfFont font, PdfFont fontBold,
            CultureInfo sk)
        {
            doc.Add(new Paragraph("Najdôležitejšie z tohto návrhu")
                .SetFont(fontBold).SetFontSize(13).SetFontColor(NEP_DARK_BLUE)
                .SetMarginBottom(8));

            // 1. riadok KPI
            var t1 = new Table(UnitValue.CreatePercentArray(new float[] { 33, 33, 34 }))
                .UseAllAvailableWidth()
                .SetMarginBottom(8);

            AddKpiCard(t1, "Výkon systému", $"{installedPowerKw.ToString("N2", sk)} kWp",
                $"Ročná výroba: {result.AverageAnnualProduction.ToString("N0", sk)} kWh", NEP_BLUE, font, fontBold);

            AddKpiCard(t1, "Návratnosť investície", paybackText,
                "Odhad podľa hodnoty energie.", ACCENT_ORANGE, font, fontBold);

            AddKpiCard(t1, "Úspora CO₂ (odhad)", $"{co2TonsPerYear.ToString("N1", sk)} t/rok",
                "Voči elektrine zo siete.", ACCENT_GREEN, font, fontBold);

            doc.Add(t1);

            // 2. riadok KPI
            var t2 = new Table(UnitValue.CreatePercentArray(new float[] { 33, 33, 34 }))
                .UseAllAvailableWidth()
                .SetMarginBottom(10);

            AddKpiCard(t2, "Využitie plochy", totalProductionKwh > 0 && powerDensity > 0
                    ? $"{powerDensity.ToString("N0", sk)} W/m²"
                    : "N/A",
                $"Plocha FV: {(input.Strings?.Sum(s => s.PanelsCount * s.PanelArea) ?? 0).ToString("N1", sk)} m²",
                NEP_DARK_BLUE, font, fontBold);

            AddKpiCard(t2, "Výroba za obdobie", $"{totalProductionKwh.ToString("N0", sk)} kWh",
                $"{Math.Min(input.LifetimeYears, result.AnnualResults.Count)} rokov podľa výpočtu.", NEP_BLUE, font, fontBold);

            var netColor = netBenefit >= 0 ? ACCENT_GREEN : NEP_RED;
            AddKpiCard(t2, "Váš finančný prínos", $"{netBenefit.ToString("N0", sk)} €",
                $"(Hodnota energie - investícia)",
                netColor, font, fontBold);

            doc.Add(t2);

            // Účet (ak je)
            if (bill.HasBill)
            {
                var billBox = new Div()
                    .SetBackgroundColor(ColorConstants.WHITE)
                    .SetBorder(new SolidBorder(NEP_BLUE, 1.2f))
                    .SetBorderRadius(new BorderRadius(14))
                    .SetPadding(12)
                    .SetMarginBottom(10);

                billBox.Add(new Paragraph("Ako sa môže zmeniť Váš účet za elektrinu")
                    .SetFont(fontBold).SetFontSize(11).SetFontColor(NEP_DARK_BLUE)
                    .SetMarginBottom(6));

                var y1 = bill.YearIndex(0);

                var row = new Table(UnitValue.CreatePercentArray(new float[] { 25, 25, 25, 25 }))
                    .UseAllAvailableWidth();

                AddMiniStat(row, "Dnešná platba/rok", $"{y1.billBefore.ToString("N0", sk)} €", NEP_RED, font, fontBold);
                AddMiniStat(row, "Účet/1 rok po FV", $"{y1.billAfter.ToString("N0", sk)} €", ACCENT_GREEN, font, fontBold);
                AddMiniStat(row, "Úspora/1 rok po FV", $"{y1.savings.ToString("N0", sk)} €", NEP_BLUE, font, fontBold);

                billBox.Add(row);

                billBox.Add(new Paragraph($"Predpoklady: vlastná spotreba {bill.SelfConsumptionPct.ToString("N0", sk)}% | inflácia cien el. {bill.EnergyInflationPct.ToString("N1", sk)}%/rok")
                    .SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                    .SetMarginTop(4).SetMarginBottom(0));

                doc.Add(billBox);
                billBox.Add(row);
                billBox.Add(row);
            }
            else
            {
                AddInfoNote(doc,
                    "Ak doplníte „Účet za elektrinu“ a „Vlastná spotreba FV“, softvér automaticky zobrazí porovnanie účtu bez FV vs. s FV a celkové úspory.",
                    font);
            }
        }

        private void AddMiniStat(Table t, string label, string value, Color accent, PdfFont font, PdfFont fontBold)
        {
            var box = new Div()
                .SetBackgroundColor(PANEL_BG)
                .SetBorder(new SolidBorder(accent, 1.0f))
                .SetBorderRadius(new BorderRadius(12))
                .SetPadding(10);

            box.Add(new Paragraph(label)
                .SetFont(fontBold).SetFontSize(9).SetFontColor(accent)
                .SetMarginBottom(4));

            box.Add(new Paragraph(value)
                .SetFont(fontBold).SetFontSize(14).SetFontColor(ColorConstants.BLACK)
                .SetMargin(0));

            t.AddCell(new Cell().SetBorder(Border.NO_BORDER).Add(box).SetPadding(4));
        }

        private void AddKpiCard(Table parent, string title, string value, string description,
            Color accent, PdfFont font, PdfFont fontBold)
        {
            var card = new Div()
                .SetBackgroundColor(ColorConstants.WHITE)
                .SetBorderRadius(new BorderRadius(14))
                .SetBorder(new SolidBorder(accent, 1.1f))
                .SetPadding(12)
                .SetMargin(2);

            card.Add(new Paragraph(title)
                .SetFont(fontBold).SetFontSize(10).SetFontColor(accent).SetMarginBottom(3));

            card.Add(new Paragraph(value)
                .SetFont(fontBold).SetFontSize(15).SetFontColor(ColorConstants.BLACK).SetMarginBottom(4));

            card.Add(new Paragraph(description)
                .SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY).SetMargin(0));

            parent.AddCell(new Cell().SetBorder(Border.NO_BORDER).Add(card));
        }

        // =====================================================================
        // 4) PREDPOKLADY (vstupy)
        // =====================================================================
        private void AddAssumptionsCard(Document doc, IndexModel.InputParameters input, string customerLabel,
            PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            doc.Add(new Paragraph("Na základe akých údajov rátame Vaše úspory")
                .SetFont(fontBold)
                .SetFontSize(13)
                .SetFontColor(NEP_DARK_BLUE)
                .SetMarginTop(4)
                .SetMarginBottom(6));

            var card = new Div()
                .SetBackgroundColor(NEP_LIGHT_BLUE)
                .SetBorderRadius(new BorderRadius(14))
                .SetBorder(new SolidBorder(NEP_BLUE, 1.0f))
                .SetPadding(12)
                .SetMarginBottom(10);

            var table = new Table(UnitValue.CreatePercentArray(new float[] { 42, 58 }))
                .UseAllAvailableWidth();

            AddSpecRow(table, "Typ zákazníka", customerLabel, font, fontBold);

            AddSpecRow(table, "Počiatočná investícia", input.InitialInvestment.ToString("N2", sk) + " €", font, fontBold);
            AddSpecRow(table, "Životnosť systému", input.LifetimeYears + " rokov", font, fontBold);

            AddSpecRow(table, "Cena elektriny – FO", input.PricePerson.ToString("N2", sk) + " €/kWh", font, fontBold);
            AddSpecRow(table, "Cena elektriny – PO", input.PriceCorp.ToString("N2", sk) + " €/kWh", font, fontBold);

            AddSpecRow(table, "Inflácia cien elektriny", input.EnergyInflationPercent.ToString("N1", sk) + " %/rok", font, fontBold);
            AddSpecRow(table, "Všeobecná inflácia", input.GeneralInflationPercent.ToString("N1", sk) + " %/rok", font, fontBold);

            AddSpecRow(table, "Degradácia FV panelov", input.DegradationPercent.ToString("N2", sk) + " %/rok", font, fontBold);
            AddSpecRow(table, "Systémové straty", input.SystemLossesPercent.ToString("N1", sk) + " %", font, fontBold);
            AddSpecRow(table, "Účinnosť meniča", input.InverterEfficiencyPercent.ToString("N1", sk) + " %", font, fontBold);

            // Bill input prehľad
            var annualBill = ResolveAnnualBill(input);
            AddSpecRow(table, "Účet za elektrinu (dnes)", annualBill.ToString("N2", sk) + " €/rok", font, fontBold);
            AddSpecRow(table, "Vlastná spotreba FV", (input.SelfConsumptionPercent).ToString("N0", sk) + " %", font, fontBold);

            card.Add(table);

            card.Add(new Paragraph("Ako sme to počítali")
                .SetFont(fontBold).SetFontSize(10).SetFontColor(NEP_DARK_BLUE)
                .SetMarginTop(10).SetMarginBottom(4));

            card.Add(new Paragraph(
                    "Výrobu (kWh) prevádzame na peniaze podľa ceny elektriny v danom roku (€/kWh), na základe zadaných parametrov počítame návratnosť. Ak je vyplnený účet, porovnanie „bez FV vs. s FV“ zohľadňuje vlastnú spotrebu – teda časť výroby, ktorá reálne zníži účet mimo virtálnej batérie, ktorá je v tomto výpočte vynechaná.")
                .SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                .SetMargin(0));

            doc.Add(card);
        }

        private void AddSpecRow(Table t, string label, string value, PdfFont font, PdfFont fontBold)
        {
            t.AddCell(new Cell()
                .Add(new Paragraph(label).SetFont(fontBold).SetFontSize(9).SetFontColor(NEP_DARK_BLUE))
                .SetBorder(Border.NO_BORDER).SetPadding(3));

            t.AddCell(new Cell()
                .Add(new Paragraph(value).SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.BLACK))
                .SetBorder(Border.NO_BORDER).SetPadding(3));
        }

        // =====================================================================
        // 5) 1/10/20/30 súhrn
        // =====================================================================
        private void AddSummary132030(Document doc, IndexModel.CalculationResult result,
            PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            doc.Add(new Paragraph("Súhrn (1 / 10 / 20 / 30 rokov)")
                .SetFont(fontBold).SetFontSize(12).SetFontColor(NEP_DARK_BLUE)
                .SetMarginBottom(6));

            var table = new Table(UnitValue.CreatePercentArray(new float[] { 40, 30, 30 }))
                .UseAllAvailableWidth();

            AddHeaderCell(table, "Obdobie", fontBold);
            AddHeaderCell(table, "Celková výroba (kWh)", fontBold);
            AddHeaderCell(table, "Celková hodnota (€)", fontBold);

            void AddRow(string label, int index)
            {
                if (result.AnnualResults.Count > index)
                {
                    var a = result.AnnualResults[index];
                    var bg = index % 2 == 0 ? PANEL_BG : ColorConstants.WHITE;

                    AddBodyCell(table, label, font, bg, TextAlignment.LEFT);
                    AddBodyCell(table, a.CumulativeProductionKwh.ToString("N0", sk), font, bg);
                    AddBodyCell(table, a.CumulativeValueEuros.ToString("N2", sk), font, bg);
                }
            }

            AddRow("Po 1. roku", 0);
            AddRow("Po 10. roku", 9);
            AddRow("Po 20. roku", 19);
            AddRow("Po 30. roku", 29);

            doc.Add(table.SetMarginBottom(12));
        }

        // =====================================================================
        // 6) Mesačný mini bar chart (bez Chart.js)
        // =====================================================================
        private void AddMiniMonthlyBarChart(Document doc, double[] monthlyProduction, PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            doc.Add(new Paragraph("Mesačný profil výroby (kWh)")
                .SetFont(fontBold).SetFontSize(12).SetFontColor(NEP_DARK_BLUE)
                .SetMarginBottom(6));

            double max = monthlyProduction.Max();
            if (max <= 0) max = 1;

            var months = new[] { "Jan", "Feb", "Mar", "Apr", "Máj", "Jún", "Júl", "Aug", "Sep", "Okt", "Nov", "Dec" };

            // “Graf” cez tabuľku 12 stĺpcov – v každom stĺpci bar s výškou podľa hodnoty
            var outer = new Div()
                .SetBackgroundColor(ColorConstants.WHITE)
                .SetBorder(new SolidBorder(GRID_BORDER, 1f))
                .SetBorderRadius(new BorderRadius(14))
                .SetPadding(12)
                .SetMarginBottom(12);

            var chart = new Table(UnitValue.CreatePercentArray(Enumerable.Repeat(1f, 12).ToArray()))
                .UseAllAvailableWidth();

            float maxBarHeight = 120f; // pt

            for (int i = 0; i < 12; i++)
            {
                var v = monthlyProduction[i];
                float h = (float)(v / max * maxBarHeight);
                if (h < 4) h = 4;

                var cell = new Cell()
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetVerticalAlignment(VerticalAlignment.BOTTOM)
                    .SetPadding(0);

                var barWrap = new Div()
                    .SetHeight(maxBarHeight + 48)   // viac priestoru dole
                    .SetPaddingBottom(10)
                    .SetWidth(UnitValue.CreatePercentValue(100))
                    .SetPaddingTop(6)
                    .SetPaddingBottom(6)
                    .SetPaddingBottom(6)
                    .SetTextAlignment(TextAlignment.CENTER);

                var bar = new Div()
                    .SetHeight(h)
                    .SetWidth(UnitValue.CreatePercentValue(70))
                    .SetBackgroundColor(NEP_BLUE)
                    .SetBorderRadius(new BorderRadius(8))
                    .SetMarginLeft(6)
                    .SetMarginRight(6);

                barWrap.Add(bar);

                barWrap.Add(new Paragraph(months[i])
                    .SetFont(fontBold).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY)
                    .SetMarginTop(6).SetMarginBottom(0));

                barWrap.Add(new Paragraph(v.ToString("N0", sk))
                    .SetFont(font).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY)
                    .SetMarginTop(2).SetMarginBottom(0));

                cell.Add(barWrap);
                chart.AddCell(cell);
            }

            outer.Add(chart);

            outer.Add(new Paragraph($"Maximum mesačnej výroby v grafe: {max.ToString("N0", sk)} kWh")
                .SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                .SetMarginTop(8).SetMarginBottom(0));

            doc.Add(outer);
        }

        private double[]? TryGetMonthlyProduction(IndexModel.CalculationResult result)
        {
            // Bezpečne cez reflexiu: aby to kompilovalo aj keď property nie je.
            try
            {
                var t = result.GetType();
                var p = t.GetProperty("MonthlyProduction") ?? t.GetProperty("MonthlyProductionKwh");
                if (p == null) return null;

                var v = p.GetValue(result);
                if (v is double[] arr) return arr;

                // niekedy môže byť List<double>
                if (v is System.Collections.IEnumerable en)
                {
                    var list = en.Cast<object>()
                        .Select(o => Convert.ToDouble(o, CultureInfo.InvariantCulture))
                        .ToArray();
                    if (list.Length == 12) return list;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        // =====================================================================
        // 7) System overview (prehľad pre klienta)
        // =====================================================================
        private void AddSystemConfigurationOverview(Document doc, IndexModel.InputParameters input,
            double installedPowerKw, double totalPanels, double totalArea,
            PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            doc.Add(new Paragraph("Prehľad systému")
                .SetFont(fontBold).SetFontSize(12).SetFontColor(NEP_DARK_BLUE)
                .SetMarginBottom(6));

            var wrap = new Div()
                .SetBackgroundColor(ColorConstants.WHITE)
                .SetBorder(new SolidBorder(GRID_BORDER, 1f))
                .SetBorderRadius(new BorderRadius(14))
                .SetPadding(12)
                .SetMarginBottom(10);

            var t = new Table(UnitValue.CreatePercentArray(new float[] { 25, 25, 25, 25 }))
                .UseAllAvailableWidth();

            AddMiniStat(t, "Panelov spolu", totalPanels.ToString("N0", sk), NEP_BLUE, font, fontBold);
            AddMiniStat(t, "Plocha FV", totalArea.ToString("N1", sk) + " m²", NEP_DARK_BLUE, font, fontBold);
            AddMiniStat(t, "Inšt. výkon", installedPowerKw.ToString("N2", sk) + " kWp", ACCENT_ORANGE, font, fontBold);
            AddMiniStat(t, "Stringy", (input.Strings?.Count ?? 0).ToString("N0", sk), ACCENT_GREEN, font, fontBold);

            wrap.Add(t);
            doc.Add(wrap);
        }

        // =====================================================================
        // 8) ÚČET – výpočet, KPI, tabuľky, mini “graf”
        // =====================================================================
        private void AddBillKpiRow(Document doc, BillSeries bill, PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            doc.Add(new Paragraph("Kľúčové čísla (účet)")
                .SetFont(fontBold).SetFontSize(12).SetFontColor(NEP_DARK_BLUE)
                .SetMarginBottom(6));

            var y1 = bill.YearIndex(0);
            var t = new Table(UnitValue.CreatePercentArray(new float[] { 25, 25, 25, 25 }))
                .UseAllAvailableWidth()
                .SetMarginBottom(10);

            AddMiniStat(t, "Účet bez FV (1. rok)", y1.billBefore.ToString("N0", sk) + " €", NEP_RED, font, fontBold);
            AddMiniStat(t, "Účet s FV (1. rok)", y1.billAfter.ToString("N0", sk) + " €", ACCENT_GREEN, font, fontBold);
            AddMiniStat(t, "Úspora (1. rok)", y1.savings.ToString("N0", sk) + " €", NEP_BLUE, font, fontBold);
            AddMiniStat(t, "Celkovo (1. rok)", y1.cumSavings.ToString("N0", sk) + " €", NEP_DARK_BLUE, font, fontBold);

            doc.Add(t);
        }

        private void AddBillMilestonesTable(Document doc, BillSeries bill, PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            doc.Add(new Paragraph("Prehľad vybraných rokov")
                .SetFont(fontBold).SetFontSize(12).SetFontColor(NEP_DARK_BLUE)
                .SetMarginBottom(6));

            var table = new Table(UnitValue.CreatePercentArray(new float[] { 16, 28, 28, 28 }))
                .UseAllAvailableWidth()
                .SetMarginBottom(10);

            AddHeaderCell(table, "Rok", fontBold);
            AddHeaderCell(table, "Účet bez FV (€)", fontBold);
            AddHeaderCell(table, "Účet s FV (€)", fontBold);
            AddHeaderCell(table, "Celková úspora na faktúre - dodávateľ (€)", fontBold);

            void AddRow(int idx)
            {
                if (idx < 0 || idx >= bill.Years.Length) return;
                var bg = idx % 2 == 0 ? PANEL_BG : ColorConstants.WHITE;

                AddBodyCell(table, bill.Years[idx].ToString(sk), font, bg);
                AddBodyCell(table, bill.BillBefore[idx].ToString("N0", sk), font, bg);
                AddBodyCell(table, bill.BillAfter[idx].ToString("N0", sk), font, bg);
                AddBodyCell(table, bill.CumulativeSavings[idx].ToString("N0", sk), font, bg);
            }

            AddRow(0);   // 1. rok
            AddRow(Math.Min(4, bill.Years.Length - 1));   // 5. rok
            AddRow(Math.Min(9, bill.Years.Length - 1));   // 10. rok
            AddRow(Math.Min(19, bill.Years.Length - 1));  // 20. rok
            AddRow(Math.Min(29, bill.Years.Length - 1));  // 30. rok

            doc.Add(table);
        }

        private void AddBillMiniComparisonBars(Document doc, BillSeries bill, PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            doc.Add(new Paragraph("Vizualizácia (bez FV vs. s FV)")
                .SetFont(fontBold).SetFontSize(12).SetFontColor(NEP_DARK_BLUE)
                .SetMarginBottom(6));

            // Použijeme 3 „milníky“ – 1. rok, 10. rok, 30. rok
            int i1 = 0;
            int i10 = Math.Min(9, bill.Years.Length - 1);
            int i30 = Math.Min(29, bill.Years.Length - 1);

            double max = new[] { bill.BillBefore[i1], bill.BillBefore[i10], bill.BillBefore[i30] }.Max();
            if (max <= 0) max = 1;

            var wrap = new Div()
                .SetBackgroundColor(ColorConstants.WHITE)
                .SetBorder(new SolidBorder(GRID_BORDER, 1f))
                .SetBorderRadius(new BorderRadius(14))
                .SetPadding(12)
                .SetMarginBottom(10);

            var t = new Table(UnitValue.CreatePercentArray(new float[] { 20, 40, 40 }))
                .UseAllAvailableWidth();

            AddHeaderCell(t, "Rok", fontBold);
            AddHeaderCell(t, "Bez FV", fontBold);
            AddHeaderCell(t, "S FV", fontBold);

            void Row(int idx)
            {
                var bg = idx % 2 == 0 ? PANEL_BG : ColorConstants.WHITE;

                t.AddCell(new Cell().SetBackgroundColor(bg).SetBorder(new SolidBorder(GRID_BORDER, 0.6f))
                    .Add(new Paragraph(bill.Years[idx].ToString(sk)).SetFont(fontBold).SetFontSize(9).SetFontColor(NEP_DARK_BLUE))
                    .SetPadding(6));

                t.AddCell(new Cell().SetBackgroundColor(bg).SetBorder(new SolidBorder(GRID_BORDER, 0.6f))
                    .Add(BarWithValue(bill.BillBefore[idx], max, NEP_RED, font, sk))
                    .SetPadding(6));

                t.AddCell(new Cell().SetBackgroundColor(bg).SetBorder(new SolidBorder(GRID_BORDER, 0.6f))
                    .Add(BarWithValue(bill.BillAfter[idx], max, ACCENT_GREEN, font, sk))
                    .SetPadding(6));
            }

            Row(i1);
            Row(i10);
            Row(i30);

            wrap.Add(t);
            doc.Add(wrap);
        }

        private IBlockElement BarWithValue(double value, double max, Color color, PdfFont font, CultureInfo sk)
        {
            var container = new Div().SetPadding(0).SetMargin(0);

            float pct = (float)(max > 0 ? (value / max) : 0);
            if (pct < 0) pct = 0;
            if (pct > 1) pct = 1;

            var barBg = new Div()
                .SetBackgroundColor(new DeviceRgb(243, 244, 246))
                .SetBorderRadius(new BorderRadius(10))
                .SetPadding(2);

            var bar = new Div()
                .SetBackgroundColor(color)
                .SetBorderRadius(new BorderRadius(8))
                .SetHeight(14)
                .SetWidth(UnitValue.CreatePercentValue(Math.Max(2, pct * 100)));

            barBg.Add(bar);
            container.Add(barBg);

            container.Add(new Paragraph(value.ToString("N0", sk) + " €")
                .SetFont(font).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY)
                .SetMarginTop(4).SetMarginBottom(0));

            return container;
        }
        
        // =====================================================================
        // 9) DETAILNÁ TABUĽKA (LANDSCAPE) – “všetko”
        // =====================================================================
        private void AddAnnualDetailsTablePortrait(Document doc,
    IndexModel.InputParameters input,
    IndexModel.CalculationResult result,
    BillSeries bill,
    PdfFont font,
    PdfFont fontBold,
    CultureInfo sk)
{
    var hasBill = bill.HasBill;

    float[] cols = hasBill
        ? new float[] { 7, 12, 12, 10, 12, 12, 11, 8 }
        : new float[] { 8, 14, 14, 12, 14, 14, 12, 12 };

    var table = new Table(UnitValue.CreatePercentArray(cols))
        .UseAllAvailableWidth();

    AddHeaderCell(table, "Rok", fontBold);
    AddHeaderCell(table, "Výroba (kWh)", fontBold);
    AddHeaderCell(table, "Celková výroba", fontBold);
    AddHeaderCell(table, "Cena €/kWh", fontBold);
    AddHeaderCell(table, "Hodnota €", fontBold);
    AddHeaderCell(table, "Celková hodnota", fontBold);
    AddHeaderCell(table, "Finančná bilancia", fontBold);
    AddHeaderCell(table, "Návratnosť", fontBold);

    bool alt = false;

    for (int i = 0; i < result.AnnualResults.Count; i++)
    {
        var a = result.AnnualResults[i];
        var bg = alt ? PANEL_BG : ColorConstants.WHITE;

        double cashFlow = i == 0 ? a.ValueEuros - input.InitialInvestment : a.ValueEuros;
        double roi = i == 0 ? 0 : (a.CumulativeValueEuros - input.InitialInvestment)
                            / input.InitialInvestment * 100.0;

        AddBodyCell(table, a.Year.ToString(sk), font, bg);
        AddBodyCell(table, a.ProductionKwh.ToString("N0", sk), font, bg);
        AddBodyCell(table, a.CumulativeProductionKwh.ToString("N0", sk), font, bg);
        AddBodyCell(table, a.PricePerKwh.ToString("N3", sk), font, bg);
        AddBodyCell(table, a.ValueEuros.ToString("N2", sk), font, bg);
        AddBodyCell(table, a.CumulativeValueEuros.ToString("N2", sk), font, bg);

        var cfCell = new Cell()
            .SetBackgroundColor(bg)
            .SetBorder(new SolidBorder(GRID_BORDER, 0.5f))
            .SetPadding(4)
            .SetTextAlignment(TextAlignment.CENTER)
            .Add(new Paragraph(cashFlow.ToString("N2", sk))
                .SetFont(font).SetFontSize(8)
                .SetFontColor(cashFlow >= 0 ? ACCENT_GREEN : NEP_RED));

        table.AddCell(cfCell);

        var roiCell = new Cell()
            .SetBackgroundColor(bg)
            .SetBorder(new SolidBorder(GRID_BORDER, 0.5f))
            .SetPadding(4)
            .SetTextAlignment(TextAlignment.CENTER)
            .Add(new Paragraph(roi.ToString("N1", sk) + " %")
                .SetFont(font).SetFontSize(8)
                .SetFontColor(roi >= 0 ? ACCENT_GREEN : NEP_RED));

        table.AddCell(roiCell);

        alt = !alt;
    }

    doc.Add(table);
}


        // =====================================================================
        // 10) STRINGY / konfigurácia
        // =====================================================================
        private void AddStringsConfiguration(Document doc, IndexModel.InputParameters input,
            PdfFont font, PdfFont fontBold, CultureInfo sk)
        {
            if (input.Strings == null || !input.Strings.Any())
            {
                AddWarningNote(doc, "Neboli zadané žiadne FV stringy.", font);
                return;
            }

            var table = new Table(UnitValue.CreatePercentArray(new float[] { 10, 14, 14, 13, 12, 12, 12, 13 }))
                .UseAllAvailableWidth()
                .SetMarginBottom(10);

            AddHeaderCell(table, "String", fontBold);
            AddHeaderCell(table, "Počet panelov", fontBold);
            AddHeaderCell(table, "Výkon panela (Wp)", fontBold);
            AddHeaderCell(table, "Účinnosť (%)", fontBold);
            AddHeaderCell(table, "Orientácia (°)", fontBold);
            AddHeaderCell(table, "Sklon (°)", fontBold);
            AddHeaderCell(table, "Plocha (m²)", fontBold);
            AddHeaderCell(table, "Výkon stringu (kWp)", fontBold);

            int idx = 1;
            bool alt = false;

            foreach (var s in input.Strings)
            {
                var bg = alt ? PANEL_BG : ColorConstants.WHITE;

                double stringKw = (s.PanelsCount * s.PanelPowerWp) / 1000.0;

                AddBodyCell(table, $"String {idx++}", font, bg, TextAlignment.LEFT);
                AddBodyCell(table, s.PanelsCount.ToString("N0", sk), font, bg);
                AddBodyCell(table, s.PanelPowerWp.ToString("N0", sk), font, bg);
                AddBodyCell(table, s.PanelEfficiency.ToString("N2", sk), font, bg);
                AddBodyCell(table, s.Orientation.ToString("N0", sk), font, bg);
                AddBodyCell(table, s.Tilt.ToString("N0", sk), font, bg);
                AddBodyCell(table, s.PanelArea.ToString("N3", sk), font, bg);
                AddBodyCell(table, stringKw.ToString("N2", sk), font, bg);

                alt = !alt;
            }

            doc.Add(table);

            AddInfoNote(doc,
                "Technická poznámka: orientácia (0°=S, 90°=V, 180°=J, 270°=Z). Sklon ovplyvňuje sezónny profil výroby. Pri viacerých stringoch sa výsledky agregujú.",
                font);
        }

        // =====================================================================
        // 11) Interpretácia – klientsky text (bez “balastu”)
        // =====================================================================
        private void AddInterpretationSection(Document doc,
            IndexModel.InputParameters input,
            IndexModel.CalculationResult result,
            string paybackText,
            double co2TonsPerYear,
            BillSeries bill,
            PdfFont font,
            PdfFont fontBold,
            CultureInfo sk)
        {
        }

        // =====================================================================
        // 12) ZÁKLADNÉ STYLES – header/body cells
        // =====================================================================
        private void AddHeaderCell(Table t, string text, PdfFont fontBold)
        {
            t.AddCell(new Cell()
                .Add(new Paragraph(text).SetFont(fontBold).SetFontSize(9).SetFontColor(ColorConstants.WHITE))
                .SetBackgroundColor(NEP_DARK_BLUE)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetPadding(5)
                .SetBorder(Border.NO_BORDER));
        }

        private void AddBodyCell(Table t, string text, PdfFont font, Color bg, TextAlignment align = TextAlignment.CENTER)
        {
            t.AddCell(new Cell()
                .Add(new Paragraph(text).SetFont(font).SetFontSize(8).SetFontColor(ColorConstants.BLACK))
                .SetBackgroundColor(bg)
                .SetTextAlignment(align)
                .SetPadding(4)
                .SetBorder(new SolidBorder(GRID_BORDER, 0.5f)));
        }

        // =====================================================================
        // 13) BILL SERIES (rovnaká logika ako FE ensureBillSeries)
        // =====================================================================
        private static double ResolveAnnualBill(IndexModel.InputParameters input)
        {
            // Ak BillMode = monthly, annual = MonthlyBill * 12, inak použij AnnualBill
            double mb = input.MonthlyBill;
            double ab = input.AnnualBill;

            var mode = (input.BillMode ?? "").Trim().ToLowerInvariant();
            if (mode == "monthly")
                return Math.Max(0, mb) * 12.0;

            // annual (default)
            return Math.Max(0, ab);
        }

        private static BillSeries ComputeBillSeriesIfAvailable(IndexModel.InputParameters input, IndexModel.CalculationResult result)
        {
            var annualBillBase = ResolveAnnualBill(input);
            if (!(annualBillBase > 0) || result?.AnnualResults == null || result.AnnualResults.Count == 0)
                return BillSeries.Empty();

            double infl = input.EnergyInflationPercent / 100.0;
            double sc = Clamp01(input.SelfConsumptionPercent / 100.0);

            int n = result.AnnualResults.Count;

            var years = new int[n];
            var before = new double[n];
            var after = new double[n];
            var savings = new double[n];
            var cumSavings = new double[n];

            double cum = 0;

            for (int i = 0; i < n; i++)
            {
                var a = result.AnnualResults[i];
                years[i] = a.Year;

                // Bill before = base * (1 + infl)^i
                double billBefore = annualBillBase * Math.Pow(1 + infl, i);

                // Potential save = valueEuros * selfConsumption
                double value = a.ValueEuros;
                double potentialSave = value * sc;

                // Savings cannot exceed billBefore
                double s = Math.Min(billBefore, potentialSave);
                double billAfter = billBefore - s;

                cum += s;

                before[i] = billBefore;
                after[i] = billAfter;
                savings[i] = s;
                cumSavings[i] = cum;
            }

            return new BillSeries(
                hasBill: true,
                years: years,
                billBefore: before,
                billAfter: after,
                savings: savings,
                cumulativeSavings: cumSavings,
                annualBillBase: annualBillBase,
                selfConsumptionPct: input.SelfConsumptionPercent,
                energyInflationPct: input.EnergyInflationPercent
            );
        }

        private static (bool has, double totalWithout, double totalWith, double totalSavings) ComputeBillTotalsOverHorizon(
            IndexModel.InputParameters input,
            IndexModel.CalculationResult result,
            BillSeries bill,
            int horizonYears)
        {
            if (!bill.HasBill || horizonYears <= 0) return (false, 0, 0, 0);

            horizonYears = Math.Min(horizonYears, bill.Years.Length);

            double totalWithout = 0;
            double totalWith = 0;

            for (int i = 0; i < horizonYears; i++)
            {
                totalWithout += bill.BillBefore[i];
                totalWith += bill.BillAfter[i];
            }

            return (true, totalWithout, totalWith, totalWithout - totalWith);
        }

        private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);

        // =====================================================================
        // 14) Helper: BillSeries struct
        // =====================================================================
        private readonly struct BillSeries
        {
            public bool HasBill { get; }
            public int[] Years { get; }
            public double[] BillBefore { get; }
            public double[] BillAfter { get; }
            public double[] Savings { get; }
            public double[] CumulativeSavings { get; }

            public double AnnualBillBase { get; }
            public double SelfConsumptionPct { get; }
            public double EnergyInflationPct { get; }

            public BillSeries(bool hasBill, int[] years, double[] billBefore, double[] billAfter, double[] savings, double[] cumulativeSavings,
                double annualBillBase, double selfConsumptionPct, double energyInflationPct)
            {
                HasBill = hasBill;
                Years = years;
                BillBefore = billBefore;
                BillAfter = billAfter;
                Savings = savings;
                CumulativeSavings = cumulativeSavings;
                AnnualBillBase = annualBillBase;
                SelfConsumptionPct = selfConsumptionPct;
                EnergyInflationPct = energyInflationPct;
            }

            public static BillSeries Empty()
                => new BillSeries(false, Array.Empty<int>(), Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), 0, 0, 0);

            public (double billBefore, double billAfter, double savings, double cumSavings) YearIndex(int idx)
            {
                if (!HasBill || idx < 0 || idx >= Years.Length) return (0, 0, 0, 0);
                return (BillBefore[idx], BillAfter[idx], Savings[idx], CumulativeSavings[idx]);
            }
        }

        // =====================================================================
        // 15) HEADER+FOOTER handler (logo fallback NEP)
        // =====================================================================
        private sealed class NepHeaderFooterHandler : IEventHandler
        {
            private readonly string _logoPath;
            private readonly PdfFont _font;
            private readonly PdfFont _fontBold;

            public NepHeaderFooterHandler(string logoPath, PdfFont font, PdfFont fontBold)
            {
                _logoPath = logoPath;
                _font = font;
                _fontBold = fontBold;
            }

            public void HandleEvent(Event currentEvent)
            {
                if (currentEvent is not PdfDocumentEvent ev) return;

                var pdf = ev.GetDocument();
                var page = ev.GetPage();
                var pageSize = page.GetPageSize();

                var canvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), pdf);

                // ── HEADER AREA
                float headerTop = pageSize.GetTop();
                float headerHeight = 70f;

                // background
                canvas.SaveState();
                canvas.SetFillColor(new DeviceRgb(248, 249, 250));
                canvas.Rectangle(pageSize.GetLeft(), headerTop - headerHeight, pageSize.GetWidth(), headerHeight);
                canvas.Fill();

                // blue line
                canvas.SetFillColor(NEP_BLUE);
                canvas.Rectangle(pageSize.GetLeft(), headerTop - headerHeight, pageSize.GetWidth(), 2f);
                canvas.Fill();
                canvas.RestoreState();

                // Logo block (left)
                float logoX = pageSize.GetLeft() + 30;
                float logoY = headerTop - 58; // inside header
                float logoW = 80;
                float logoH = 40;

                if (File.Exists(_logoPath))
                {
                    try
                    {
                        var imgData = ImageDataFactory.Create(_logoPath);

                        float imgW = imgData.GetWidth();
                        float imgH = imgData.GetHeight();
                        float ratio = imgW / imgH;

// maximálna výška loga
                        float maxH = logoH;
                        float drawH = maxH;
                        float drawW = drawH * ratio;

// ak by bolo príliš široké, obmedz šírku
                        if (drawW > logoW)
                        {
                            drawW = logoW;
                            drawH = drawW / ratio;
                        }

// centrovanie v boxe
                        float drawX = logoX + (logoW - drawW) / 2;
                        float drawY = logoY + (logoH - drawH) / 2;

                        canvas.AddImageFittedIntoRectangle(
                            imgData,
                            new Rectangle(drawX, drawY, drawW, drawH),
                            true
                        );
                    }
                    catch
                    {
                        DrawLogoFallback(canvas, logoX, logoY, logoW, logoH);
                    }
                }
                else
                {
                    DrawLogoFallback(canvas, logoX, logoY, logoW, logoH);
                }

                // Company + doc title (center/right) – text cez Canvas text
                float textLeft = logoX + 95;
                float textTop = headerTop - 24;

                canvas.BeginText();
                canvas.SetFontAndSize(_fontBold, 10);
                canvas.SetFillColor(NEP_DARK_BLUE);
                canvas.MoveText(textLeft, textTop);
                canvas.ShowText("NestStats");
                canvas.EndText();

                canvas.BeginText();
                canvas.SetFontAndSize(_font, 8.5f);
                canvas.SetFillColor(ColorConstants.DARK_GRAY);
                canvas.MoveText(textLeft, textTop - 14);
                canvas.ShowText("Energetický workspace pre návrh a monitoring FVE");
                canvas.EndText();

                // Right side title
                var rightX = pageSize.GetRight() - 30;
                canvas.BeginText();
                canvas.SetFontAndSize(_fontBold, 12);
                canvas.SetFillColor(NEP_RED);
                canvas.MoveText(rightX - 170, textTop - 2);
                canvas.ShowText("SPRÁVA ÚSPORY v1.4");
                canvas.EndText();

                // ── FOOTER AREA
                float footerBottom = pageSize.GetBottom();
                float footerHeight = 38f;

                canvas.SaveState();
                canvas.SetFillColor(new DeviceRgb(248, 249, 250));
                canvas.Rectangle(pageSize.GetLeft(), footerBottom, pageSize.GetWidth(), footerHeight);
                canvas.Fill();

                // line
                canvas.SetFillColor(NEP_BLUE);
                canvas.Rectangle(pageSize.GetLeft(), footerBottom + footerHeight - 2f, pageSize.GetWidth(), 2f);
                canvas.Fill();
                canvas.RestoreState();

                int pageNum = pdf.GetPageNumber(page);

                // left footer note
                canvas.BeginText();
                canvas.SetFontAndSize(_font, 8);
                canvas.SetFillColor(ColorConstants.DARK_GRAY);
                canvas.MoveText(pageSize.GetLeft() + 30, footerBottom + 14);
                canvas.ShowText("Informatívny výstup kalkulačky NestStats. Výsledky sú orientačné.");
                canvas.EndText();

                // right page number
                canvas.BeginText();
                canvas.SetFontAndSize(_fontBold, 8.5f);
                canvas.SetFillColor(NEP_DARK_BLUE);
                canvas.MoveText(pageSize.GetRight() - 30 - 60, footerBottom + 14);
                canvas.ShowText($"Strana {pageNum}");
                canvas.EndText();
            }

            private void DrawLogoFallback(PdfCanvas canvas, float x, float y, float w, float h)
            {
                canvas.SaveState();
                canvas.SetFillColor(NEP_BLUE);
                canvas.RoundRectangle(x, y, w, h, 6);
                canvas.Fill();
                canvas.RestoreState();

                canvas.BeginText();
                canvas.SetFontAndSize(_fontBold, 16);
                canvas.SetFillColor(ColorConstants.WHITE);
                canvas.MoveText(x + 22, y + 12);
                canvas.ShowText("NS");
                canvas.EndText();
            }
        }
    }
}
