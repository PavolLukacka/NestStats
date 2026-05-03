(() => {
    const body = document.body;
    if (!body) {
        return;
    }

    const uiState = window.nestStatsUi || { lang: "sk", locale: "sk-SK" };
    const appLocale = uiState.locale || (uiState.lang === "en" ? "en-US" : "sk-SK");
    const isEnglishUi = uiState.lang === "en";
    document.documentElement.lang = uiState.lang === "en" ? "en" : "sk";

    const textCatalog = {
        en: {
            "Platforma": "Platform",
            "Návrh FV": "PV design",
            "Návrh TČ": "HP design",
            "Manuál": "Manual",
            "Spustenie": "Launch",
            "Systémy": "Systems",
            "Nastavenia": "Settings",
            "Priradenia": "Assignments",
            "Prihlásiť sa": "Sign in",
            "Vytvoriť účet": "Create account",
            "Odhlásiť sa": "Sign out",
            "Prepnúť jazyk": "Switch language",
            "Párovanie zariadenia": "Device pairing",
            "Bezpečný zdroj telemetrie": "Secure telemetry source",
            "Batériová vrstva": "Battery layer",
            "Koordinácia úložiska": "Storage coordination",
            "Prebytkové výstupy": "Surplus outputs",
            "Wallbox a SSR pripravené": "Wallbox and SSR ready",
            "Vitajte,": "Welcome,",
            "Pripojte svoju prvú inštaláciu": "Connect your first installation",
            "Monitoring": "Monitoring",
        "Prehľad výkonu": "Power overview",
        "Denné KPI": "Daily KPIs",
        "Štatistická analýza dát": "Statistical data analysis",
        "Live vrstva": "Live layer",
        "Štatistiky": "Statistics",
            "Odporúčania": "Recommendations",
            "Večerný import": "Evening import",
            "Počasie": "Weather",
            "Trendy": "Trends",
            "Tok energie": "Energy flow",
            "Kontrola": "Control",
            "Telemetria": "Telemetry",
            "AC fázy": "AC phases",
            "Ekonomika": "Economics",
            "Dopad": "Impact",
            "Hardvér": "Hardware",
            "História": "History",
            "Zariadenia": "Devices",
            "Relé / SSR": "Relay / SSR",
            "Posledná synchronizácia": "Last synchronization",
            "Načítavam dashboard": "Loading dashboard",
            "pripravujem grafy a rozloženie": "preparing charts and layout",
            "Open-source platforma pre fotovoltiku": "Open-source platform for photovoltaics",
            "Lokálne riadenie energie": "Local energy control",
            "Prvé spustenie platformy": "First platform launch",
            "FVE online": "PV online",
            "Riadenie prebytkov": "Surplus control",
            "Pripravené na onboarding": "Ready for onboarding",
            "Transparentná logika": "Transparent logic"
        },
        sk: {
            "Platform": "Platforma",
            "PV design": "Návrh FV",
            "HP design": "Návrh TČ",
            "Manual": "Manuál",
            "Launch": "Spustenie",
            "Systems": "Systémy",
            "Settings": "Nastavenia",
            "Assignments": "Priradenia",
            "Sign in": "Prihlásiť sa",
            "Create account": "Vytvoriť účet",
            "Sign out": "Odhlásiť sa",
            "Switch language": "Prepnúť jazyk",
            "First-time launch": "Prvé spustenie",
            "Account activation": "Aktivácia účtu",
            "Turn a fresh account into a live energy workspace": "Zmeň nový účet na živý energetický workspace",
            "Platform": "Platforma",
            "Manual": "Manuál",
            "Playbooks": "Playbooky",
            "Energy software platform": "Energetická softvérová platforma",
            "NestStats product story": "Produktový príbeh NestStats",
            "Monitoring, control, economics and service in one energy operating system": "Monitoring, riadenie, ekonomika a servis v jednom energetickom operačnom systéme",
            "Real-time telemetry": "Telemetria v reálnom čase",
            "Tariff context": "Tarifný kontext",
            "Service workflows": "Servisné workflowy",
            "Open workspace": "Otvoriť workspace",
            "Read the full manual": "Otvoriť celý manuál",
            "Who this is for": "Pre koho to je",
            "Full site manual": "Plný manuál webu",
            "Knowledge center": "Centrum znalostí",
            "A practical manual for the whole NestStats site, from launch to admin operations": "Praktický manuál pre celý NestStats web od spustenia po admin operácie",
            "Back to workspace": "Späť do workspace",
            "Back to platform": "Späť na platformu",
            "Open playbooks": "Otvoriť playbooky",
            "Operations cockpit": "Operačný cockpit",
            "Platform administration": "Administrácia platformy",
            "Open assignment desk": "Otvoriť pult priradení",
            "Admin handbook": "Admin manuál",
            "Assignment desk": "Pult priradení",
            "Assisted operations": "Asistované operácie",
            "Back to admin overview": "Späť na admin prehľad",
            "Privacy": "Súkromie",
            "Trust layer": "Vrstva dôvery",
            "Privacy and data handling": "Súkromie a spracovanie dát",
            "Error": "Chyba",
            "Recovery path": "Cesta obnovy",
            "Unexpected error": "Neočakávaná chyba",
            "Return to dashboard": "Späť na dashboard",
            "Open troubleshooting manual": "Otvoriť troubleshooting manuál",
            "Sign in to NestStats": "Prihlásenie do NestStats",
            "Create an account for PV monitoring and control": "Vytvor si účet pre monitoring a riadenie FVE",
            "Open-source platform for photovoltaics": "Open-source platforma pre fotovoltiku",
            "Local energy control": "Lokálne riadenie energie",
            "First platform launch": "Prvé spustenie platformy",
            "PV online": "FVE online",
            "Surplus control": "Riadenie prebytkov",
            "Ready for onboarding": "Pripravené na onboarding",
            "Transparent logic": "Transparentná logika",
            "Turn a fresh account into a live energy workspace": "Zmeň nový účet na živý energetický workspace",
            "Pick the name the platform should use, decide if you want to connect the installation right away, and move from account creation into real monitoring, control and economics.": "Vyber názov, ktorý má platforma používať, rozhodni sa, či chceš pripojiť inštaláciu hneď, a posuň sa od vytvorenia účtu k reálnemu monitoringu, riadeniu a ekonomike.",
            "Profile identity": "Identita profilu",
            "System connection": "Pripojenie systému",
            "Economics ready": "Pripravené na ekonomiku",
            "Open full manual": "Otvoriť celý manuál",
            "See product scope": "Pozrieť rozsah produktu",
            "PV design guide": "Návrh FVE",
            "Back to overview": "Späť na úvod",
            "Launch map": "Mapa spustenia",
            "One setup pass should unlock naming, system ownership and the next economic layer.": "Jedno nastavenie má odomknúť pomenovanie, vlastníctvo systému a ďalšiu ekonomickú vrstvu.",
            "Identity": "Identita",
            "Device": "Zariadenie",
            "Savings": "Úspory",
            "User email": "Používateľský email",
            "Current phase": "Aktuálna fáza",
            "Profile naming": "Pomenovanie profilu",
            "What unlocks next": "Čo sa odomkne ďalej",
            "System linking": "Napojenie systému",
            "End result": "Koncový výsledok",
            "Operational workspace": "Operačný workspace",
            "Identity step": "Krok identity",
            "Name the workspace and choose the pace": "Pomenuj workspace a zvoľ tempo",
            "Workspace name": "Názov workspace",
            "Signed-in email": "Prihlásený email",
            "Connect a system now?": "Pripojiť systém teraz?",
            "Yes, continue straight into connection": "Áno, pokračovať rovno na pripojenie",
            "Not yet, finish the account first": "Zatiaľ nie, najprv dokončiť účet",
            "Continue launch": "Pokračovať v spustení",
            "What happens next": "Čo nasleduje",
            "Three layers that build the software experience": "Tri vrstvy, ktoré skladajú softvérový zážitok",
            "Identity becomes a workspace": "Identita sa mení na workspace",
            "Installation gets attached": "Inštalácia sa pripojí",
            "Economics starts making sense": "Ekonomika začne dávať zmysel",
            "Real-time command surface": "Riadiaca plocha v reálnom čase",
            "Local loads and surplus routing": "Lokálne záťaže a smerovanie prebytkov",
            "Tariff-aware savings view": "Pohľad na úspory podľa tarify",
            "Personalization layer": "Vrstva personalizácie",
            "Operator profile": "Profil operátora",
            "Turn raw account settings into a contextualized energy workspace": "Premieň surové nastavenia účtu na kontextový energetický workspace",
            "Named workspace": "Pomenovaný workspace",
            "Selected tariff": "Zvolená tarifa",
            "Primary system": "Primárny systém",
            "Profile readiness": "Pripravenosť profilu",
            "Connected systems": "Pripojené systémy",
            "Onboarding state": "Stav onboardingu",
            "Profile control": "Riadenie profilu",
            "Personalize the financial and operational identity": "Personalizuj finančnú a operačnú identitu",
            "Client or site label": "Názov klienta alebo lokality",
            "Exact tariff plan": "Presný tarifný plán",
            "Select a system": "Vyber systém",
            "What changes downstream?": "Čo sa tým zmení ďalej?",
            "Save profile settings": "Uložiť nastavenia profilu",
            "Account readiness map": "Mapa pripravenosti účtu",
            "Connected systems today": "Dnes pripojené systémy",
            "Why it matters": "Prečo na tom záleží",
            "Sharper economics": "Presnejšia ekonomika",
            "Navigation quality": "Kvalita navigácie",
            "Fewer dead-end flows": "Menej slepých workflowov",
            "Operational clarity": "Operačná jasnosť",
            "One system leads by default": "Jeden systém vedie predvolene",
            "Use-case playbooks": "Playbooky použitia",
            "Scenario library": "Knižnica scenárov",
            "Playbooks for onboarding, support and software rollout conversations": "Playbooky pre onboarding, support a rozhovory o nasadení softvéru",
            "Playbook intent": "Zmysel playbooku",
            "Playbook 1: New residential install": "Playbook 1: Nová rezidenčná inštalácia",
            "Playbook 2: Support rescue": "Playbook 2: Support záchrana",
            "Sales framing": "Obchodné rámcovanie",
            "Lead with operating clarity": "Začni operačnou jasnosťou",
            "Installer framing": "Rámec pre inštalatéra",
            "Reduce handoff friction": "Zníž trenie pri handoffe",
            "Support framing": "Rámec pre support",
            "Service without spreadsheet chaos": "Servis bez spreadsheet chaosu",
            "Contents": "Obsah",
            "Site map": "Mapa webu",
            "Getting started": "Začíname",
            "Page guide": "Sprievodca stránkami",
            "Roles and responsibilities": "Roly a zodpovednosti",
            "How to present NestStats": "Ako prezentovať NestStats",
            "Dashboard intelligence": "Inteligencia dashboardu",
            "System portfolio": "Portfólio systémov",
            "Financial context": "Finančný kontext",
            "Command-grade visibility": "Viditeľnosť na úrovni command centra",
            "Ready for local automation": "Pripravené na lokálnu automatizáciu",
            "Tariff-aware interpretation": "Interpretácia podľa tarify",
            "Faster first value": "Rýchlejšia prvá hodnota",
            "Service-oriented operations": "Servisne orientované operácie",
            "Product knowledge in-app": "Produktové znalosti priamo v appke",
            "How the platform feels now": "Ako platforma pôsobí teraz",
            "Why this matters commercially": "Prečo je to obchodne dôležité",
            "Need the page-by-page manual or the workflow playbook?": "Potrebuješ manuál po stránkach alebo workflow playbook?",
            "Open manual": "Otvoriť manuál"
        }
    };

    Object.assign(textCatalog.en, {
        "Predchádzajúci": "Previous",
        "Dnes": "Today",
        "Nasledujúci": "Next",
        "Systém": "System",
        "Dátum": "Date",
        "Rozsah": "Range",
        "Aktualizovať": "Refresh",
        "Denný CSV": "Daily CSV",
        "Timeline CSV": "Timeline CSV",
        "Zdroj dát": "Data source",
        "Interval vzorkovania": "Sampling interval",
        "Počet bodov v okne": "Points in window",
        "Okno": "Window",
        "Čerstvosť": "Freshness",
        "práve teraz": "just now",
        "Inštalovaný výkon FV": "Installed PV capacity",
        "Energia v čase": "Energy over time",
        "Prehľad výkonu": "Power overview",
        "náhľad": "preview",
        "stav nabitia": "state of charge",
        "Teplota": "Temperature",
        "Oblačnosť": "Cloud cover",
        "Vietor": "Wind",
        "Zrážky": "Precipitation",
        "FV odhad": "PV estimate",
        "Denné KPI pre": "Daily KPIs for",
        "Odvodené priamo zo záznamu vybraného dňa · z dátovej vrstvy": "Derived directly from the selected day record · from the data layer",
        "FV výroba": "PV generation",
        "Spotreba domu": "Home consumption",
        "Import zo siete": "Grid import",
        "Podiel spotreby zo siete": "Share of consumption from the grid",
        "Export do siete": "Grid export",
        "výnos": "revenue",
        "Bilancia dňa": "Daily balance",
        "Export mínus import": "Export minus import",
        "Sebestačnosť dňa": "Daily self-sufficiency",
        "Vlastná spotreba": "Self-consumption",
        "Index kvality": "Quality index",
        "Výkonnostný index": "Performance index",
        "Dostupnosť telemetrie": "Telemetry availability",
        "Stabilita siete": "Grid stability",
        "Kvalita dát": "Data quality",
        "Dostupnosť": "Availability",
        "Stabilita": "Stability",
        "Kvalita": "Quality",
        "ukazuje, ako sa systému darí v porovnaní s typickým dňom v tomto období.": "shows how the system is doing compared with a typical day in this season.",
        "hovorí, nakoľko aktuálne sú prijaté dáta.": "reflects how current the incoming data is.",
        "vyjadruje, do akej miery domácnosť funguje plynulo bez potreby siete.": "shows how smoothly the household operates without needing the grid.",
        "zohľadňuje, ako spoľahlivé a kompletné sú podklady pre vyhodnotenie.": "reflects how reliable and complete the evaluation inputs are.",
        "Výkonnostný index ukazuje, ako sa systému darí v porovnaní s typickým dňom v tomto období. Dostupnosť hovorí, nakoľko aktuálne sú prijaté dáta. Stabilita vyjadruje, do akej miery domácnosť funguje plynulo bez potreby siete. Kvalita zohľadňuje, ako spoľahlivé a kompletné sú podklady pre vyhodnotenie.": "The performance index shows how the system is doing compared with a typical day in this season. Availability reflects how current the incoming data is. Grid stability shows how smoothly the household operates without needing the grid. Data quality reflects how reliable and complete the evaluation inputs are.",
        "Okamžité hodnoty (kW)": "Instant values (kW)",
        "FV pole": "PV array",
        "Menič AC": "Inverter AC",
        "Dom": "Home",
        "Pohotovosť": "Standby",
        "Batéria SOC": "Battery SOC",
        "PV saturácia": "PV saturation",
        "saturácia": "saturation",
        "WattRouter util": "WattRouter utilization",
        "kap.": "cap.",
        "SSR priemer": "SSR average",
        "Energetický rozpočet": "Energy budget",
        "Alokácia kWh": "kWh allocation",
        "Výroba": "Generation",
        "Spotreba": "Consumption",
        "Celková výroba vs spotreba": "Total generation vs consumption",
        "Štatistická analýza dát": "Statistical data analysis",
        "FV výkon (kW)": "PV output (kW)",
        "Priemer": "Average",
        "Percentil 90": "90th percentile",
        "Rozptyl": "Standard deviation",
        "Priemer SOC": "Average SOC",
        "Cykly dnes": "Cycles today",
        "Závislosť": "Dependency",
        "Bilancia": "Balance",
        "Full-load hodín": "Full-load hours",
        "kWh/kWp za deň": "kWh/kWp per day",
        "Bat. throughput": "Battery throughput",
        "cykly": "cycles",
        "Export prognóza / mes.": "Export forecast / month",
        "ročne": "annually",
        "Inteligentné riadenie": "Smart control",
        "Prediktívna vrstva": "Predictive layer",
        "Zajtrajší odhad a miera istoty": "Tomorrow's estimate and confidence",
        "automatický odhad": "automatic estimate",
        "FV zajtra": "PV tomorrow",
        "odhad z posledných dní": "estimate from recent days",
        "očakávaný denný odber domácnosti": "expected daily household demand",
        "Riziko odberu zo siete": "Risk of grid import",
        "priestor pre flexibilnú záťaž": "room for flexible load",
        "Prevádzkový dohľad": "Operational oversight",
        "Anomálie a odporúčané kroky": "Anomalies and recommended actions",
        "signálov": "signals",
        "Batéria sa zohrieva": "Battery is warming up",
        "Teplota batérie je zvýšená oproti komfortnému prevádzkovému pásmu.": "Battery temperature is elevated above the comfortable operating range.",
        "Nevyužitý FV prebytok": "Unused PV surplus",
        "Dlhšie exportné okno naznačuje priestor pre ohrev vody, nabíjanie EV alebo inú stratégiu ukladania.": "A longer export window indicates room for water heating, EV charging, or another storage strategy.",
        "Akčné odporúčania": "Action recommendations",
        "Čo sa oplatí urobiť práve teraz": "What is worth doing right now",
        "odporúčaní": "recommendations",
        "Prevádzkový briefing": "Operational briefing",
        "Táto vrstva prekladá výrobu, import, export, stav batérie aj večerné riziko do konkrétnych krokov, ktoré pomáhajú zvýšiť vlastnú spotrebu a znížiť závislosť od siete.": "This layer translates generation, import, export, battery status, and evening risk into concrete actions that help increase self-consumption and reduce reliance on the grid.",
        "Export dnes": "Export today",
        "Import dnes": "Import today",
        "Sledujte ďalší cyklus": "Watch the next cycle",
        "Momentálne sa neukazuje potreba silného zásahu. Ďalšiu presnosť prinesie viac histórie pre budúcu predikciu.": "There is currently no need for a strong intervention. More history will improve future prediction accuracy.",
        "Nízke riziko": "Low risk",
        "Predikcia večerného importu": "Evening import prediction",
        "Prediktívna energetika": "Predictive energy",
        "Pravdepodobnosť importu": "Import probability",
        "Spotreba vecer": "Evening consumption",
        "model z telemetrie a dennej historie": "model from telemetry and daily history",
        "FV este do vecera": "PV before evening",
        "SOC na vecer": "SOC for the evening",
        "Odhad importu": "Estimated import",
        "kolko moze chybat po FV a baterii": "how much may still be missing after PV and battery",
        "Trh a príbeh dňa": "Market and story of the day",
        "SK spot ceny": "SK spot prices",
        "Trh dnes a zajtra": "Market today and tomorrow",
        "Aktuálny interval": "Current interval",
        "24 h priemer": "24h average",
        "trhová komoditná zložka": "market commodity component",
        "Prvá hodina": "First hour",
        "Spot ukazuje trhovu komoditnu cenu pre SK obchodnu oblast. Koncova cena pre odber v SR sa este sklada aj z distribucnych a dalsich regulovanych poloziek. Zdroj OKTE.": "Spot shows the market commodity price for the SK trading area. The final retail price in Slovakia also includes distribution and other regulated components. Source: OKTE.",
        "Denný energetický príbeh": "Daily energy story",
        "Dnes a včera v jednej vrstve": "Today and yesterday in one layer",
        "klientsky sumár": "client summary",
        "Sieť musela doplniť časť spotreby": "The grid had to cover part of the consumption",
        "Počasie a PV odhad": "Weather and PV estimate",
        "Počasie podľa adresy": "Weather by address",
        "Aktuálne podmienky": "Current conditions",
        "Slnecno": "Sunny",
        "Zamracene": "Overcast",
        "Zamračené": "Overcast",
        "Oblacno": "Cloudy",
        "Oblačno": "Cloudy",
        "vplyv na výkon panelov": "impact on panel output",
        "10 m nad zemou": "10 m above ground",
        "aktuálna hodina": "current hour",
        "Slnko": "Sun",
        "Odhadovaná krivka výroby -": "Estimated generation curve -",
        "Špička": "Peak",
        "Denný odhad": "Daily estimate",
        "Inštalácia": "Installation",
        "Deň": "Day",
        "Solárna produkcia": "Solar production",
        "FV výkon a saturácia": "PV output and saturation",
        "Aktuálne": "Current",
        "živý FV výkon v aktuálnom okne": "live PV output in the current window",
        "najsilnejší bod zachytený v timeline": "strongest point captured in the timeline",
        "Saturácia FV": "PV saturation",
        "Import / export": "Import / export",
        "Smer a objem siete": "Grid direction and volume",
        "Aktuálny stav": "Current status",
        "Export voči distribučnej sieti": "Export to the distribution grid",
        "Celková výroba": "Total generation",
        "Mix spotreby": "Consumption mix",
        "Zdroje krytia spotreby": "Sources covering consumption",
        "Batéria a autonómia": "Battery and autonomy",
        "SOC a pokrytie spotreby": "SOC and consumption coverage",
        "Aktuálne prúdenie": "Current flow",
        "Menič / inverter": "Inverter",
        "DC → AC konverzia": "DC to AC conversion",
        "Domácnosť": "Household",
        "okamžitá záťaž": "instant load",
        "Pohotovosť": "Standby",
        "Nabíjanie": "Charging",
        "Vybíjanie": "Discharging",
        "Aktuálna záťaž": "Current load",
        "Celková spotreba": "Total consumption",
        "Distribučná sieť": "Distribution grid",
        "vyťaženie": "utilization",
        "Telemetria FV": "PV telemetry",
        "MPPT výkon": "MPPT power",
        "MPPT napätie a prúd": "MPPT voltage and current",
        "Výkon, relé a vyťaženie": "Power, relays, and utilization",
        "Inteligentné riadenie": "Smart control",
        "Prediktívna vrstva": "Predictive layer",
        "Zajtrajší odhad a miera istoty": "Tomorrow's estimate and confidence",
        "Prevádzkový dohľad": "Operational oversight",
        "Anomálie a odporúčané kroky": "Anomalies and recommended actions",
        "Trh a príbeh dňa": "Market and story of the day",
        "SK spot ceny": "SK spot prices",
        "Denný energetický príbeh": "Daily energy story",
        "Dnes a včera v jednej vrstve": "Today and yesterday in one layer",
        "Počasie a PV odhad": "Weather and PV estimate",
        "Počasie podľa adresy": "Weather by address",
        "Aktuálne podmienky": "Current conditions",
        "Polooblacno": "Partly cloudy",
        "Slnecno": "Sunny",
        "Odhad dnes": "Today's estimate",
        "spicka asi": "peak around",
        "instalacie": "installation",
        "Batéria sa zohrieva": "Battery is warming up",
        "Nerovnováha MPPT": "MPPT imbalance",
        "Jeden string vyrába citeľne menej než druhý aj keď je FV výroba aktívna.": "One string is producing noticeably less than the other even though PV generation is active.",
        "Rozdiel MPPT:": "MPPT difference:",
        "Vecer vyzera bezpecne bez vyznamneho importu. Bateria a zvysna FV vyroba by mali pokryt beznu spotrebu.": "The evening looks safe without significant import. The battery and remaining PV generation should cover normal consumption.",
        "pouzitelne": "usable",
        "nad rezervou": "above reserve",
        "potencial pred 17:00 na dobitie baterie": "potential before 17:00 to recharge the battery",
        "z dátovej vrstvy": "from the data layer",
        "bez dát": "no data",
        "Saturácia": "Saturation",
        "teraz": "now",
        "času bolo čerpanie zo siete": "of the time power was imported from the grid",
        "času tiekol prebytok von": "of the time surplus was flowing out",
        "Tok energie — live": "Energy flow — live",
        "Napätie 1 (V)": "Voltage 1 (V)",
        "Napätie 2 (V)": "Voltage 2 (V)",
        "Prúd 1 (A)": "Current 1 (A)",
        "Prúd 2 (A)": "Current 2 (A)",
        "Dnes systém vyrobil": "Today the system generated",
        "a domácnosť spotrebovala": "and the household consumed",
        "Zo siete bolo potrebné dokúpiť o": "It was necessary to buy",
        "viac, než sa vyexportovalo.": "more from the grid than was exported.",
        "Oblacnost bude hlavny limit.": "Cloud cover will be the main limiting factor."
    });

    Object.assign(textCatalog.en, {
        "Telemetria batérie": "Battery telemetry",
        "DC napätie, prúd, SOC": "DC voltage, current, SOC",
        "Kvalita AC siete": "AC grid quality",
        "Frekvencia a výkon": "Frequency and power",
        "AC fázy": "AC phases",
        "Symetria a odchylka napätia": "Voltage symmetry and deviation",
        "Napätie R / S / T": "Voltage R / S / T",
        "Teplota, SOH a cykly": "Temperature, SOH, and cycles",
        "AC fázy — detail": "AC phases — detail",
        "Prúdy R/S/T (výstup vs PCC)": "Currents R/S/T (output vs PCC)",
        "Výkon R/S/T (výstup vs PCC)": "Power R/S/T (output vs PCC)",
        "Forecast days": "Forecast days",
        "Hourly weather forecast": "Hourly weather forecast",
        "PV forecast": "PV forecast"
    });

    const prefixCatalog = {
        en: [
            ["Dnes ·", "Today ·"],
            ["Zajtra ·", "Tomorrow ·"],
            ["Prehľad výkonu —", "Power overview —"],
            ["Denné KPI pre", "Daily KPIs for"],
            ["Alokácia kWh —", "kWh allocation —"],
            ["Odvodené priamo zo záznamu vybraného dňa", "Derived directly from the selected day record"],
            ["Teplota batérie:", "Battery temperature:"],
            ["Export špička:", "Export peak:"],
            ["Očakávaný import približne", "Expected import approximately"],
            ["Odhad SOC na začiatku večera:", "Estimated SOC at the start of the evening:"],
            ["Spotreba vecer", "Evening consumption"],
            ["FV este do vecera", "PV before evening"],
            ["SOC na vecer", "SOC for the evening"],
            ["Odhad importu", "Estimated import"],
            ["TERAZ -", "NOW -"],
            ["Odhadovaná krivka výroby -", "Estimated generation curve -"]
        ]
    };

    const replacementEntries = Object.fromEntries(
        Object.entries(textCatalog).map(([lang, dictionary]) => [
            lang,
            Object.keys(dictionary).sort((a, b) => b.length - a.length)
        ])
    );

    const attributeKeys = ["placeholder", "aria-label", "title"];
    const escapeRegExp = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

    const translateInlineText = (value, dictionary) => {
        if (!value || !dictionary) {
            return value;
        }

        const trimmed = value.trim();
        if (!trimmed) {
            return value;
        }

        if (Object.prototype.hasOwnProperty.call(dictionary, trimmed)) {
            return value.replace(trimmed, dictionary[trimmed]);
        }

        if (trimmed.startsWith("Vitajte,") && dictionary["Vitajte,"]) {
            return value.replace("Vitajte,", dictionary["Vitajte,"]);
        }

        const prefixes = prefixCatalog[uiState.lang] || [];
        for (const [sourcePrefix, targetPrefix] of prefixes) {
            if (trimmed.startsWith(sourcePrefix)) {
                return value.replace(sourcePrefix, targetPrefix);
            }
        }

        const replacementKeys = replacementEntries[uiState.lang] || [];
        let replacedValue = value;
        let changed = false;
        for (const source of replacementKeys) {
            if (!source || !replacedValue.includes(source)) {
                continue;
            }

            const pattern = new RegExp(`(^|[^\\p{L}\\p{N}])(${escapeRegExp(source)})(?=$|[^\\p{L}\\p{N}])`, "gu");
            const nextValue = replacedValue.replace(pattern, (_, prefix) => `${prefix}${dictionary[source]}`);
            if (nextValue !== replacedValue) {
                replacedValue = nextValue;
                changed = true;
            }
        }

        if (changed) {
            return replacedValue;
        }

        return value;
    };

    const applyStaticTranslations = () => {
        const dictionary = textCatalog[uiState.lang];
        if (!dictionary) {
            return;
        }

        const walker = document.createTreeWalker(body, NodeFilter.SHOW_TEXT, {
            acceptNode(node) {
                const parentTag = node.parentElement?.tagName;
                if (!node.textContent?.trim() ||
                    parentTag === "SCRIPT" ||
                    parentTag === "STYLE" ||
                    parentTag === "NOSCRIPT" ||
                    node.parentElement?.closest("[data-no-translate]")) {
                    return NodeFilter.FILTER_REJECT;
                }

                return NodeFilter.FILTER_ACCEPT;
            }
        });

        const textNodes = [];
        while (walker.nextNode()) {
            textNodes.push(walker.currentNode);
        }

        textNodes.forEach((node) => {
            const translated = translateInlineText(node.textContent, dictionary);
            if (translated && translated !== node.textContent) {
                node.textContent = translated;
            }
        });

        document.querySelectorAll(attributeKeys.map((key) => `[${key}]`).join(",")).forEach((element) => {
            attributeKeys.forEach((key) => {
                const currentValue = element.getAttribute(key);
                const translated = translateInlineText(currentValue, dictionary);
                if (translated && translated !== currentValue) {
                    element.setAttribute(key, translated);
                }
            });
        });

        document.title = translateInlineText(document.title, dictionary);
    };

    const applyDashboardEnglishOverlay = () => {
        if (uiState.lang !== "en") {
            return;
        }

        const literalMap = new Map([
            ["Predchádzajúci", "Previous"],
            ["Dnes", "Today"],
            ["Nasledujúci", "Next"],
            ["Systém", "System"],
            ["Dátum", "Date"],
            ["Rozsah", "Range"],
            ["Aktualizovať", "Refresh"],
            ["Denný CSV", "Daily CSV"],
            ["Timeline CSV", "Timeline CSV"],
            ["Zdroj dát", "Data source"],
            ["Interval vzorkovania", "Sampling interval"],
            ["Počet bodov v okne", "Points in window"],
            ["Okno", "Window"],
            ["Čerstvosť", "Freshness"],
            ["práve teraz", "just now"],
            ["Inštalovaný výkon FV", "Installed PV capacity"],
            ["Energia v čase", "Energy over time"],
            ["Prehľad výkonu", "Power overview"],
            ["Spotreba", "Consumption"],
            ["Sieť", "Grid"],
            ["Batéria", "Battery"],
            ["Počasie", "Weather"],
            ["Teplota", "Temperature"],
            ["Oblačnosť", "Cloud cover"],
            ["Vietor", "Wind"],
            ["Zrážky", "Precipitation"],
            ["FV odhad", "PV estimate"],
            ["Denné KPI pre", "Daily KPIs for"],
            ["Odvodené priamo zo záznamu vybraného dňa", "Derived directly from the selected day record"],
            ["z dátovej vrstvy", "from the data layer"],
            ["bez dát", "no data"],
            ["FV výroba", "PV generation"],
            ["Spotreba domu", "Home consumption"],
            ["Import zo siete", "Grid import"],
            ["Podiel spotreby zo siete", "Share of consumption from the grid"],
            ["Export do siete", "Grid export"],
            ["výnos", "revenue"],
            ["Bilancia dňa", "Daily balance"],
            ["Export mínus import", "Export minus import"],
            ["Sebestačnosť dňa", "Daily self-sufficiency"],
            ["Vlastná spotreba", "Self-consumption"],
            ["Index kvality", "Quality index"],
            ["Výkonnostný index", "Performance index"],
            ["Dostupnosť telemetrie", "Telemetry availability"],
            ["Stabilita siete", "Grid stability"],
            ["Kvalita dát", "Data quality"],
            ["Live snapshot", "Live snapshot"],
            ["Okamžité hodnoty (kW)", "Instant values (kW)"],
            ["FV pole", "PV array"],
            ["Menič AC", "Inverter AC"],
            ["Menič / inverter", "Inverter"],
            ["Dom", "Home"],
            ["Domácnosť", "Household"],
            ["Distribučná sieť", "Grid connection"],
            ["Pohotovosť", "Standby"],
            ["Batéria SOC", "Battery SOC"],
            ["PV saturácia", "PV saturation"],
            ["saturácia", "saturation"],
            ["WattRouter util", "WattRouter utilization"],
            ["SSR priemer", "SSR average"],
            ["Energetický rozpočet", "Energy budget"],
            ["Alokácia kWh", "kWh allocation"],
            ["Výroba", "Generation"],
            ["Celková výroba vs spotreba", "Total generation vs consumption"],
            ["Štatistická analýza dát", "Statistical data analysis"],
            ["FV výkon (kW)", "PV output (kW)"],
            ["Minimum", "Minimum"],
            ["Priemer", "Average"],
            ["Maximum", "Maximum"],
            ["Percentil 90", "90th percentile"],
            ["Rozptyl", "Standard deviation"],
            ["Batéria SOC (%)", "Battery SOC (%)"],
            ["Min SOC", "Min SOC"],
            ["Priemer SOC", "Average SOC"],
            ["Max SOC", "Max SOC"],
            ["Cykly dnes", "Cycles today"],
            ["Sieť (kW)", "Grid (kW)"],
            ["Max. import", "Max import"],
            ["Max. export", "Max export"],
            ["Závislosť", "Dependency"],
            ["Bilancia", "Balance"],
            ["Full-load hodín", "Full-load hours"],
            ["pri", "at"],
            ["kWh/kWp za deň", "kWh/kWp per day"],
            ["Spec. yield", "Spec. yield"],
            ["Bat. throughput", "Battery throughput"],
            ["cykly", "cycles"],
            ["Export prognóza / mes.", "Export forecast / month"],
            ["ročne", "annually"],
            ["Inteligentné riadenie", "Smart control"],
            ["Prediktívna vrstva", "Predictive layer"],
            ["Zajtrajší odhad a miera istoty", "Tomorrow's estimate and confidence"],
            ["automatický odhad", "automatic estimate"],
            ["Očakávaný import približne", "Expected import around"],
            ["FV zajtra", "PV tomorrow"],
            ["odhad z posledných dní", "estimate from recent days"],
            ["očakávaný denný odber domácnosti", "expected daily household demand"],
            ["Import", "Import"],
            ["Riziko odberu zo siete", "Risk of grid import"],
            ["priestor pre flexibilnú záťaž", "room for flexible load"],
            ["Prevádzkový dohľad", "Operational oversight"],
            ["Anomálie a odporúčané kroky", "Anomalies and recommended actions"],
            ["Batéria sa zohrieva", "Battery is warming up"],
            ["Teplota batérie je zvýšená oproti komfortnému prevádzkovému pásmu.", "Battery temperature is elevated above the comfortable operating range."],
            ["Nerovnováha MPPT", "MPPT imbalance"],
            ["Jeden string vyrába citeľne menej než druhý aj keď je FV výroba aktívna.", "One string is producing significantly less than the other even though PV production is active."],
            ["Nevyužitý FV prebytok", "Unused PV surplus"],
            ["Dlhšie exportné okno naznačuje priestor pre ohrev vody, nabíjanie EV alebo inú stratégiu ukladania.", "A longer export window indicates room for water heating, EV charging, or another storage strategy."],
            ["Akčné odporúčania", "Action recommendations"],
            ["Čo sa oplatí urobiť práve teraz", "What is worth doing right now"],
            ["Prevádzkový briefing", "Operational briefing"],
            ["Táto vrstva prekladá výrobu, import, export, stav batérie aj večerné riziko do konkrétnych krokov, ktoré pomáhajú zvýšiť vlastnú spotrebu a znížiť závislosť od siete.", "This layer translates generation, import, export, battery status, and evening risk into concrete steps that help increase self-consumption and reduce grid dependency."],
            ["Sledujte ďalší cyklus", "Watch the next cycle"],
            ["Momentálne sa neukazuje potreba silného zásahu. Ďalšiu presnosť prinesie viac histórie pre budúcu predikciu.", "There is no strong need for intervention right now. More history will improve future prediction accuracy."],
            ["Nízke riziko", "Low risk"],
            ["Predikcia večerného importu", "Evening import forecast"],
            ["Prediktívna energetika", "Predictive energy"],
            ["Riziko večerného odberu zo siete", "Risk of evening grid import"],
            ["Pravdepodobnosť importu", "Import probability"],
            ["Večer vyzerá bezpečne bez významného importu. Batéria a zvyšná FV výroba by mali pokryť bežnú spotrebu.", "The evening looks safe without significant import. The battery and remaining PV output should cover normal consumption."],
            ["Vecer vyzera bezpecne bez vyznamneho importu. Bateria a zvysna FV vyroba by mali pokryt beznu spotrebu.", "The evening looks safe without significant import. The battery and remaining PV output should cover normal consumption."],
            ["Odhad SOC na začiatku večera", "Estimated SOC at the start of the evening"],
            ["Spotreba vecer", "Evening consumption"],
            ["model z telemetrie a dennej historie", "model from telemetry and daily history"],
            ["FV este do vecera", "PV before evening"],
            ["potencial pred 17:00 na dobitie baterie", "potential before 17:00 to charge the battery"],
            ["SOC na vecer", "SOC for the evening"],
            ["pouzitelne", "usable"],
            ["nad rezervou", "above reserve"],
            ["Odhad importu", "Estimated import"],
            ["kolko moze chybat po FV a baterii", "how much may still be missing after PV and battery"],
            ["Trh a príbeh dňa", "Market and story of the day"],
            ["SK spot ceny", "SK spot prices"],
            ["Trh dnes a zajtra", "Market today and tomorrow"],
            ["Aktuálny interval", "Current interval"],
            ["24 h priemer", "24 h average"],
            ["trhová komoditná zložka", "market commodity component"],
            ["Prvá hodina", "First hour"],
            ["Spot ukazuje trhovu komoditnu cenu pre SK obchodnu oblast. Koncova cena pre odber v SR sa este sklada aj z distribucnych a dalsich regulovanych poloziek. Zdroj OKTE.", "Spot shows the market commodity price for the SK bidding zone. The final retail price in Slovakia also includes distribution and other regulated components. Source: OKTE."],
            ["Spot ukazuje trhovú komoditnú cenu pre SK obchodnú oblasť. Koncová cena pre odber v SR sa ešte skladá aj z distribučných a ďalších regulovaných položiek. Zdroj OKTE.", "Spot shows the market commodity price for the SK bidding zone. The final retail price in Slovakia also includes distribution and other regulated components. Source: OKTE."],
            ["Denný energetický príbeh", "Daily energy story"],
            ["Dnes a včera v jednej vrstve", "Today and yesterday in one layer"],
            ["klientsky sumár", "client summary"],
            ["Sieť musela doplniť časť spotreby", "The grid had to cover part of the consumption"],
            ["Počasie podľa adresy", "Weather by address"],
            ["Aktuálne podmienky", "Current conditions"],
            ["Slnecno", "Sunny"],
            ["Polooblacno", "Partly cloudy"],
            ["Zamracene", "Overcast"],
            ["Zamračené", "Overcast"],
            ["Oblacno", "Cloudy"],
            ["Oblačno", "Cloudy"],
            ["Odhadovaná krivka výroby dnes", "Estimated generation curve today"],
            ["Špička", "Peak"],
            ["Denný odhad", "Daily estimate"],
            ["Inštalácia", "Installation"],
            ["Deň", "Day"],
            ["Solárna produkcia", "Solar production"],
            ["FV výkon a saturácia", "PV output and saturation"],
            ["Aktuálne", "Current"],
            ["živý FV výkon v aktuálnom okne", "live PV output in the current window"],
            ["najsilnejší bod zachytený v timeline", "strongest point captured in the timeline"],
            ["Saturácia FV", "PV saturation"],
            ["teraz", "now"],
            ["Import / export", "Import / export"],
            ["Smer a objem siete", "Grid direction and volume"],
            ["Aktuálny stav", "Current state"],
            ["Export voči distribučnej sieti", "Export to the distribution grid"],
            ["času bolo čerpanie zo siete", "of the time the household was drawing from the grid"],
            ["času tiekol prebytok von", "of the time surplus was flowing out"],
            ["Mix spotreby", "Consumption mix"],
            ["Zdroje krytia spotreby", "Sources covering consumption"],
            ["Batéria a autonómia", "Battery and autonomy"],
            ["SOC a pokrytie spotreby", "SOC and consumption coverage"],
            ["Tok energie — live", "Energy flow — live"],
            ["Aktuálne prúdenie", "Current flow"],
            ["Celková spotreba", "Total consumption"],
            ["DC → AC konverzia", "DC → AC conversion"],
            ["vyťaženie", "utilization"],
            ["Telemetria FV", "PV telemetry"],
            ["MPPT výkon", "MPPT power"],
            ["MPPT napätie a prúd", "MPPT voltage and current"],
            ["Napätie 1 (V)", "Voltage 1 (V)"],
            ["Napätie 2 (V)", "Voltage 2 (V)"],
            ["Prúd 1 (A)", "Current 1 (A)"],
            ["Prúd 2 (A)", "Current 2 (A)"],
            ["Telemetria batérie", "Battery telemetry"],
            ["DC napätie, prúd, SOC", "DC voltage, current, SOC"],
            ["Kvalita AC siete", "AC grid quality"],
            ["Frekvencia a výkon", "Frequency and power"],
            ["AC fázy", "AC phases"],
            ["Symetria a odchylka napätia", "Voltage symmetry and deviation"],
            ["Prúdy R/S/T (výstup vs PCC)", "Currents R/S/T (output vs PCC)"],
            ["Výkon R/S/T (výstup vs PCC)", "Power R/S/T (output vs PCC)"],
            ["Ekonomika", "Economics"],
            ["Tarifa, export a cisty prinos", "Tariff, export, and net benefit"],
            ["Tarifa, export a čistý prínos", "Tariff, export, and net benefit"],
            ["auto vyber", "auto selection"],
            ["Aktivny benchmark", "Active benchmark"],
            ["Aktívny benchmark", "Active benchmark"],
            ["Mesacny sucet", "Monthly sum"],
            ["Mesačný súčet", "Monthly sum"],
            ["Dodavatel a plan sa prepina automaticky v ramci vybraneho benchmarku, takze indikator sekcie uz korektne ukazuje aktivnu ekonomicku vrstvu.", "The supplier and plan switch automatically within the selected benchmark, so the section indicator now correctly shows the active economics layer."],
            ["Dodávateľ a plán sa prepína automaticky v rámci vybraného benchmarku, takže indikátor sekcie už korektne ukazuje aktívnu ekonomickú vrstvu.", "The supplier and plan switch automatically within the selected benchmark, so the section indicator now correctly shows the active economics layer."],
            ["Porovnanie úspor", "Savings comparison"],
            ["Porovnanie plánov a sadzieb", "Plan and tariff comparison"],
            ["live benchmark", "live benchmark"],
            ["Environmentálny dopad", "Environmental impact"],
            ["CO₂, uhlie a stromy", "CO2, coal, and trees"],
            ["deň / mesiac / rok / celkovo", "day / month / year / total"],
            ["CO₂ dnes", "CO2 today"],
            ["aktuálny deň", "current day"],
            ["CO₂ mesiac", "CO2 month"],
            ["posledných 30 dní", "last 30 days"],
            ["CO₂ rok", "CO2 year"],
            ["ročný benchmark", "annual benchmark"],
            ["CO₂ lifetime", "CO2 lifetime"],
            ["od štartu systému", "since system start"],
            ["Uhlie dnes", "Coal today"],
            ["ekvivalent uhlia", "coal equivalent"],
            ["Uhlie rok", "Coal year"],
            ["ročný ekvivalent", "annual equivalent"],
            ["Ekologický trend", "Environmental trend"],
            ["CO₂ trend", "CO2 trend"],
            ["Denné výsledky", "Daily results"],
            ["História posledných dní", "History of recent days"],
            ["Všetko", "All"],
            ["Historické dáta zatiaľ nie sú dostupné.", "Historical data is not available yet."],
            ["Štatistiky obdobia", "Period statistics"],
            ["Agregáty a trendy", "Aggregates and trends"],
            ["všetky dáta", "all data"],
            ["FV spolu", "PV total"],
            ["Spotreba spolu", "Consumption total"],
            ["Import spolu", "Import total"],
            ["Export spolu", "Export total"],
            ["Priem. sebestačnosť", "Avg. self-sufficiency"],
            ["Priem. vlastná spotr.", "Avg. self-consumption"],
            ["Najlepší deň", "Best day"],
            ["Prebytok vs. deficit", "Surplus vs deficit"],
            ["Prebytok vs deficit", "Surplus vs deficit"],
            ["Export pokrýva", "Export covers"],
            ["spotreby", "consumption"],
            ["Sieťová závislosť", "Grid dependency"],
            ["Rozšírená analytika", "Advanced analytics"],
            ["Batéria v čase", "Battery over time"],
            ["Nabíjanie a vybíjanie po dňoch", "Charging and discharging by day"],
            ["Využitie FV", "PV utilization"],
            ["Kam smerovala energia z výroby", "Where generated energy was directed"],
            ["História autonómie", "Autonomy history"],
            ["Sebestačnosť a vlastná spotreba", "Self-sufficiency and self-consumption"],
            ["Hardvér", "Hardware"],
            ["Prehľad zariadení", "Device overview"],
            ["zariadení", "devices"],
            ["Fotovoltický menič", "Photovoltaic inverter"],
            ["Solárny invertor", "Solar inverter"],
            ["Online", "Online"],
            ["Oneskorenie", "Delayed"],
            ["Batériové úložisko", "Battery storage"],
            ["BMS / akumulátor", "BMS / accumulator"],
            ["Aktívna", "Active"],
            ["Nepripojená", "Disconnected"],
            ["Riadenie prebytkov", "Surplus control"],
            ["Aktívny", "Active"],
            ["Smart meter", "Smart meter"],
            ["Inteligentný elektromer", "Smart electricity meter"],
            ["Nepripojený", "Disconnected"],
            ["Externá batéria — BMS", "External battery — BMS"],
            ["Wallbox / EVSE", "Wallbox / EVSE"],
            ["EV nabíjacia stanica", "EV charging station"],
            ["Relé aktívne", "Active relays"],
            ["Kanály", "Channels"],
            ["SSR / Relé kanály", "SSR / relay channels"],
            ["Aktívne výstupné kanály", "Active output channels"],
            ["aktívnych", "active"],
            ["Relé dáta nie sú dostupné. Skontrolujte pripojenie WattRouter modulu.", "Relay data is not available. Check the WattRouter module connection."],
            ["Žiadne relé dáta.", "No relay data."],
            ["Kanály — detail", "Channels — detail"],
            ["Výkon a záťaž", "Power and load"],
            ["Aktívne", "Active"],
            ["Rezerva", "Reserve"],
            ["Plynulé / sekčné", "Variable / staged"],
            ["Celkový výkon", "Total power"],
            ["Priemerné vyťaženie kanálov", "Average channel utilization"],
            ["Najsilnejší kanál", "Strongest channel"],
            ["Typ riadenia", "Control mode"],
            ["Plynulé SSR + sekcie", "Variable SSR + staged outputs"],
            ["Sekciové relé", "Staged relays"],
            ["Počet zobrazených kanálov sa riadi konfiguráciou systému. Prvý SSR kanál je modulovaný a ostatné dostupné výstupy sú sekčné.", "The number of displayed channels follows the system configuration. The first SSR channel is modulated and the remaining available outputs are staged."],
            ["Priama spotreba", "Direct consumption"],
            ["IR výstup", "IR output"],
            ["IS výstup", "IS output"],
            ["IT výstup", "IT output"],
            ["IR PCC", "IR PCC"],
            ["IS PCC", "IS PCC"],
            ["IT PCC", "IT PCC"],
            ["R výstup", "R output"],
            ["S výstup", "S output"],
            ["T výstup", "T output"],
            ["Naplánujte spotrebu do solárneho okna", "Plan consumption for the solar window"],
            ["Dnes alebo zajtra sa črtá vyšší export. Presuňte ohrev vody, umývačku, sušičku alebo EV nabíjanie do najsilnejšieho FV okna.", "Today or tomorrow is shaping up for higher export. Shift water heating, the dishwasher, dryer, or EV charging into the strongest PV window."],
            ["Spot ukazuje trhovu komoditnu cenu pre SK obchodnu oblast. Koncova cena pre odber v SR sa este sklada aj z distribucnych a dalsich regulovanych poloziek.", "Spot shows the market commodity price for the SK bidding zone. The final retail price in Slovakia also includes distribution and other regulated components."],
            ["modulácia", "modulation"],
            ["stav", "state"],
            ["Sťahujem telemetriu, históriu, počasie a pripravujem nový render dashboardu.", "Downloading telemetry, history, and weather, then preparing a new dashboard render."],
            ["Aktualizujem vybraný pohľad", "Refreshing the selected view"],
            ["nástroj", "tool"],
            ["neutral", "neutral"]
        ]);

        const literalKeys = Array.from(literalMap.keys()).sort((a, b) => b.length - a.length);

        const translateLiteralText = (value) => {
            if (!value) {
                return value;
            }

            let translated = value;
            literalKeys.forEach((source) => {
                const target = literalMap.get(source);
                if (source && target && translated.includes(source) && !translated.includes(target)) {
                    translated = translated.split(source).join(target);
                }
            });

            translated = translated
                .replace(/posledných\s+(\d+)\s+dní/gi, "last $1 days")
                .replace(/poslednýc?h\s+(\d+)\s+dn[ií]/gi, "last $1 days")
                .replace(/(\d+)\s+signálov/gi, "$1 signals")
                .replace(/(\d+)\s+odporúčaní/gi, "$1 recommendations")
                .replace(/(\d+)\s+zariadení/gi, "$1 devices")
                .replace(/(\d+)\s+aktívnych/gi, "$1 active")
                .replace(/(\d+(?:[.,]\d+)?)\s*%\s*vyťaženie/gi, "$1% utilization")
                .replace(/(\d+(?:[.,]\d+)?)\s*%\s*z\s*/gi, "$1% of ")
                .replace(/Možné zníženie exportu o\s+([\d.,]+)\s*kWh/gi, "Possible export reduction of $1 kWh")
                .replace(/Sekcia\s+(\d+)/gi, "Section $1")
                .replace(/TERAZ\s*-\s*/gi, "NOW - ")
                .replace(/Dnes\s*·\s*/gi, "Today · ");

            return translated;
        };

        const setText = (selector, value) => {
            const element = document.querySelector(selector);
            if (element) {
                element.textContent = value;
            }
        };

        const setHtml = (selector, transform) => {
            const element = document.querySelector(selector);
            if (element) {
                element.innerHTML = transform(element.innerHTML, element);
            }
        };

        const setTextList = (selector, values) => {
            document.querySelectorAll(selector).forEach((element, index) => {
                if (values[index]) {
                    element.textContent = values[index];
                }
            });
        };

        const translateElementTree = (root) => {
            if (!root) {
                return;
            }

            const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
                acceptNode(node) {
                    const parentTag = node.parentElement?.tagName;
                    if (!node.textContent?.trim() ||
                        parentTag === "SCRIPT" ||
                        parentTag === "STYLE" ||
                        parentTag === "NOSCRIPT" ||
                        node.parentElement?.closest("[data-no-translate]")) {
                        return NodeFilter.FILTER_REJECT;
                    }

                    return NodeFilter.FILTER_ACCEPT;
                }
            });

            const nodes = [];
            while (walker.nextNode()) {
                nodes.push(walker.currentNode);
            }

            nodes.forEach((node) => {
                const nextValue = translateLiteralText(node.textContent);
                if (nextValue && nextValue !== node.textContent) {
                    node.textContent = nextValue;
                }
            });

            root.querySelectorAll(attributeKeys.map((key) => `[${key}]`).join(",")).forEach((element) => {
                attributeKeys.forEach((key) => {
                    const currentValue = element.getAttribute(key);
                    const translated = translateLiteralText(currentValue);
                    if (translated && translated !== currentValue) {
                        element.setAttribute(key, translated);
                    }
                });
            });
        };

        const translateSection = (selector) => {
            document.querySelectorAll(selector).forEach((root) => translateElementTree(root));
        };

        const translateCharts = () => {
            if (!window.Chart?.instances) {
                return;
            }

            const instances = window.Chart.instances;
            const charts = Array.isArray(instances)
                ? instances
                : instances instanceof Map
                    ? Array.from(instances.values())
                    : Object.values(instances);

            charts.forEach((chart) => {
                if (!chart) {
                    return;
                }

                let changed = false;
                (chart.data?.datasets || []).forEach((dataset) => {
                    if (typeof dataset.label === "string") {
                        const translated = translateLiteralText(dataset.label);
                        if (translated !== dataset.label) {
                            dataset.label = translated;
                            changed = true;
                        }
                    }
                });

                Object.values(chart.options?.scales || {}).forEach((scale) => {
                    if (scale?.title && typeof scale.title.text === "string") {
                        const translated = translateLiteralText(scale.title.text);
                        if (translated !== scale.title.text) {
                            scale.title.text = translated;
                            changed = true;
                        }
                    }
                });

                ["title", "subtitle"].forEach((pluginKey) => {
                    const plugin = chart.options?.plugins?.[pluginKey];
                    if (!plugin) {
                        return;
                    }

                    if (typeof plugin.text === "string") {
                        const translated = translateLiteralText(plugin.text);
                        if (translated !== plugin.text) {
                            plugin.text = translated;
                            changed = true;
                        }
                    } else if (Array.isArray(plugin.text)) {
                        const translated = plugin.text.map((item) => translateLiteralText(item));
                        if (translated.join("|") !== plugin.text.join("|")) {
                            plugin.text = translated;
                            changed = true;
                        }
                    }
                });

                if (changed) {
                    chart.update("none");
                }
            });
        };

        const timelineButtons = document.querySelectorAll(".timeline-nav .btn");
        if (timelineButtons[0]) timelineButtons[0].textContent = "← Previous";
        if (timelineButtons[1]) timelineButtons[1].textContent = "Today";
        if (timelineButtons[2]) timelineButtons[2].textContent = "Next →";

        setText('label[for="SnNumber"]', "System");
        setText('label[for="Day"]', "Date");
        setText('label[for="HoursBack"]', "Range");
        setText('#dashboardRangeForm button[type="submit"]', "Refresh");

        document.querySelectorAll(".ctrl-strip__iconbtn").forEach((button, index) => {
            const title = index === 0 ? "Download daily CSV" : "Download timeline CSV";
            button.setAttribute("title", title);
            button.setAttribute("aria-label", title);
            const sr = button.querySelector(".u-sr-only");
            if (sr) {
                sr.textContent = index === 0 ? "Daily CSV" : "Timeline CSV";
            }
        });

        setTextList("#datasource-bar .datasource__lbl", [
            "Data source",
            "Sampling interval",
            "Points in window",
            "Window",
            "Freshness",
            "Auto refresh",
            "Installed PV capacity"
        ]);

        setText("#overview .main-chart__title .eyebrow", "Energy over time");
        setHtml("#overview .main-chart__title h2", (html) => translateLiteralText(html).replace(/Power overview\s*[—-]/, "Power overview —"));
        setTextList("#chart-readout .chart-readout__lbl", [
            "PV total",
            "MPPT 1",
            "MPPT 2",
            "Consumption",
            "Grid",
            "Export",
            "SOC",
            "Battery"
        ]);
        setText(".chart-readout__weather-title", "Weather");
        setTextList("#trendWeatherPanel .chart-weather-pill small", ["Temperature", "Cloud cover", "Wind", "Precipitation", "PV estimate"]);
        setHtml("#chart-readout .chart-readout__meta > span", (html) => translateLiteralText(html));

        setHtml("#day .day-strip__head h2", (html) => translateLiteralText(html));
        setHtml("#day .day-strip__head small", (html) => translateLiteralText(html));
        setTextList("#day .day-kpi__lbl", [
            "PV generation",
            "Home consumption",
            "Grid import",
            "Grid export",
            "Daily balance",
            "Daily self-sufficiency"
        ]);

        const snapshotPanels = document.querySelectorAll("#snapshot .panel-head");
        if (snapshotPanels[0]) {
            const eyebrow = snapshotPanels[0].querySelector(".eyebrow");
            const title = snapshotPanels[0].querySelector("h2");
            if (eyebrow) eyebrow.textContent = "Quality index";
            if (title) title.textContent = "System health score";
        }
        if (snapshotPanels[1]) {
            const eyebrow = snapshotPanels[1].querySelector(".eyebrow");
            const title = snapshotPanels[1].querySelector("h2");
            if (eyebrow) eyebrow.textContent = "Live snapshot";
            if (title) title.textContent = "Instant values (kW)";
        }
        if (snapshotPanels[2]) {
            const eyebrow = snapshotPanels[2].querySelector(".eyebrow");
            const title = snapshotPanels[2].querySelector("h2");
            if (eyebrow) eyebrow.textContent = "Energy budget";
            if (title) title.textContent = translateLiteralText(title.textContent);
        }

        setText("#flow-inverter-note", "DC → AC conversion");
        setText("#flow-home-note", "Total consumption");
        setText("#weatherPvTitle", "Estimated generation curve today");
        setText("#weatherSelectedDayLabel", "Today");

        setTextList("#history .data-table thead th", [
            "Date",
            "PV generation",
            "Consumption",
            "Import",
            "Export",
            "Self-sufficiency",
            "Self-consumption",
            "Balance"
        ]);

        setTextList("#advanced-analytics .panel-head .eyebrow", [
            "Battery over time",
            "PV utilization",
            "Autonomy history"
        ]);
        setTextList("#advanced-analytics .panel-head h2", [
            "Charging and discharging by day",
            "Where generated energy was directed",
            "Self-sufficiency and self-consumption"
        ]);

        setTextList("#devices .device-name", [
            "Photovoltaic inverter",
            "Battery storage",
            "WattRouter",
            "Smart meter",
            "External battery — BMS",
            "Wallbox / EVSE"
        ]);
        setTextList("#devices .device-type", [
            "Solar inverter",
            "BMS / accumulator",
            "Surplus control",
            "Smart electricity meter",
            "Battery Management System",
            "EV charging station"
        ]);

        [
            ".section-divider span",
            ".panel-head .eyebrow",
            ".panel-head h2",
            ".panel-badge",
            ".k-lbl",
            ".kpi-cell .k-lbl",
            ".day-kpi__sub",
            ".fn__lbl",
            ".fn span",
            ".lc-lbl",
            ".lmb__head span",
            ".datasource__val",
            ".trend-feature-card__metric small",
            ".trend-feature-card__metric span",
            ".st-lbl",
            ".st-row span",
            ".insight-lbl",
            ".insight-item small",
            ".device-footer small",
            ".device-metrics span",
            ".device-status",
            ".relay-chip small",
            ".relay-chip em",
            ".sc-head span",
            ".weather-main span",
            ".weather-main p",
            ".weather-kpis .kpi-cell small",
            ".weather-kpis .kpi-cell .k-lbl",
            ".action-rec-summary small",
            ".action-rec-summary p",
            ".action-rec-item small",
            ".smart-score span",
            ".smart-kpi small",
            ".anomaly-card p",
            ".anomaly-card small",
            ".evening-risk__kicker",
            ".evening-risk__hero p",
            ".evening-risk__window",
            ".spot-day-card small",
            ".story-card p",
            ".story-kpis .kpi-cell .k-lbl"
        ].forEach((selector) => translateSection(selector));

        [
            "#overview",
            "#day",
            "#snapshot",
            "#statistics",
            "#smart-center",
            "#action-recommendations",
            "#evening-import",
            "#market-story",
            "#weather",
            "#trends",
            "#flow",
            "#telemetry",
            "#grid-phases",
            "#economics",
            "#environment",
            "#history",
            "#advanced-analytics",
            "#devices",
            "#relays"
        ].forEach((selector) => translateSection(selector));

        translateElementTree(document.body);
        translateCharts();
    };

    applyStaticTranslations();
    applyDashboardEnglishOverlay();

    let translationFrame = 0;
    const scheduleStaticTranslations = () => {
        if (translationFrame) {
            return;
        }

        translationFrame = window.requestAnimationFrame(() => {
            translationFrame = 0;
            applyStaticTranslations();
            applyDashboardEnglishOverlay();
        });
    };

    const translationObserver = new MutationObserver((mutations) => {
        for (const mutation of mutations) {
            if (mutation.type === "childList" || mutation.type === "characterData") {
                scheduleStaticTranslations();
                break;
            }
        }
    });

    translationObserver.observe(body, {
        childList: true,
        characterData: true,
        subtree: true
    });

    window.addEventListener("load", scheduleStaticTranslations, { once: true });
    window.setTimeout(scheduleStaticTranslations, 400);
    window.setTimeout(scheduleStaticTranslations, 1400);
    if (uiState.lang === "en") {
        window.setInterval(scheduleStaticTranslations, 2500);
    }

    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const revealTargets = Array.from(document.querySelectorAll([
        ".page-hero",
        ".landing-hero",
        ".panel",
        ".quick-card",
        ".benefit-card",
        ".assignment-card",
        ".device-card",
        ".insight-card",
        ".empty-state",
        ".message-strip",
        ".hero-note"
    ].join(", ")));

    if (!prefersReducedMotion && revealTargets.length > 0) {
        const revealObserver = new IntersectionObserver((entries, observer) => {
            entries.forEach((entry) => {
                if (!entry.isIntersecting) {
                    return;
                }

                entry.target.classList.add("is-visible");
                observer.unobserve(entry.target);
            });
        }, {
            threshold: 0.12,
            rootMargin: "0px 0px -8% 0px"
        });

        revealTargets.forEach((target, index) => {
            target.classList.add("ui-reveal");
            target.style.transitionDelay = `${Math.min(index * 35, 220)}ms`;
            revealObserver.observe(target);
        });
    } else {
        revealTargets.forEach((target) => target.classList.add("is-visible"));
    }

    const syncScrolledState = () => {
        body.classList.toggle("is-scrolled", window.scrollY > 18);
    };

    syncScrolledState();
    window.addEventListener("scroll", syncScrolledState, { passive: true });

    const navRoot = document.querySelector("[data-nav-root]");
    const navToggle = document.querySelector("[data-nav-toggle]");
    const navPanel = document.querySelector("[data-nav-panel]");
    const compactNavQuery = window.matchMedia("(max-width: 1100px)");

    if (navRoot && navToggle && navPanel) {
        window.__nsNavBound = true;

        const syncNavExpanded = (expanded) => {
            const isExpanded = compactNavQuery.matches && expanded;
            body.classList.toggle("nav-expanded", isExpanded);
            navRoot.classList.toggle("is-open", isExpanded);
            navToggle.setAttribute("aria-expanded", isExpanded ? "true" : "false");
            navPanel.style.removeProperty("display");
        };

        const closeNav = () => {
            syncNavExpanded(false);
        };

        const syncNavForViewport = () => {
            if (!compactNavQuery.matches) {
                closeNav();
            } else {
                syncNavExpanded(body.classList.contains("nav-expanded"));
            }
        };

        navToggle.addEventListener("click", () => {
            const nextExpanded = !body.classList.contains("nav-expanded");
            syncNavExpanded(nextExpanded);
        });

        document.addEventListener("click", (event) => {
            if (!compactNavQuery.matches || !body.classList.contains("nav-expanded")) {
                return;
            }

            if (navRoot.contains(event.target)) {
                return;
            }

            closeNav();
        });

        document.addEventListener("keydown", (event) => {
            if (event.key !== "Escape" || !body.classList.contains("nav-expanded")) {
                return;
            }

            closeNav();
            navToggle.focus();
        });

        navPanel.querySelectorAll("a, button[type='submit']").forEach((target) => {
            target.addEventListener("click", () => {
                if (compactNavQuery.matches) {
                    closeNav();
                }
            });
        });

        syncNavForViewport();
        compactNavQuery.addEventListener("change", syncNavForViewport);
    }

    const railToggle = document.querySelector("[data-side-rail-toggle]");
    const compactRailQuery = window.matchMedia("(max-width: 1080px)");

    if (railToggle) {
        const railPreferenceKey = "neststats:rail-collapsed";
        const savedRailState = window.localStorage.getItem(railPreferenceKey);

        const setRailCollapsed = (collapsed, persist = true) => {
            body.classList.toggle("rail-collapsed", collapsed);
            railToggle.setAttribute("aria-expanded", collapsed ? "false" : "true");

            if (persist) {
                window.localStorage.setItem(railPreferenceKey, collapsed ? "true" : "false");
            }
        };

        const initialRailCollapsed = savedRailState == null
            ? compactRailQuery.matches
            : savedRailState === "true";

        setRailCollapsed(initialRailCollapsed, false);

        railToggle.addEventListener("click", () => {
            const nextCollapsed = !body.classList.contains("rail-collapsed");
            setRailCollapsed(nextCollapsed);
        });

        compactRailQuery.addEventListener("change", (event) => {
            if (window.localStorage.getItem(railPreferenceKey) != null) {
                return;
            }

            setRailCollapsed(event.matches, false);
        });
    }

    const sectionLinks = Array.from(document.querySelectorAll(".side-nav a[href^='#']"));
    const sections = Array.from(document.querySelectorAll(".section-block[id]"));

    if (sectionLinks.length > 0 && sections.length > 0) {
        const navObserver = new IntersectionObserver((entries) => {
            const visible = entries
                .filter((entry) => entry.isIntersecting)
                .sort((left, right) => right.intersectionRatio - left.intersectionRatio)[0];

            if (!visible) {
                return;
            }

            sectionLinks.forEach((link) => {
                const isMatch = link.getAttribute("href") === `#${visible.target.id}`;
                link.classList.toggle("is-active", isMatch);
            });
        }, {
            rootMargin: "-18% 0px -62% 0px",
            threshold: [0.12, 0.3, 0.55]
        });

        sections.forEach((section) => navObserver.observe(section));
    }
})();

(() => {
    const bootstrapNode = document.getElementById("dashboard-bootstrap");
    if (!bootstrapNode) {
        return;
    }

    if (window.__nestStatsDashboardBooted) {
        return;
    }

    window.__nestStatsDashboardBooted = true;

    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    let bootstrap;
    try {
        bootstrap = JSON.parse(bootstrapNode.textContent || "{}");
    } catch (error) {
        console.error("Dashboard bootstrap parse error", error);
        return;
    }

    if (typeof Chart === "undefined") {
        return;
    }

    const timeline = bootstrap.charts?.timeline ?? [];
    const history = bootstrap.charts?.history ?? [];
    const tariffBenchmarks = bootstrap.tariffBenchmarks ?? [];
    const energyBreakdowns = bootstrap.energyBreakdowns ?? [];
    const exportRevenue = bootstrap.exportRevenue ?? {};
    const environmentalBenefits = bootstrap.environmentalBenefits ?? {};
    const refreshSeconds = Number(bootstrap.refreshSeconds ?? 15);
    const wattMaxKw = Number(bootstrap.wattMaxKw ?? 16);
    const installedPvKw = Number(bootstrap.installedPvKw ?? 10);
    const relayChannelCount = Number(bootstrap.relayChannelCount ?? 8);
    const defaultProviderKey = bootstrap.defaultProviderKey ?? tariffBenchmarks[0]?.providerKey ?? "";
    const defaultTariffKey = bootstrap.defaultTariffKey ?? tariffBenchmarks[0]?.key ?? "";

    let overviewChart = null;
    let historyChart = null;
    let solarQualityChart = null;
    let exchangeChart = null;
    let allocationChart = null;
    let savingsChart = null;
    let supplyMixChart = null;
    let autonomyChart = null;
    let tariffPlanChart = null;
    let pvTelemetryChart = null;
    let mpptDetailChart = null;
    let batteryTelemetryChart = null;
    let gridQualityChart = null;
    let gridPhaseVoltageChart = null;
    let gridPhaseBalanceChart = null;
    let gridPhaseCurrentChart = null;
    let gridPhasePowerChart = null;
    let impactChart = null;
    const chartRenderTokens = new Map();
    const chartLoadingTimers = new Map();
    let overviewMode = "balance";
    let historyWindow = "7";
    let selectedProviderKey = defaultProviderKey;
    let selectedTariffKey = defaultTariffKey;
    const lineCurveTension = prefersReducedMotion ? 0 : 0.4;
    const chartAnimationDuration = prefersReducedMotion ? 0 : 720;

    const clamp = (value, min, max) => Math.min(Math.max(Number(value) || 0, min), max);
    const asNumber = (value) => (Number.isFinite(Number(value)) ? Number(value) : 0);
    const kw = (value) => `${asNumber(value).toFixed(2)} kW`;
    const pct = (value, decimals = 0) => `${asNumber(value).toFixed(decimals)} %`;
    const formatCurrency = (value, decimals = 2) => new Intl.NumberFormat(appLocale, {
        style: "currency",
        currency: "EUR",
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals
    }).format(asNumber(value));

    const setText = (id, value) => {
        const node = document.getElementById(id);
        if (node) {
            node.textContent = value;
        }
    };

    const setWidth = (id, percentage) => {
        const node = document.getElementById(id);
        if (node) {
            node.style.width = `${clamp(percentage, 0, 100)}%`;
        }
    };

    const setFlowLink = (id, active, reverse = false) => {
        const node = document.getElementById(id);
        if (!node) {
            return;
        }

        node.classList.toggle("flow-link--active", Boolean(active));
        node.classList.toggle("flow-link--reverse", Boolean(reverse));
    };

    const formatTimestamp = (value) => {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return uiState.lang === "en" ? "No timestamp" : "Bez času";
        }

        return `${date.toLocaleDateString(appLocale)} ${date.toLocaleTimeString(appLocale)}`;
    };

    const getHistorySeries = () => {
        if (history.length >= 2) {
            return history;
        }

        return buildHistoryFallback();
    };

    const getHistorySlice = () => {
        const source = getHistorySeries();

        if (historyWindow === "all") {
            return source;
        }

        const take = Number(historyWindow);
        if (!Number.isFinite(take) || take <= 0) {
            return source;
        }

        return source.slice(Math.max(0, source.length - take));
    };

    const formatBucketLabel = (timestamp, rangeMs) => {
        const date = new Date(timestamp);
        if (rangeMs > 1000 * 60 * 60 * 48) { // More than 2 days
            return date.toLocaleString(appLocale, { day: "numeric", month: "numeric" });
        }
        if (rangeMs > 1000 * 60 * 60 * 12) { // More than 12 hours
            return date.toLocaleString(appLocale, { day: "numeric", month: "numeric", hour: "2-digit", minute: "2-digit" });
        }
        return date.toLocaleTimeString(appLocale, { hour: "2-digit", minute: "2-digit" });
    };

    const aggregateTimeline = (points, maxPoints = 144) => {
        if (!Array.isArray(points) || points.length === 0) {
            return [];
        }

        if (points.length <= maxPoints) {
            const start = asNumber(points[0].ts);
            const end = asNumber(points[points.length - 1].ts);
            const rangeMs = Math.max(1, end - start);
            return points.map((point) => ({
                ...point,
                label: formatBucketLabel(point.ts, rangeMs)
            }));
        }

        const start = asNumber(points[0].ts);
        const end = asNumber(points[points.length - 1].ts);
        const rangeMs = Math.max(1, end - start);
        const bucketSize = Math.max(1, Math.ceil(rangeMs / maxPoints));
        const buckets = [];

        for (const point of points) {
            const ts = asNumber(point.ts);
            const index = Math.min(maxPoints - 1, Math.floor((ts - start) / bucketSize));

            if (!buckets[index]) {
                buckets[index] = {
                    count: 0,
                    ts: ts,
                    lastTs: ts,
                    pvKw: 0,
                    consumptionKw: 0,
                    gridKw: 0,
                    batteryKw: 0,
                    inverterKw: 0,
                    wattKw: 0,
                    soC: 0,
                    pvSaturationPct: 0,
                    wattUtilizationPct: 0,
                    relayAverageLoadPct: 0,
                    relaysOn: 0,
                    mppt1Kw: 0,
                    mppt2Kw: 0,
                    pvVoltageV: 0,
                    pvCurrentA: 0,
                    mppt1VoltageV: 0,
                    mppt2VoltageV: 0,
                    mppt1CurrentA: 0,
                    mppt2CurrentA: 0,
                    batteryVoltageV: 0,
                    batteryCurrentA: 0,
                    batteryTemperatureC: 0,
                    batterySoH: 0,
                    batteryChargeCycle: 0,
                    voltagePhaseR: 0,
                    voltagePhaseS: 0,
                    voltagePhaseT: 0,
                    currentOutputR: 0,
                    currentOutputS: 0,
                    currentOutputT: 0,
                    activePowerOutputR: 0,
                    activePowerOutputS: 0,
                    activePowerOutputT: 0,
                    currentPccR: 0,
                    currentPccS: 0,
                    currentPccT: 0,
                    activePowerPccR: 0,
                    activePowerPccS: 0,
                    activePowerPccT: 0,
                    gridFrequencyHz: 0,
                    gridFetch: false
                };
            }

            const bucket = buckets[index];
            bucket.count += 1;
            bucket.lastTs = ts;
            bucket.pvKw += asNumber(point.pvKw);
            bucket.consumptionKw += asNumber(point.consumptionKw);
            bucket.gridKw += asNumber(point.gridKw);
            bucket.batteryKw += asNumber(point.batteryKw);
            bucket.inverterKw += asNumber(point.inverterKw);
            bucket.wattKw += asNumber(point.wattKw);
            bucket.soC += asNumber(point.soC);
            bucket.pvSaturationPct += asNumber(point.pvSaturationPct);
            bucket.wattUtilizationPct += asNumber(point.wattUtilizationPct);
            bucket.relayAverageLoadPct += asNumber(point.relayAverageLoadPct);
            bucket.relaysOn += asNumber(point.relaysOn);
            bucket.mppt1Kw += asNumber(point.mppt1Kw);
            bucket.mppt2Kw += asNumber(point.mppt2Kw);
            bucket.pvVoltageV += asNumber(point.pvVoltageV);
            bucket.pvCurrentA += asNumber(point.pvCurrentA);
            bucket.mppt1VoltageV += asNumber(point.mppt1VoltageV);
            bucket.mppt2VoltageV += asNumber(point.mppt2VoltageV);
            bucket.mppt1CurrentA += asNumber(point.mppt1CurrentA);
            bucket.mppt2CurrentA += asNumber(point.mppt2CurrentA);
            bucket.batteryVoltageV += asNumber(point.batteryVoltageV);
            bucket.batteryCurrentA += asNumber(point.batteryCurrentA);
            bucket.batteryTemperatureC += asNumber(point.batteryTemperatureC);
            bucket.batterySoH += asNumber(point.batterySoH);
            bucket.batteryChargeCycle += asNumber(point.batteryChargeCycle);
            bucket.voltagePhaseR += asNumber(point.voltagePhaseR);
            bucket.voltagePhaseS += asNumber(point.voltagePhaseS);
            bucket.voltagePhaseT += asNumber(point.voltagePhaseT);
            bucket.currentOutputR += asNumber(point.currentOutputR);
            bucket.currentOutputS += asNumber(point.currentOutputS);
            bucket.currentOutputT += asNumber(point.currentOutputT);
            bucket.activePowerOutputR += asNumber(point.activePowerOutputR);
            bucket.activePowerOutputS += asNumber(point.activePowerOutputS);
            bucket.activePowerOutputT += asNumber(point.activePowerOutputT);
            bucket.currentPccR += asNumber(point.currentPccR);
            bucket.currentPccS += asNumber(point.currentPccS);
            bucket.currentPccT += asNumber(point.currentPccT);
            bucket.activePowerPccR += asNumber(point.activePowerPccR);
            bucket.activePowerPccS += asNumber(point.activePowerPccS);
            bucket.activePowerPccT += asNumber(point.activePowerPccT);
            bucket.gridFrequencyHz += asNumber(point.gridFrequencyHz);
            bucket.gridFetch = bucket.gridFetch || Boolean(point.gridFetch);
        }

        return buckets.filter(Boolean).map((bucket) => ({
            ts: bucket.lastTs,
            label: formatBucketLabel(bucket.lastTs, rangeMs),
            pvKw: bucket.pvKw / bucket.count,
            consumptionKw: bucket.consumptionKw / bucket.count,
            gridKw: bucket.gridKw / bucket.count,
            batteryKw: bucket.batteryKw / bucket.count,
            inverterKw: bucket.inverterKw / bucket.count,
            wattKw: bucket.wattKw / bucket.count,
            soC: bucket.soC / bucket.count,
            pvSaturationPct: bucket.pvSaturationPct / bucket.count,
            wattUtilizationPct: bucket.wattUtilizationPct / bucket.count,
            relayAverageLoadPct: bucket.relayAverageLoadPct / bucket.count,
            relaysOn: Math.round(bucket.relaysOn / bucket.count),
            mppt1Kw: bucket.mppt1Kw / bucket.count,
            mppt2Kw: bucket.mppt2Kw / bucket.count,
            pvVoltageV: bucket.pvVoltageV / bucket.count,
            pvCurrentA: bucket.pvCurrentA / bucket.count,
            mppt1VoltageV: bucket.mppt1VoltageV / bucket.count,
            mppt2VoltageV: bucket.mppt2VoltageV / bucket.count,
            mppt1CurrentA: bucket.mppt1CurrentA / bucket.count,
            mppt2CurrentA: bucket.mppt2CurrentA / bucket.count,
            batteryVoltageV: bucket.batteryVoltageV / bucket.count,
            batteryCurrentA: bucket.batteryCurrentA / bucket.count,
            batteryTemperatureC: bucket.batteryTemperatureC / bucket.count,
            batterySoH: bucket.batterySoH / bucket.count,
            batteryChargeCycle: bucket.batteryChargeCycle / bucket.count,
            voltagePhaseR: bucket.voltagePhaseR / bucket.count,
            voltagePhaseS: bucket.voltagePhaseS / bucket.count,
            voltagePhaseT: bucket.voltagePhaseT / bucket.count,
            currentOutputR: bucket.currentOutputR / bucket.count,
            currentOutputS: bucket.currentOutputS / bucket.count,
            currentOutputT: bucket.currentOutputT / bucket.count,
            activePowerOutputR: bucket.activePowerOutputR / bucket.count,
            activePowerOutputS: bucket.activePowerOutputS / bucket.count,
            activePowerOutputT: bucket.activePowerOutputT / bucket.count,
            currentPccR: bucket.currentPccR / bucket.count,
            currentPccS: bucket.currentPccS / bucket.count,
            currentPccT: bucket.currentPccT / bucket.count,
            activePowerPccR: bucket.activePowerPccR / bucket.count,
            activePowerPccS: bucket.activePowerPccS / bucket.count,
            activePowerPccT: bucket.activePowerPccT / bucket.count,
            gridFrequencyHz: bucket.gridFrequencyHz / bucket.count,
            gridFetch: bucket.gridFetch
        }));
    };

    const buildHistoryFallback = () => {
        const source = aggregateTimeline(timeline, 24);
        if (source.length === 0) {
            return [];
        }

        return source.map((point) => {
            const importKw = point.gridKw < 0 ? Math.abs(point.gridKw) : 0;
            const exportKw = point.gridKw > 0 ? point.gridKw : 0;
            const selfSufficiencyPct = point.consumptionKw > 0
                ? ((point.consumptionKw - importKw) / point.consumptionKw) * 100
                : 0;

            return {
                label: point.label,
                pv: point.pvKw,
                consumption: point.consumptionKw,
                import: importKw,
                export: exportKw,
                selfSufficiencyPct,
                selfConsumptionPct: point.pvKw > 0
                    ? ((point.pvKw - exportKw) / point.pvKw) * 100
                    : 0
            };
        });
    };

    const toggleChartEmptyState = (canvas, hasData, message) => {
        const wrap = canvas?.parentElement;
        if (!wrap) {
            return;
        }

        let empty = wrap.querySelector(".chart-empty");
        if (!empty) {
            empty = document.createElement("div");
            empty.className = "chart-empty";
            wrap.appendChild(empty);
        }

        empty.textContent = message;
        empty.style.display = hasData ? "none" : "grid";
        canvas.style.display = hasData ? "block" : "none";
    };

    const getChartShell = (canvas) => canvas?.closest(".main-chart__canvas-wrap, .chart-wrap");

    const ensureChartLoadingStrip = (canvas) => {
        const shell = getChartShell(canvas);
        if (!shell || shell.querySelector(".chart-loading-strip")) {
            return shell;
        }

        const strip = document.createElement("div");
        strip.className = "chart-loading-strip";
        strip.setAttribute("aria-hidden", "true");
        shell.appendChild(strip);
        return shell;
    };

    const finishChartRender = (key, shell, token) => {
        const activeTimer = chartLoadingTimers.get(key);
        if (activeTimer) {
            window.clearTimeout(activeTimer);
            chartLoadingTimers.delete(key);
        }

        if (chartRenderTokens.get(key) !== token) {
            return;
        }

        shell.classList.remove("is-chart-loading");
        chartRenderTokens.delete(key);
    };

    const queueChartRender = (key, canvas, render) => {
        if (!canvas) {
            return;
        }

        const shell = ensureChartLoadingStrip(canvas);
        if (!shell) {
            render();
            return;
        }

        const previousTimer = chartLoadingTimers.get(key);
        if (previousTimer) {
            window.clearTimeout(previousTimer);
            chartLoadingTimers.delete(key);
        }

        const token = Symbol(key);
        chartRenderTokens.set(key, token);
        shell.classList.add("is-chart-loading");

        window.requestAnimationFrame(() => {
            window.requestAnimationFrame(() => {
                if (chartRenderTokens.get(key) !== token) {
                    return;
                }

                try {
                    render();
                } finally {
                    const timer = window.setTimeout(() => {
                        finishChartRender(key, shell, token);
                    }, prefersReducedMotion ? 0 : 260);

                    chartLoadingTimers.set(key, timer);
                }
            });
        });
    };

    const getTimelineSeries = (maxPoints = 144) => aggregateTimeline(timeline, maxPoints);

    const buildCoverageSeries = (points) => points.map((point) => {
        const gridImportKw = point.gridKw < 0 ? Math.abs(point.gridKw) : 0;
        const batteryDischargeKw = point.batteryKw < 0 ? Math.abs(point.batteryKw) : 0;
        const directSolarKw = clamp(
            point.consumptionKw - gridImportKw - batteryDischargeKw,
            0,
            Math.max(point.pvKw, 0)
        );

        return {
            label: point.label,
            consumptionKw: point.consumptionKw,
            directSolarKw,
            batteryDischargeKw,
            gridImportKw,
            wattKw: point.wattKw
        };
    });

    const flowDirectionLabel = (gridKw) => {
        if (gridKw > 0.15) {
            return isEnglishUi ? "Grid export" : "Export do siete";
        }

        if (gridKw < -0.15) {
            return isEnglishUi ? "Grid import" : "Import zo siete";
        }

            return isEnglishUi ? "Near-zero flow" : "Takmer nulový tok";
    };

    const batteryModeLabel = (batteryKw) => {
        if (batteryKw > 0.15) {
            return isEnglishUi ? "charging" : "nabíja sa";
        }

        if (batteryKw < -0.15) {
            return isEnglishUi ? "discharging" : "vybíja sa";
        }

        return isEnglishUi ? "stable" : "stabilná";
    };

    const getTariffsForSelectedProvider = () => {
        const source = Array.isArray(tariffBenchmarks) ? tariffBenchmarks : [];
        const filtered = source.filter((item) => item.providerKey === selectedProviderKey);
        const output = filtered.length > 0 ? filtered : source;

        return [...output].sort((left, right) => {
            if (left.providerName !== right.providerName) {
                return String(left.providerName).localeCompare(String(right.providerName), "sk");
            }

            return String(left.tariffCode).localeCompare(String(right.tariffCode), "sk", { numeric: true });
        });
    };

    const getSelectedTariff = () => {
        const providerTariffs = getTariffsForSelectedProvider();
        if (providerTariffs.length === 0) {
            return null;
        }

        const selected = providerTariffs.find((item) => item.key === selectedTariffKey) ?? providerTariffs[0];
        selectedProviderKey = selected.providerKey;
        selectedTariffKey = selected.key;
        return selected;
    };

    const syncTariffControls = () => {
        const providerSelect = document.getElementById("tariff-provider-select");
        const planSelect = document.getElementById("tariff-plan-select");
        const providerTariffs = getTariffsForSelectedProvider();
        const selectedTariff = getSelectedTariff();

        if (providerSelect) {
            providerSelect.value = selectedProviderKey;
        }

        if (!planSelect) {
            return;
        }

        const nextOptions = providerTariffs.map((tariff) => `
            <option value="${tariff.key}" ${tariff.key === selectedTariff?.key ? "selected" : ""}>
                ${tariff.tariffCode} - ${tariff.tariffLabel}
            </option>
        `).join("");

        if (planSelect.innerHTML !== nextOptions) {
            planSelect.innerHTML = nextOptions;
        }

        planSelect.value = selectedTariff?.key ?? "";
    };

    const buildTariffCardHtml = (tariff, isActive) => `
        <article class="tariff-card tariff-card--interactive ${isActive ? "is-active" : ""}" data-tariff-key="${tariff.key}" data-provider-key="${tariff.providerKey}" role="button" tabindex="0" aria-pressed="${isActive ? "true" : "false"}">
            <div class="tariff-card__head">
                <div>
                    <h3>${tariff.tariffCode}</h3>
                    <span class="tariff-card__subhead">${tariff.tariffLabel}</span>
                </div>
                <strong>${formatCurrency(tariff.netAnnualBenefitEur, 0)}</strong>
            </div>
            <p>${tariff.assumptionLabel ?? tariff.notes ?? ""}</p>
            <dl class="tariff-metrics">
                <div><dt>Efektívna</dt><dd>${formatCurrency(tariff.effectiveImportRateEurPerKwh, 3)}</dd></div>
                <div><dt>Fix / mesiac</dt><dd>${formatCurrency(tariff.monthlyFixedFeeEur, 2)}</dd></div>
                <div><dt>VT / NT</dt><dd>${formatCurrency(tariff.highRateEurPerKwh, 3)} / ${formatCurrency(tariff.lowRateEurPerKwh ?? tariff.highRateEurPerKwh, 3)}</dd></div>
                <div><dt>Net / mesiac</dt><dd>${formatCurrency(tariff.netMonthlyBenefitEur, 2)}</dd></div>
            </dl>
            <a class="panel-meta" href="${tariff.sourceUrl}" target="_blank" rel="noreferrer">${tariff.sourceLabel} - ${tariff.effectiveDate}</a>
        </article>
    `;

    const renderTariffProviderGrid = () => {
        const container = document.getElementById("tariff-provider-grid");
        if (!container) {
            return;
        }

        const providerTariffs = getTariffsForSelectedProvider();
        const selectedTariff = getSelectedTariff();
        container.innerHTML = providerTariffs.map((tariff) => buildTariffCardHtml(tariff, tariff.key === selectedTariff?.key)).join("");
    };

    const updateTariffSpotlight = () => {
        const selectedTariff = getSelectedTariff();
        if (!selectedTariff) {
            return;
        }

        const providerLabel = selectedTariff.distributorName
            ? `${selectedTariff.providerName} - ${selectedTariff.distributorName}`
            : selectedTariff.providerName;

        setText("tariff-provider-label", providerLabel);
        setText("tariff-rate-label", `${formatCurrency(selectedTariff.effectiveImportRateEurPerKwh, 3)} / kWh`);
        setText("tariff-plan-label", `${selectedTariff.tariffCode} - ${selectedTariff.tariffLabel}`);
        setText("tariff-assumption-label", selectedTariff.assumptionLabel || selectedTariff.notes || "Bez doplňujúcej poznámky.");
        setText("tariff-net-day", formatCurrency(selectedTariff.netDailyBenefitEur, 2));
        setText("tariff-net-month", formatCurrency(selectedTariff.netMonthlyBenefitEur, 2));
        setText("tariff-net-year", formatCurrency(selectedTariff.netAnnualBenefitEur, 0));
        setText("tariff-fixed-fee", formatCurrency(selectedTariff.annualFixedFeeEur, 0));
        setText("tariff-nt-share", `${asNumber(selectedTariff.estimatedLowTariffSharePct).toFixed(0)} % NT`);
        setText("tariff-product-type", selectedTariff.productType || "fixná");
        setText("tariff-high-rate", formatCurrency(selectedTariff.highRateEurPerKwh, 3));
        setText("tariff-low-rate", formatCurrency(selectedTariff.lowRateEurPerKwh ?? selectedTariff.highRateEurPerKwh, 3));
        setText("tariff-export-rate", formatCurrency(exportRevenue.rateEurPerKwh, 3));
        setText("tariff-effective-date", selectedTariff.effectiveDate || exportRevenue.effectiveDate || "-");
        setText("tariff-source-label", selectedTariff.sourceLabel || exportRevenue.sourceLabel || "-");
        setText("tariff-annual-stack", formatCurrency(selectedTariff.netAnnualBenefitEur + asNumber(exportRevenue.annualRevenueEur), 0));
        setText("tariff-monthly-stack", `Mesačný súčet ${formatCurrency(selectedTariff.netMonthlyBenefitEur + asNumber(exportRevenue.monthlyRevenueEur), 2)}`);
    };

    const renderRelayChips = (relayStates) => {
        const container = document.getElementById("relay-grid-live");
        if (!container || !Array.isArray(relayStates)) {
            return;
        }

        const relayCount = relayStates.length;
        container.classList.remove("relay-grid--single", "relay-grid--few", "relay-grid--dense");
        if (relayCount === 1) {
            container.classList.add("relay-grid--single");
        } else if (relayCount <= 3) {
            container.classList.add("relay-grid--few");
        } else if (relayCount >= 7) {
            container.classList.add("relay-grid--dense");
        }

        const activeBadge = document.getElementById("relay-active-count");
        if (activeBadge) {
            const activeCount = relayStates.filter((relay) => Boolean(relay?.isOn)).length;
            activeBadge.textContent = `${activeCount} / ${relayCount} ${isEnglishUi ? "active" : "aktívnych"}`;
        }

        if (relayCount === 0) {
            container.innerHTML = "";
            return;
        }

        container.innerHTML = relayStates.map((relay) => {
            const powerKw = asNumber(relay.powerKw);
            const loadPct = clamp(relay.loadPercentage, 0, 100);
            const isOn = Boolean(relay.isOn);
            const relayDetail = isEnglishUi
                ? String(relay.detail ?? "")
                    .replace(/Plynula regulacia vykonu/gi, "Continuous power control")
                    .replace(/Konfigurovany kanal/gi, "Configured channel")
                    .replace(/Sekcia\s+(\d+)/gi, "Section $1")
                : (relay.detail ?? "");
            const modeLabel = relay.mode === "variable"
                ? (isEnglishUi ? "modulation" : "modulácia")
                : (isEnglishUi ? "state" : "stav");

            return `
                <article class="relay-chip ${isOn ? "is-on" : ""}">
                    <span>${relay.name}</span>
                    <strong>${powerKw.toFixed(2)} kW</strong>
                    <small>${relayDetail}</small>
                    <div class="relay-chip__track">
                        <span style="width:${loadPct}%"></span>
                    </div>
                    <em>${loadPct.toFixed(1)} % ${modeLabel}</em>
                </article>
            `;
        }).join("");
    };

    const withAlpha = (color, alpha) => {
        if (typeof color !== "string") {
            return `rgba(24,33,31,${alpha})`;
        }

        if (color.startsWith("rgba(") || color.startsWith("rgb(")) {
            const parts = color.match(/[\d.]+/g);
            if (parts?.length >= 3) {
                return `rgba(${parts[0]}, ${parts[1]}, ${parts[2]}, ${alpha})`;
            }
        }

        if (color.startsWith("#")) {
            let hex = color.slice(1);
            if (hex.length === 3) {
                hex = hex.split("").map((part) => part + part).join("");
            }

            if (hex.length >= 6) {
                const value = Number.parseInt(hex.slice(0, 6), 16);
                const red = (value >> 16) & 255;
                const green = (value >> 8) & 255;
                const blue = value & 255;
                return `rgba(${red}, ${green}, ${blue}, ${alpha})`;
            }
        }

        return color;
    };

    const gradient = (chart, from, to, mid) => {
        const { ctx, chartArea } = chart;
        if (!chartArea) {
            return from;
        }

        const fill = ctx.createLinearGradient(0, chartArea.top, 0, chartArea.bottom);
        fill.addColorStop(0, from);
        if (mid) {
            fill.addColorStop(0.52, mid);
        }
        fill.addColorStop(1, to);
        return fill;
    };

    const hasDashPattern = (dataset) => Array.isArray(dataset?.borderDash) && dataset.borderDash.length > 0;

    const surfaceFill = (chart, color, topAlpha = 0.22, midAlpha = 0.08, bottomAlpha = 0) =>
        gradient(
            chart,
            withAlpha(color, topAlpha),
            withAlpha(color, bottomAlpha),
            withAlpha(color, midAlpha)
        );

    const datasetLine = (label, values, borderColor, topFill, bottomFill, extra = {}) => ({
        label,
        data: values,
        borderColor,
        backgroundColor: (context) =>
            gradient(context.chart, topFill, bottomFill, withAlpha(borderColor, 0.09)),
        pointRadius: 0,
        pointHoverRadius: 6,
        pointHitRadius: 18,
        pointHoverBorderWidth: 2,
        pointHoverBackgroundColor: '#fff',
        borderWidth: 2.8,
        tension: lineCurveTension,
        cubicInterpolationMode: "monotone",
        spanGaps: true,
        fill: "origin",
        ...extra
    });

    const datasetPlainLine = (label, values, borderColor, extra = {}) => {
        const wantsSurface = extra.fill ?? !hasDashPattern(extra);

        return {
            label,
            data: values,
            borderColor,
            backgroundColor: wantsSurface
                ? (context) => surfaceFill(context.chart, borderColor, 0.14, 0.05, 0)
                : borderColor,
            pointRadius: 0,
            pointHoverRadius: 6,
            pointHitRadius: 18,
            pointHoverBorderWidth: 2,
            pointHoverBackgroundColor: '#fff',
            borderWidth: 2.5,
            tension: lineCurveTension,
            cubicInterpolationMode: "monotone",
            spanGaps: true,
            fill: wantsSurface ? "origin" : false,
            ...extra
        };
    };

    const defaultChartOptions = {
        responsive: true,
        maintainAspectRatio: false,
        normalized: true,
        interaction: {
            mode: "index",
            intersect: false
        },
        elements: {
            line: {
                borderCapStyle: "round",
                borderJoinStyle: "round",
                capBezierPoints: true
            }
        },
        animation: {
            duration: chartAnimationDuration,
            easing: "easeOutCubic"
        },
        layout: {
            padding: {
                top: 8
            }
        },
        plugins: {
            legend: {
                position: "bottom",
                labels: {
                    usePointStyle: true,
                    boxWidth: 10,
                    padding: 18
                }
            },
            tooltip: {
                backgroundColor: "rgba(24,33,31,0.92)",
                titleColor: "#f8fafc",
                bodyColor: "#f8fafc",
                padding: 14,
                callbacks: {
                    label(context) {
                        const axis = context.dataset.yAxisID;
                        const value = asNumber(context.parsed.y);

                        if (axis === "y1") {
                            return `${context.dataset.label}: ${value.toFixed(1)} %`;
                        }

                        if (axis === "y2") {
                            return `${context.dataset.label}: ${value.toFixed(0)}`;
                        }

                        return `${context.dataset.label}: ${value.toFixed(2)} kW`;
                    }
                }
            }
        },
        scales: {
            x: {
                grid: { color: "rgba(24,33,31,0.05)" },
                ticks: {
                    color: "#66726f",
                    maxRotation: 45,
                    minRotation: 45,
                    autoSkip: true,
                    maxTicksLimit: 12
                }
            },
            y: {
                grid: { color: "rgba(24,33,31,0.06)" },
                ticks: {
                    color: "#66726f",
                    callback(value) {
                        return `${value} kW`;
                    }
                }
            }
        }
    };

    const buildOverviewConfig = () => {
        const chartTimeline = getTimelineSeries(overviewMode === "balance" ? 120 : 96);
        const labels = chartTimeline.map((point) => point.label);

        if (overviewMode === "hardware") {
            return {
                type: "line",
                labels,
                datasets: [
                    datasetLine(
                        "PV total",
                        chartTimeline.map((point) => point.pvKw),
                        "#d97706",
                        "rgba(217,119,6,0.28)",
                        "rgba(217,119,6,0.02)"
                    ),
                    datasetLine(
                        "MPPT 1",
                        chartTimeline.map((point) => point.mppt1Kw),
                        "#DC2626",
                        "rgba(220,38,38,0.18)",
                        "rgba(220,38,38,0.02)"
                    ),
                    datasetLine(
                        "MPPT 2",
                        chartTimeline.map((point) => point.mppt2Kw),
                        "#EA580C",
                        "rgba(234,88,12,0.18)",
                        "rgba(234,88,12,0.02)"
                    ),
                    datasetPlainLine(
                        "Menic",
                        chartTimeline.map((point) => point.inverterKw),
                        "#18211f"
                    ),
                    datasetPlainLine(
                        "Batéria",
                        chartTimeline.map((point) => point.batteryKw),
                        "#7c3aed"
                    ),
                    datasetPlainLine(
                        "SOC",
                        chartTimeline.map((point) => point.soC),
                        "#4f46e5",
                        { yAxisID: "y1", borderDash: [6, 4] }
                    )
                ],
                options: {
                    ...defaultChartOptions,
                    scales: {
                        ...defaultChartOptions.scales,
                        y1: {
                            display: true,
                            position: "right",
                            grid: { drawOnChartArea: false },
                            ticks: {
                                color: "#66726f",
                                callback(value) {
                                    return `${value} %`;
                                }
                            }
                        }
                    }
                }
            };
        }

        if (overviewMode === "quality") {
            return {
                type: "line",
                labels,
                datasets: [
                    {
                        type: "bar",
                        label: "Aktivne SSR",
                        data: chartTimeline.map((point) => point.relaysOn),
                        backgroundColor: "rgba(15,118,110,0.25)",
                        borderRadius: 10,
                        yAxisID: "y2"
                    },
                    datasetPlainLine(
                        "SOC",
                        chartTimeline.map((point) => point.soC),
                        "#4f46e5",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "PV saturacia",
                        chartTimeline.map((point) => point.pvSaturationPct),
                        "#d97706",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "Watt vyuzitie",
                        chartTimeline.map((point) => point.wattUtilizationPct),
                        "#dc5f0c",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "SSR priemer",
                        chartTimeline.map((point) => point.relayAverageLoadPct),
                        "#0f766e",
                        { yAxisID: "y1", borderDash: [8, 4] }
                    )
                ],
                options: {
                    ...defaultChartOptions,
                    scales: {
                        ...defaultChartOptions.scales,
                        y: {
                            display: false
                        },
                        y1: {
                            display: true,
                            position: "left",
                            min: 0,
                            max: 100,
                            grid: { color: "rgba(24,33,31,0.06)" },
                            ticks: {
                                color: "#66726f",
                                callback(value) {
                                    return `${value} %`;
                                }
                            }
                        },
                        y2: {
                            display: true,
                            position: "right",
                            min: 0,
                            max: relayChannelCount,
                            grid: { drawOnChartArea: false },
                            ticks: {
                                color: "#66726f",
                                precision: 0
                            }
                        }
                    }
                }
            };
        }

        return {
            type: "line",
            labels,
            datasets: [
                datasetLine(
                    "PV vykon",
                    chartTimeline.map((point) => point.pvKw),
                    "#d97706",
                    "rgba(217,119,6,0.28)",
                    "rgba(217,119,6,0.02)"
                ),
                datasetLine(
                    "Spotreba",
                    chartTimeline.map((point) => point.consumptionKw),
                    "#18211f",
                    "rgba(24,33,31,0.14)",
                    "rgba(24,33,31,0.02)"
                ),
                datasetPlainLine(
                    "Siet",
                    chartTimeline.map((point) => point.gridKw),
                    "#2563eb",
                    { borderWidth: 2.3 }
                ),
                datasetPlainLine(
                    "Batéria",
                    chartTimeline.map((point) => point.batteryKw),
                    "#7c3aed"
                ),
                datasetPlainLine(
                    "WattRouter",
                    chartTimeline.map((point) => point.wattKw),
                    "#dc5f0c"
                ),
                datasetPlainLine(
                    "SOC",
                    chartTimeline.map((point) => point.soC),
                    "#4f46e5",
                    { yAxisID: "y1", borderDash: [6, 4] }
                )
            ],
            options: {
                ...defaultChartOptions,
                scales: {
                    ...defaultChartOptions.scales,
                    y1: {
                        display: true,
                        position: "right",
                        min: 0,
                        max: 100,
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} %`;
                            }
                        }
                    }
                }
            }
        };
    };

    const renderOverviewChart = () => {
        const canvas = document.getElementById("overviewChart");
        if (!canvas) {
            return;
        }

        queueChartRender("overview", canvas, () => {
            if (overviewChart) {
                overviewChart.destroy();
            }

            const config = buildOverviewConfig();
            const hasData = config.labels.length > 0 && config.datasets.some((dataset) => Array.isArray(dataset.data) && dataset.data.length > 0);
        toggleChartEmptyState(canvas, hasData, "Pre zvolený interval nie sú dostupné trendové dáta.");
            if (!hasData) {
                return;
            }

            overviewChart = new Chart(canvas, {
                type: config.type,
                data: {
                    labels: config.labels,
                    datasets: config.datasets
                },
                options: config.options
            });
        });
    };

    const renderHistoryChart = () => {
        const canvas = document.getElementById("historyChart");
        if (!canvas) {
            return;
        }

        queueChartRender("history", canvas, () => {
            if (historyChart) {
                historyChart.destroy();
            }

            const slice = getHistorySlice();
            const hasData = slice.length > 0;
        toggleChartEmptyState(canvas, hasData, "Pre zvolený rozsah nie sú dostupné denné agregácie.");
            if (!hasData) {
                return;
            }

            historyChart = new Chart(canvas, {
                type: "bar",
                data: {
                    labels: slice.map((point) => point.label),
                    datasets: [
                    {
                        type: "bar",
                        label: "PV",
                        data: slice.map((point) => point.pv),
                        backgroundColor: "rgba(217,119,6,0.72)",
                        borderRadius: 10
                    },
                    {
                        type: "bar",
                        label: "Spotreba",
                        data: slice.map((point) => point.consumption),
                        backgroundColor: "rgba(24,33,31,0.52)",
                        borderRadius: 10
                    },
                    datasetPlainLine(
                        "Import",
                        slice.map((point) => point.import),
                        "#2563eb"
                    ),
                    datasetPlainLine(
                        "Export",
                        slice.map((point) => point.export),
                        "#0f766e"
                    ),
                    datasetPlainLine(
                        "Sebestačnosť",
                        slice.map((point) => point.selfSufficiencyPct),
                        "#7c3aed",
                        { yAxisID: "y1" }
                    )
                    ]
                },
                options: {
                    ...defaultChartOptions,
                    scales: {
                        ...defaultChartOptions.scales,
                        y1: {
                            position: "right",
                            min: 0,
                            max: 100,
                            grid: { drawOnChartArea: false },
                            ticks: {
                                color: "#66726f",
                                callback(value) {
                                    return `${value} %`;
                                }
                            }
                        }
                    }
                }
            });
        });
    };

    const renderSolarQualityChart = () => {
        const canvas = document.getElementById("solarQualityChart");
        if (!canvas) {
            return;
        }

        if (solarQualityChart) {
            solarQualityChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.length > 0;
        toggleChartEmptyState(canvas, hasData, "Kvalitativne signaly sa pre tento cas nenasli.");
        if (!hasData) {
            return;
        }

        solarQualityChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    datasetLine(
                        "PV saturacia",
                        chartTimeline.map((point) => point.pvSaturationPct),
                        "#f59e0b",
                        "rgba(245,158,11,0.24)",
                        "rgba(245,158,11,0.02)",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "SOC",
                        chartTimeline.map((point) => point.soC),
                        "#4f46e5",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "SSR priemer",
                        chartTimeline.map((point) => point.relayAverageLoadPct),
                        "#0f766e",
                        { yAxisID: "y1", borderDash: [6, 4] }
                    ),
                    {
                        type: "bar",
                        label: "Aktivne SSR",
                        data: chartTimeline.map((point) => point.relaysOn),
                        backgroundColor: "rgba(15,118,110,0.18)",
                        borderRadius: 10,
                        yAxisID: "y2"
                    }
                ]
            },
            options: {
                ...defaultChartOptions,
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        display: false
                    },
                    y1: {
                        position: "left",
                        min: 0,
                        max: 100,
                        grid: { color: "rgba(24,33,31,0.06)" },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} %`;
                            }
                        }
                    },
                    y2: {
                        position: "right",
                        min: 0,
                        max: relayChannelCount,
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            precision: 0
                        }
                    }
                }
            }
        });
    };

    const renderExchangeChart = () => {
        const canvas = document.getElementById("exchangeChart");
        if (!canvas) {
            return;
        }

        if (exchangeChart) {
            exchangeChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.length > 0;
        toggleChartEmptyState(canvas, hasData, "Toky medzi sietou a domom teraz nie su k dispozicii.");
        if (!hasData) {
            return;
        }

        exchangeChart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    {
                        type: "bar",
                        label: "Import",
                        data: chartTimeline.map((point) => (point.gridKw < 0 ? Math.abs(point.gridKw) : 0)),
                        backgroundColor: "rgba(37,99,235,0.58)",
                        borderRadius: 8,
                        stack: "grid"
                    },
                    {
                        type: "bar",
                        label: "Export",
                        data: chartTimeline.map((point) => (point.gridKw > 0 ? point.gridKw : 0)),
                        backgroundColor: "rgba(15,118,110,0.58)",
                        borderRadius: 8,
                        stack: "grid"
                    },
                    datasetPlainLine(
                        "WattRouter",
                        chartTimeline.map((point) => point.wattKw),
                        "#dc5f0c"
                    ),
                    datasetPlainLine(
                        "Spotreba",
                        chartTimeline.map((point) => point.consumptionKw),
                        "#18211f"
                    ),
                    datasetPlainLine(
                        "Odber zo siete",
                        chartTimeline.map((point) => (point.gridFetch ? 1 : 0)),
                        "#7c3aed",
                        { yAxisID: "y1", borderDash: [6, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                if (context.dataset.yAxisID === "y1") {
                                    return `${context.dataset.label}: ${asNumber(context.parsed.y) > 0 ? "Zapnuté" : "Vypnuté"}`;
                                }

                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(2)} kW`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y1: {
                        position: "right",
                        min: 0,
                        max: 1,
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return Number(value) > 0 ? "Zap." : "Vyp.";
                            }
                        }
                    }
                }
            }
        });
    };

    const renderSupplyMixChart = () => {
        const canvas = document.getElementById("supplyMixChart");
        if (!canvas) {
            return;
        }

        if (supplyMixChart) {
            supplyMixChart.destroy();
        }

        const coverage = buildCoverageSeries(getTimelineSeries(72));
        const hasData = coverage.length > 0;
        toggleChartEmptyState(canvas, hasData, "Nepodarilo sa zostavit krytie spotreby z dostupnych dat.");
        if (!hasData) {
            return;
        }

        supplyMixChart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: coverage.map((point) => point.label),
                datasets: [
                    {
                        type: "bar",
                        label: "Priame PV",
                        data: coverage.map((point) => point.directSolarKw),
                        backgroundColor: "rgba(245,158,11,0.68)",
                        borderRadius: 8,
                        stack: "cover"
                    },
                    {
                        type: "bar",
                        label: "Bateria",
                        data: coverage.map((point) => point.batteryDischargeKw),
                        backgroundColor: "rgba(124,58,237,0.46)",
                        borderRadius: 8,
                        stack: "cover"
                    },
                    {
                        type: "bar",
                        label: "Import",
                        data: coverage.map((point) => point.gridImportKw),
                        backgroundColor: "rgba(37,99,235,0.34)",
                        borderRadius: 8,
                        stack: "cover"
                    },
                    datasetPlainLine(
                        "Spotreba",
                        coverage.map((point) => point.consumptionKw),
                        "#18211f",
                        { borderWidth: 2.4 }
                    ),
                    datasetPlainLine(
                        "WattRouter",
                        coverage.map((point) => point.wattKw),
                        "#dc5f0c",
                        { borderDash: [8, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                scales: {
                    x: {
                        ...defaultChartOptions.scales.x,
                        stacked: true
                    },
                    y: {
                        ...defaultChartOptions.scales.y,
                        stacked: true
                    }
                }
            }
        });
    };

    const renderAutonomyChart = () => {
        const canvas = document.getElementById("autonomyChart");
        if (!canvas) {
            return;
        }

        if (autonomyChart) {
            autonomyChart.destroy();
        }

        const slice = getHistorySlice();
        const hasData = slice.length > 0;
        toggleChartEmptyState(canvas, hasData, "Nie sú dostupné denné KPI pre sebestačnosť.");
        if (!hasData) {
            return;
        }

        autonomyChart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: slice.map((point) => point.label),
                datasets: [
                    {
                        type: "bar",
                        label: "Import",
                        data: slice.map((point) => point.import),
                        backgroundColor: "rgba(37,99,235,0.22)",
                        borderRadius: 8
                    },
                    {
                        type: "bar",
                        label: "Export",
                        data: slice.map((point) => point.export),
                        backgroundColor: "rgba(15,118,110,0.24)",
                        borderRadius: 8
                    },
                    datasetPlainLine(
                        "Sebestačnosť",
                        slice.map((point) => point.selfSufficiencyPct),
                        "#0f766e",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "Vlastna spotreba",
                        slice.map((point) => asNumber(point.selfConsumptionPct ?? point.selfUsePct)),
                        "#d97706",
                        { yAxisID: "y1", borderDash: [8, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                scales: {
                    ...defaultChartOptions.scales,
                    y1: {
                        position: "right",
                        min: 0,
                        max: 100,
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} %`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderAllocationChart = () => {
        const canvas = document.getElementById("allocationChart");
        if (!canvas) {
            return;
        }

        if (allocationChart) {
            allocationChart.destroy();
        }

        const toneColor = {
            solar: "#f59e0b",
            battery: "#7c3aed",
            grid: "#0f766e",
            import: "#2563eb",
            good: "#16a34a"
        };

        const production = energyBreakdowns[0]?.segments ?? [];
        const consumption = energyBreakdowns[1]?.segments ?? [];
        const hasData = production.length > 0 && consumption.length > 0;
        toggleChartEmptyState(canvas, hasData, "Rozdelenie energie zatial nema dost dat.");
        if (!hasData) {
            return;
        }

        allocationChart = new Chart(canvas, {
            type: "doughnut",
            data: {
                labels: [...production.map((segment) => segment.label), ...consumption.map((segment) => segment.label)],
                datasets: [
                    {
                        label: energyBreakdowns[0]?.title ?? "Vyroba",
                        data: production.map((segment) => segment.valueKwh),
                        backgroundColor: production.map((segment) => toneColor[segment.tone] ?? "#94a3b8"),
                        borderWidth: 0,
                        hoverOffset: 8,
                        radius: "96%",
                        cutout: "62%"
                    },
                    {
                        label: energyBreakdowns[1]?.title ?? "Spotreba",
                        data: consumption.map((segment) => segment.valueKwh),
                        backgroundColor: consumption.map((segment) => toneColor[segment.tone] ?? "#cbd5e1"),
                        borderWidth: 0,
                        hoverOffset: 8,
                        radius: "58%",
                        cutout: "32%"
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: "bottom",
                        labels: {
                            usePointStyle: true,
                            boxWidth: 10,
                            padding: 16
                        }
                    },
                    tooltip: {
                        backgroundColor: "rgba(24,33,31,0.92)",
                        titleColor: "#f8fafc",
                        bodyColor: "#f8fafc",
                        callbacks: {
                            label(context) {
                                return `${context.dataset.label}: ${asNumber(context.parsed).toFixed(1)} kWh`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderTariffPlanChart = () => {
        const canvas = document.getElementById("tariffPlanChart");
        if (!canvas) {
            return;
        }

        if (tariffPlanChart) {
            tariffPlanChart.destroy();
        }

        const selectedTariff = getSelectedTariff();
        const hasData = Boolean(selectedTariff);
        toggleChartEmptyState(canvas, hasData, "Tarifna skladba zatial nema vybrany plan.");
        if (!hasData) {
            return;
        }

        tariffPlanChart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: ["VT", "NT", "Efektívna", "Export"],
                datasets: [
                    {
                        type: "bar",
                        label: "Sadzba EUR / kWh",
                        data: [
                            asNumber(selectedTariff.highRateEurPerKwh),
                            asNumber(selectedTariff.lowRateEurPerKwh ?? selectedTariff.highRateEurPerKwh),
                            asNumber(selectedTariff.effectiveImportRateEurPerKwh),
                            asNumber(exportRevenue.rateEurPerKwh)
                        ],
                        backgroundColor: [
                            "rgba(15,118,110,0.72)",
                            "rgba(37,99,235,0.48)",
                            "rgba(24,33,31,0.42)",
                            "rgba(217,119,6,0.65)"
                        ],
                        borderRadius: 10
                    },
                    datasetPlainLine(
                        "NT podiel",
                        [
                            asNumber(selectedTariff.estimatedLowTariffSharePct),
                            asNumber(selectedTariff.estimatedLowTariffSharePct),
                            asNumber(selectedTariff.estimatedLowTariffSharePct),
                            0
                        ],
                        "#4f46e5",
                        { yAxisID: "y1", borderDash: [8, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                if (context.dataset.yAxisID === "y1") {
                                    return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(0)} %`;
                                }

                                return `${context.dataset.label}: ${formatCurrency(context.parsed.y, 3)}`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        min: 0,
                        grid: { color: "rgba(24,33,31,0.06)" },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} EUR`;
                            }
                        }
                    },
                    y1: {
                        position: "right",
                        min: 0,
                        max: 100,
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} %`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderPvTelemetryChart = () => {
        const canvas = document.getElementById("pvTelemetryChart");
        if (!canvas) {
            return;
        }

        if (pvTelemetryChart) {
            pvTelemetryChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.some((point) =>
            asNumber(point.mppt1Kw) > 0 ||
            asNumber(point.mppt2Kw) > 0 ||
            asNumber(point.pvKw) > 0
        );
        toggleChartEmptyState(canvas, hasData, "PV telemetry nema dostatok DC dat.");
        if (!hasData) {
            return;
        }

        pvTelemetryChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    datasetLine(
                        "MPPT 1 kW",
                        chartTimeline.map((point) => point.mppt1Kw),
                        "#d97706",
                        "rgba(217,119,6,0.24)",
                        "rgba(217,119,6,0.02)"
                    ),
                    datasetLine(
                        "MPPT 2 kW",
                        chartTimeline.map((point) => point.mppt2Kw),
                        "#f59e0b",
                        "rgba(245,158,11,0.22)",
                        "rgba(245,158,11,0.02)"
                    ),
                    datasetPlainLine(
                        "PV total",
                        chartTimeline.map((point) => point.pvKw),
                        "#0f766e",
                        { borderDash: [6, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(2)} kW`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderMpptDetailChart = () => {
        const canvas = document.getElementById("mpptDetailChart");
        if (!canvas) {
            return;
        }

        if (mpptDetailChart) {
            mpptDetailChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.some((point) =>
            asNumber(point.mppt1VoltageV) > 0 ||
            asNumber(point.mppt2VoltageV) > 0 ||
            Math.abs(asNumber(point.mppt1CurrentA)) > 0 ||
            Math.abs(asNumber(point.mppt2CurrentA)) > 0 ||
            asNumber(point.pvVoltageV) > 0 ||
            Math.abs(asNumber(point.pvCurrentA)) > 0
        );
        toggleChartEmptyState(canvas, hasData, "MPPT detail nema dostatok string dat.");
        if (!hasData) {
            return;
        }

        mpptDetailChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    datasetPlainLine(
                        "MPPT 1 V",
                        chartTimeline.map((point) => point.mppt1VoltageV),
                        "#d97706",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "MPPT 2 V",
                        chartTimeline.map((point) => point.mppt2VoltageV),
                        "#f59e0b",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "PV priemer V",
                        chartTimeline.map((point) => point.pvVoltageV),
                        "#0f766e",
                        { yAxisID: "y1", borderDash: [6, 4] }
                    ),
                    datasetPlainLine(
                        "MPPT 1 A",
                        chartTimeline.map((point) => point.mppt1CurrentA),
                        "#2563eb",
                        { yAxisID: "y2" }
                    ),
                    datasetPlainLine(
                        "MPPT 2 A",
                        chartTimeline.map((point) => point.mppt2CurrentA),
                        "#6366f1",
                        { yAxisID: "y2" }
                    ),
                    datasetPlainLine(
                        "PV priemer A",
                        chartTimeline.map((point) => point.pvCurrentA),
                        "#475569",
                        { yAxisID: "y2", borderDash: [6, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                if (context.dataset.yAxisID === "y2") {
                                    return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(2)} A`;
                                }

                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(1)} V`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        display: false
                    },
                    y1: {
                        position: "left",
                        grid: { color: "rgba(24,33,31,0.06)" },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} V`;
                            }
                        }
                    },
                    y2: {
                        position: "right",
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} A`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderBatteryTelemetryChart = () => {
        const canvas = document.getElementById("batteryTelemetryChart");
        if (!canvas) {
            return;
        }

        if (batteryTelemetryChart) {
            batteryTelemetryChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.some((point) =>
            asNumber(point.batteryVoltageV) > 0 ||
            Math.abs(asNumber(point.batteryCurrentA)) > 0 ||
            asNumber(point.batteryTemperatureC) > 0 ||
            asNumber(point.soC) > 0 ||
            asNumber(point.batteryChargeCycle) > 0
        );
        toggleChartEmptyState(canvas, hasData, "Battery telemetry nema dostatok BMS dat.");
        if (!hasData) {
            return;
        }

        batteryTelemetryChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    datasetPlainLine(
                        "Napatie",
                        chartTimeline.map((point) => point.batteryVoltageV),
                        "#0f766e"
                    ),
                    datasetPlainLine(
                        "Proud",
                        chartTimeline.map((point) => point.batteryCurrentA),
                        "#2563eb"
                    ),
                    datasetPlainLine(
                        "Teplota",
                        chartTimeline.map((point) => point.batteryTemperatureC),
                        "#dc5f0c",
                        { borderDash: [7, 4] }
                    ),
                    datasetPlainLine(
                        "SOC",
                        chartTimeline.map((point) => point.soC),
                        "#4f46e5",
                        { yAxisID: "y1" }
                    ),
                    datasetPlainLine(
                        "SOH",
                        chartTimeline.map((point) => point.batterySoH),
                        "#7c3aed",
                        { yAxisID: "y1", borderDash: [4, 4] }
                    ),
                    datasetPlainLine(
                        "Cykly",
                        chartTimeline.map((point) => point.batteryChargeCycle),
                        "#475569",
                        { yAxisID: "y2", borderDash: [6, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                if (context.dataset.yAxisID === "y1") {
                                    return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(0)} %`;
                                }

                                if (context.dataset.yAxisID === "y2") {
                                    return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(1)}`;
                                }

                                const suffix = context.dataset.label.includes("Napatie")
                                    ? " V"
                                    : context.dataset.label.includes("Proud")
                                        ? " A"
                                        : " C";
                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(1)}${suffix}`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        grid: { color: "rgba(24,33,31,0.06)" },
                        ticks: {
                            color: "#66726f"
                        }
                    },
                    y1: {
                        position: "right",
                        min: 0,
                        max: 100,
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} %`;
                            }
                        }
                    },
                    y2: {
                        position: "right",
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f"
                        }
                    }
                }
            }
        });
    };

    const renderGridQualityChart = () => {
        const canvas = document.getElementById("gridQualityChart");
        if (!canvas) {
            return;
        }

        if (gridQualityChart) {
            gridQualityChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.some((point) =>
            Math.abs(asNumber(point.gridKw)) > 0 ||
            asNumber(point.inverterKw) > 0 ||
            asNumber(point.gridFrequencyHz) > 0
        );
        toggleChartEmptyState(canvas, hasData, "Grid quality nema dostatok PCC dat.");
        if (!hasData) {
            return;
        }

        gridQualityChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    datasetLine(
                        "Menic AC",
                        chartTimeline.map((point) => point.inverterKw),
                        "#18211f",
                        "rgba(24,33,31,0.16)",
                        "rgba(24,33,31,0.02)"
                    ),
                    datasetPlainLine(
                        "PCC vykon",
                        chartTimeline.map((point) => point.gridKw),
                        "#2563eb"
                    ),
                    datasetPlainLine(
                        "Frekvencia",
                        chartTimeline.map((point) => point.gridFrequencyHz),
                        "#0f766e",
                        { yAxisID: "y1" }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                if (context.dataset.yAxisID === "y1") {
                                    return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(2)} Hz`;
                                }

                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(2)} kW`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y1: {
                        position: "right",
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} Hz`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderGridPhaseVoltageChart = () => {
        const canvas = document.getElementById("gridPhaseVoltageChart");
        if (!canvas) {
            return;
        }

        if (gridPhaseVoltageChart) {
            gridPhaseVoltageChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.some((point) =>
            asNumber(point.voltagePhaseR) > 0 ||
            asNumber(point.voltagePhaseS) > 0 ||
            asNumber(point.voltagePhaseT) > 0
        );
        toggleChartEmptyState(canvas, hasData, "Napatie faz nie je k dispozicii.");
        if (!hasData) {
            return;
        }

        gridPhaseVoltageChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    datasetPlainLine(
                        "Faza R",
                        chartTimeline.map((point) => point.voltagePhaseR),
                        "#2563eb"
                    ),
                    datasetPlainLine(
                        "Faza S",
                        chartTimeline.map((point) => point.voltagePhaseS),
                        "#0f766e"
                    ),
                    datasetPlainLine(
                        "Faza T",
                        chartTimeline.map((point) => point.voltagePhaseT),
                        "#d97706"
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(1)} V`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        ...defaultChartOptions.scales.y,
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} V`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderGridPhaseBalanceChart = () => {
        const canvas = document.getElementById("gridPhaseBalanceChart");
        if (!canvas) {
            return;
        }

        canvas.closest(".panel")?.querySelector("h2")?.replaceChildren(document.createTextNode(
            uiState.lang === "en" ? "Voltage symmetry and deviation" : "Symetria a odchylka napatia"
        ));

        if (gridPhaseBalanceChart) {
            gridPhaseBalanceChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const derivedTimeline = chartTimeline.map((point) => {
            const phaseValues = [
                asNumber(point.voltagePhaseR),
                asNumber(point.voltagePhaseS),
                asNumber(point.voltagePhaseT)
            ].filter((value) => value > 0);

            if (phaseValues.length === 0) {
                return {
                    label: point.label,
                    avgVoltage: 0,
                    spreadVoltage: 0
                };
            }

            const minVoltage = Math.min(...phaseValues);
            const maxVoltage = Math.max(...phaseValues);
            const avgVoltage = phaseValues.reduce((sum, value) => sum + value, 0) / phaseValues.length;

            return {
                label: point.label,
                avgVoltage,
                spreadVoltage: maxVoltage - minVoltage
            };
        });

        const hasData = derivedTimeline.some((point) =>
            asNumber(point.avgVoltage) > 0 || asNumber(point.spreadVoltage) > 0
        );

        toggleChartEmptyState(canvas, hasData, "Symetria faz nie je k dispozicii.");
        if (!hasData) {
            return;
        }

        gridPhaseBalanceChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: derivedTimeline.map((point) => point.label),
                datasets: [
                    datasetPlainLine(
                        "Priemer",
                        derivedTimeline.map((point) => point.avgVoltage),
                        "#4f46e5"
                    ),
                    datasetPlainLine(
                        "Rozptyl",
                        derivedTimeline.map((point) => point.spreadVoltage),
                        "#dc2626",
                        { yAxisID: "y1", borderDash: [6, 4], fill: false }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                const unit = context.dataset.yAxisID === "y1" ? " V diff" : " V";
                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(1)}${unit}`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        ...defaultChartOptions.scales.y,
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} V`;
                            }
                        }
                    },
                    y1: {
                        display: true,
                        position: "right",
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} V`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderGridPhaseCurrentChart = () => {
        const canvas = document.getElementById("gridPhaseCurrentChart");
        if (!canvas) {
            return;
        }

        if (gridPhaseCurrentChart) {
            gridPhaseCurrentChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.some((point) =>
            Math.abs(asNumber(point.currentOutputR)) > 0 ||
            Math.abs(asNumber(point.currentOutputS)) > 0 ||
            Math.abs(asNumber(point.currentOutputT)) > 0 ||
            Math.abs(asNumber(point.currentPccR)) > 0 ||
            Math.abs(asNumber(point.currentPccS)) > 0 ||
            Math.abs(asNumber(point.currentPccT)) > 0
        );
        toggleChartEmptyState(canvas, hasData, "Prúdy fáz nie sú dostupné.");
        if (!hasData) {
            return;
        }

        gridPhaseCurrentChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    datasetPlainLine(
                        "R vystup",
                        chartTimeline.map((point) => point.currentOutputR),
                        "#2563eb"
                    ),
                    datasetPlainLine(
                        "S vystup",
                        chartTimeline.map((point) => point.currentOutputS),
                        "#0f766e"
                    ),
                    datasetPlainLine(
                        "T vystup",
                        chartTimeline.map((point) => point.currentOutputT),
                        "#d97706"
                    ),
                    datasetPlainLine(
                        "R PCC",
                        chartTimeline.map((point) => point.currentPccR),
                        "#2563eb",
                        { borderDash: [6, 4] }
                    ),
                    datasetPlainLine(
                        "S PCC",
                        chartTimeline.map((point) => point.currentPccS),
                        "#0f766e",
                        { borderDash: [6, 4] }
                    ),
                    datasetPlainLine(
                        "T PCC",
                        chartTimeline.map((point) => point.currentPccT),
                        "#d97706",
                        { borderDash: [6, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(2)} A`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        ...defaultChartOptions.scales.y,
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} A`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderGridPhasePowerChart = () => {
        const canvas = document.getElementById("gridPhasePowerChart");
        if (!canvas) {
            return;
        }

        if (gridPhasePowerChart) {
            gridPhasePowerChart.destroy();
        }

        const chartTimeline = getTimelineSeries(96);
        const hasData = chartTimeline.some((point) =>
            Math.abs(asNumber(point.activePowerOutputR)) > 0 ||
            Math.abs(asNumber(point.activePowerOutputS)) > 0 ||
            Math.abs(asNumber(point.activePowerOutputT)) > 0 ||
            Math.abs(asNumber(point.activePowerPccR)) > 0 ||
            Math.abs(asNumber(point.activePowerPccS)) > 0 ||
            Math.abs(asNumber(point.activePowerPccT)) > 0
        );
        toggleChartEmptyState(canvas, hasData, "Fazovy vykon nie je dostupny.");
        if (!hasData) {
            return;
        }

        gridPhasePowerChart = new Chart(canvas, {
            type: "line",
            data: {
                labels: chartTimeline.map((point) => point.label),
                datasets: [
                    datasetPlainLine(
                        "R vystup",
                        chartTimeline.map((point) => point.activePowerOutputR),
                        "#2563eb"
                    ),
                    datasetPlainLine(
                        "S vystup",
                        chartTimeline.map((point) => point.activePowerOutputS),
                        "#0f766e"
                    ),
                    datasetPlainLine(
                        "T vystup",
                        chartTimeline.map((point) => point.activePowerOutputT),
                        "#d97706"
                    ),
                    datasetPlainLine(
                        "R PCC",
                        chartTimeline.map((point) => point.activePowerPccR),
                        "#2563eb",
                        { borderDash: [6, 4] }
                    ),
                    datasetPlainLine(
                        "S PCC",
                        chartTimeline.map((point) => point.activePowerPccS),
                        "#0f766e",
                        { borderDash: [6, 4] }
                    ),
                    datasetPlainLine(
                        "T PCC",
                        chartTimeline.map((point) => point.activePowerPccT),
                        "#d97706",
                        { borderDash: [6, 4] }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(2)} kW`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        ...defaultChartOptions.scales.y,
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} kW`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderImpactChart = () => {
        const canvas = document.getElementById("impactChart");
        if (!canvas) {
            return;
        }

        if (impactChart) {
            impactChart.destroy();
        }

        const periods = [
            {
                label: "Dnes",
                co2Kg: asNumber(environmentalBenefits.todayCo2SavedKg),
                coalKg: asNumber(environmentalBenefits.todayCoalSavedKg),
                yieldEur: asNumber(environmentalBenefits.todayYieldEur)
            },
            {
                label: "Mesiac",
                co2Kg: asNumber(environmentalBenefits.monthlyCo2SavedKg),
                coalKg: asNumber(environmentalBenefits.todayCoalSavedKg) * 30,
                yieldEur: asNumber(environmentalBenefits.monthlyYieldEur)
            },
            {
                label: "Rok",
                co2Kg: asNumber(environmentalBenefits.annualCo2SavedTons) * 1000,
                coalKg: asNumber(environmentalBenefits.annualCoalSavedTons) * 1000,
                yieldEur: asNumber(environmentalBenefits.annualYieldEur)
            },
            {
                label: "Celkom",
                co2Kg: asNumber(environmentalBenefits.co2SavedTons) * 1000,
                coalKg: asNumber(environmentalBenefits.coalSavedTons) * 1000,
                yieldEur: asNumber(environmentalBenefits.lifetimeYieldEur)
            }
        ];

        const hasData = periods.some((item) => item.co2Kg > 0 || item.coalKg > 0 || item.yieldEur > 0);
        toggleChartEmptyState(canvas, hasData, "Environmentálny benchmark nemá dostatok agregovaných dát.");
        if (!hasData) {
            return;
        }

        impactChart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: periods.map((item) => item.label),
                datasets: [
                    {
                        type: "bar",
                        label: "CO2 kg",
                        data: periods.map((item) => item.co2Kg),
                        backgroundColor: "rgba(15,118,110,0.62)",
                        borderRadius: 10
                    },
                    {
                        type: "bar",
                        label: "Uhlie kg",
                        data: periods.map((item) => item.coalKg),
                        backgroundColor: "rgba(217,119,6,0.58)",
                        borderRadius: 10
                    },
                    datasetPlainLine(
                        "Výnos EUR",
                        periods.map((item) => item.yieldEur),
                        "#4f46e5",
                        { yAxisID: "y1" }
                    )
                ]
            },
            options: {
                ...defaultChartOptions,
                plugins: {
                    ...defaultChartOptions.plugins,
                    tooltip: {
                        ...defaultChartOptions.plugins.tooltip,
                        callbacks: {
                            label(context) {
                                if (context.dataset.yAxisID === "y1") {
                                    return `${context.dataset.label}: ${formatCurrency(context.parsed.y, context.parsed.y > 999 ? 0 : 2)}`;
                                }

                                return `${context.dataset.label}: ${asNumber(context.parsed.y).toFixed(0)} kg`;
                            }
                        }
                    }
                },
                scales: {
                    ...defaultChartOptions.scales,
                    y: {
                        grid: { color: "rgba(24,33,31,0.06)" },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} kg`;
                            }
                        }
                    },
                    y1: {
                        position: "right",
                        grid: { drawOnChartArea: false },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} EUR`;
                            }
                        }
                    }
                }
            }
        });
    };

    const renderSavingsChart = () => {
        const canvas = document.getElementById("savingsChart");
        if (!canvas) {
            return;
        }

        if (savingsChart) {
            savingsChart.destroy();
        }

        const ordered = getTariffsForSelectedProvider()
            .sort((left, right) => asNumber(left.netAnnualBenefitEur) - asNumber(right.netAnnualBenefitEur));
        const hasData = ordered.length > 0;
        toggleChartEmptyState(canvas, hasData, "Benchmark úspory nie je pre vybraného dodávateľa dostupný.");
        if (!hasData) {
            return;
        }

        savingsChart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: ordered.map((item) => item.tariffCode),
                datasets: [
                    {
                        label: "Čistý ročný prínos",
                        data: ordered.map((item) => item.netAnnualBenefitEur),
                        backgroundColor: ordered.map((item) => item.key === selectedTariffKey ? "rgba(15,118,110,0.78)" : "rgba(24,33,31,0.28)"),
                        borderRadius: 10
                    }
                ]
            },
            options: {
                indexAxis: "y",
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: false
                    },
                    tooltip: {
                        backgroundColor: "rgba(24,33,31,0.92)",
                        titleColor: "#f8fafc",
                        bodyColor: "#f8fafc",
                        callbacks: {
                            label(context) {
                                return `${formatCurrency(context.parsed.x, 0)} / rok`;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { color: "rgba(24,33,31,0.05)" },
                        ticks: {
                            color: "#66726f",
                            callback(value) {
                                return `${value} EUR`;
                            }
                        }
                    },
                    y: {
                        grid: { display: false },
                        ticks: {
                            color: "#18211f"
                        }
                    }
                }
            }
        });
    };

    const updateLiveVisuals = (live) => {
        if (!live) {
            return;
        }

        const pvKw = asNumber(live.pvTotal ?? live.pvPower);
        const inverterKw = asNumber(live.inverterPower);
        const gridKw = asNumber(live.gridPower);
        const batteryKw = asNumber(live.batteryPower);
        const wattKw = asNumber(live.wattPowerKw);
        const consumptionKw = asNumber(live.consumption);
        const flowHomeKw = Math.max(0, consumptionKw - wattKw);
        const soc = clamp(live.soc, 0, 100);
        const pvSat = clamp(live.pvSaturationPct, 0, 100);
        const wattUtil = clamp(live.wattUtilizationPct, 0, 100);
        const relayAverage = clamp(live.relayAverageLoadPct, 0, 100);
        const relayCount = asNumber(live.relayCount);
        const batteryTemp = asNumber(live.batteryTemperature);
        const pvVoltage = asNumber(live.pvVoltage);
        const pvCurrent = asNumber(live.pvCurrent);
        const batteryVoltage = asNumber(live.batteryVoltage);
        const batteryCurrent = asNumber(live.batteryCurrent);
        const gridFrequency = asNumber(live.gridFrequency);
        const soh = asNumber(live.soh);
        const relayStates = Array.isArray(live.relayStates) ? live.relayStates : [];

        setText("live-pv", kw(pvKw));
        setText("live-inverter", kw(inverterKw));
        setText("live-consumption", kw(consumptionKw));
        setText("live-grid", kw(gridKw));
        setText("live-battery", kw(batteryKw));
        setText("live-soc", pct(soc));
        setText("live-watt", kw(wattKw));
        setText("live-updated-at", formatTimestamp(live.time));
        setText("live-pv-saturation", pct(pvSat, 1));
        setText("live-watt-util", pct(wattUtil, 1));
        setText("live-relay-average", pct(relayAverage, 1));
        setText("tech-live-soc", `${pct(soc)} SOC | SOH ${soh.toFixed(0)} %`);
        setText("tech-live-pv-dc", `${pvVoltage.toFixed(1)} V`);
        setText("tech-live-pv-current", `${pvCurrent.toFixed(1)} A`);
        setText("tech-live-battery-voltage", `${batteryVoltage.toFixed(1)} V`);
        setText("tech-live-battery-current", `${batteryCurrent.toFixed(1)} A`);
        setText("tech-live-grid-frequency", `${gridFrequency.toFixed(2)} Hz`);

        setWidth("live-soc-bar", soc);
        setWidth("live-pv-bar", pvSat);
        setWidth("live-watt-bar", wattUtil);
        setWidth("live-relay-bar", relayAverage);

        setText("flow-pv-kw", kw(pvKw));
        setText("flow-battery-kw", kw(batteryKw));
        setText("flow-inverter-kw", kw(inverterKw));
        setText("flow-home-kw", kw(flowHomeKw));
        setText("flow-grid-kw", kw(gridKw));
        setText("flow-watt-kw", kw(wattKw));

        setText("flow-pv-note", `${pct(pvSat, 1)} ${isEnglishUi ? "of" : "z"} ${installedPvKw.toFixed(1)} kWp`);
        setText(
            "flow-battery-note",
            `${pct(soc)} SOC${batteryTemp > 0 ? ` | ${batteryTemp.toFixed(1)} C` : ""} | ${batteryModeLabel(batteryKw)}`
        );
        setText("flow-inverter-note", isEnglishUi ? `AC node | home ${kw(flowHomeKw)}` : `AC uzol | dom ${kw(flowHomeKw)}`);
        setText("flow-grid-note", flowDirectionLabel(gridKw));
        setText("flow-home-note", isEnglishUi ? "Load" : "Odber");
        setText("flow-watt-note", `${pct(wattUtil, 1)} ${isEnglishUi ? "utilization" : "vyťaženie"} | ${relayCount.toFixed(0)}/${relayChannelCount.toFixed(0)} ${isEnglishUi ? "active" : "aktívne"}`.replace(/utilization(?:zation)+/gi, "utilization"));

        setWidth("flow-pv-fill", pvSat);
        setWidth("flow-battery-fill", soc);
        setWidth("flow-watt-fill", wattUtil);

        setFlowLink("flow-link-pv-inverter", pvKw > 0.05);
        setFlowLink("flow-link-battery-inverter", Math.abs(batteryKw) > 0.05, batteryKw > 0);
        setFlowLink("flow-link-inverter-home", flowHomeKw > 0.05);
        setFlowLink("flow-link-inverter-grid", Math.abs(gridKw) > 0.05, gridKw < 0);
        setFlowLink("flow-link-inverter-watt", wattKw > 0.05);

        renderRelayChips(relayStates);
    };

    const fetchLive = async () => {
        if (!bootstrap.liveEndpoint) {
            return;
        }

        try {
            const response = await fetch(bootstrap.liveEndpoint, {
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            if (!response.ok) {
                return;
            }

            const live = await response.json();
            updateLiveVisuals(live);
        } catch (error) {
            console.error("Dashboard live refresh failed", error);
        }
    };

    document.querySelectorAll("[data-chart-view]").forEach((button) => {
        button.addEventListener("click", () => {
            overviewMode = button.dataset.chartView || "balance";
            document.querySelectorAll("[data-chart-view]").forEach((target) => {
                target.classList.toggle("is-active", target === button);
            });
            renderOverviewChart();
        });
    });

    document.querySelectorAll("[data-history-window]").forEach((button) => {
        button.addEventListener("click", () => {
            historyWindow = button.dataset.historyWindow || "7";
            document.querySelectorAll("[data-history-window]").forEach((target) => {
                target.classList.toggle("is-active", target === button);
            });
            renderHistoryChart();
        });
    });

    const rerenderTariffAnalytics = () => {
        syncTariffControls();
        updateTariffSpotlight();
        renderTariffProviderGrid();
        renderTariffPlanChart();
        renderSavingsChart();
    };

    document.getElementById("tariff-provider-select")?.addEventListener("change", (event) => {
        selectedProviderKey = event.target.value || defaultProviderKey;
        selectedTariffKey = "";
        rerenderTariffAnalytics();
    });

    document.getElementById("tariff-plan-select")?.addEventListener("change", (event) => {
        selectedTariffKey = event.target.value || defaultTariffKey;
        rerenderTariffAnalytics();
    });

    document.addEventListener("click", (event) => {
        const card = event.target.closest("#tariff-provider-grid .tariff-card--interactive");
        if (!card) {
            return;
        }

        selectedProviderKey = card.dataset.providerKey || selectedProviderKey;
        selectedTariffKey = card.dataset.tariffKey || selectedTariffKey;
        rerenderTariffAnalytics();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const card = event.target.closest("#tariff-provider-grid .tariff-card--interactive");
        if (!card) {
            return;
        }

        event.preventDefault();
        selectedProviderKey = card.dataset.providerKey || selectedProviderKey;
        selectedTariffKey = card.dataset.tariffKey || selectedTariffKey;
        rerenderTariffAnalytics();
    });

    renderOverviewChart();
    renderHistoryChart();
    renderSolarQualityChart();
    renderExchangeChart();
    renderSupplyMixChart();
    renderAutonomyChart();
    renderAllocationChart();
    renderPvTelemetryChart();
    renderMpptDetailChart();
    renderBatteryTelemetryChart();
    renderGridQualityChart();
    renderGridPhaseVoltageChart();
    renderGridPhaseBalanceChart();
    renderGridPhaseCurrentChart();
    renderGridPhasePowerChart();
    renderImpactChart();
    rerenderTariffAnalytics();
    renderRelayChips(bootstrap.initialLive?.relayStates ?? []);
    updateLiveVisuals(bootstrap.initialLive);

    if (refreshSeconds > 0) {
        window.setInterval(fetchLive, refreshSeconds * 1000);
    }

    // ── PV cycle timeline scrubber ──────────────────────────────────────
    (function initPvTimeline() {
        const canAnimate = typeof Element !== "undefined" && !!Element.prototype.getAnimations;

        function initPvCycleBar(visual) {
            const bar = visual.querySelector(".js-pv-cycle-bar");
            if (!bar) return;
            const track = bar.querySelector(".js-pv-cycle-track");
            const fill = bar.querySelector(".js-pv-cycle-fill");
            const head = bar.querySelector(".js-pv-cycle-head");
            if (!track || !fill || !head) return;

            // Read --pv-cycle from the visual (inherited from auth-card--visual)
            const cycleStr = getComputedStyle(visual).getPropertyValue("--pv-cycle").trim();
            const cycleS = parseFloat(cycleStr) || 34;
            const cycleMsTotal = cycleS * 1000;

            // Remove CSS animation from scrub head — JS controls left
            head.style.animation = "none";

            let currentT = 0;  // seconds within cycle
            let lastTs = null;
            let isScrubbing = false;
            let cycleAnims = null;

            // Collect cycle-duration animations from all participating elements
            function getCycleAnims() {
                if (cycleAnims) return cycleAnims;
                if (!canAnimate) return (cycleAnims = []);
                const sels = [
                    ".pvs-sky", ".pvs-sun", ".pvs-sun__rays",
                    ".pvs-cloud", ".pvs-ground",
                    ".pvs-moon", ".pvs-stars",
                    ".pvs-house__img", ".pvs-house__shine", ".pvs-house__glow",
                    ".pvs-battery__fill", ".pvs-battery__bolt", ".pvs-battery__label",
                    ".pvs-inverter__led",
                    ".pvs-flow--solar", ".pvs-flow--charge", ".pvs-flow--night",
                    ".pvs-timeline__bg"
                ];
                const found = [];
                sels.forEach(function(sel) {
                    visual.querySelectorAll(sel).forEach(function(el) {
                        el.getAnimations().forEach(function(a) {
                            try {
                                const d = a.effect && a.effect.getTiming ? a.effect.getTiming().duration : 0;
                                if (typeof d === "number" && Math.abs(d - cycleMsTotal) < 800) {
                                    found.push(a);
                                }
                            } catch (e) { /* skip */ }
                        });
                    });
                });
                return (cycleAnims = found);
            }

            // Sync starting position from the sky animation (already running)
            function syncStart() {
                if (!canAnimate) return;
                const sky = visual.querySelector(".pvs-sky");
                if (!sky) return;
                try {
                    const skyAnims = sky.getAnimations();
                    for (let i = 0; i < skyAnims.length; i++) {
                        const a = skyAnims[i];
                        const d = a.effect && a.effect.getTiming ? a.effect.getTiming().duration : 0;
                        if (typeof d === "number" && Math.abs(d - cycleMsTotal) < 800) {
                            const t = a.currentTime;
                            if (typeof t === "number" && t >= 0) {
                                currentT = (t / 1000) % cycleS;
                            }
                            break;
                        }
                    }
                } catch (e) { /* skip */ }
            }

            // ── kW display ──────────────────────────────────────────
            var kwEl   = visual.querySelector(".pvs-inverter__kw");
            var modeEl = visual.querySelector(".pvs-inverter__mode");

            // Piecewise-linear solar/battery production curve (p = 0..1)
            function lerp(a, b, t) { return a + (b - a) * Math.max(0, Math.min(1, t)); }

            function kwAtProgress(p) {
                // 1-10 kW throughout the replay: solar ramps by day, storage carries the night.
                if (p < 0.04)  return { val: lerp(1.0,  1.8, p / 0.04),           mode: "solar"   };
                if (p < 0.12)  return { val: lerp(1.8,  5.0, (p-0.04)/0.08),      mode: "solar"   };
                if (p < 0.24)  return { val: lerp(5.0,  8.8, (p-0.12)/0.12),      mode: "solar"   };
                if (p < 0.32)  return { val: lerp(8.8, 10.0, (p-0.24)/0.08),      mode: "solar"   };
                if (p < 0.38)  return { val: 10.0,                                mode: "solar"   };
                if (p < 0.45)  return { val: lerp(10.0, 4.4, (p-0.38)/0.07),      mode: "solar"   };
                if (p < 0.50)  return { val: lerp(4.4,  1.0, (p-0.45)/0.05),      mode: "solar"   };
                if (p < 0.56)  return { val: lerp(1.0,  2.8, (p-0.50)/0.06),      mode: "battery" };
                if (p < 0.78)  return { val: lerp(2.8,  2.0, (p-0.56)/0.22),      mode: "battery" };
                if (p < 0.90)  return { val: lerp(2.0,  1.2, (p-0.78)/0.12),      mode: "battery" };
                if (p < 0.96)  return { val: lerp(1.2,  1.0, (p-0.90)/0.06),      mode: "battery" };
                return             { val: lerp(1.0,  1.8, (p-0.96)/0.04),          mode: "solar"   };
            }

            function updateKw() {
                if (!kwEl && !modeEl) return;
                var p = currentT / cycleS;
                var r = kwAtProgress(p);
                if (kwEl) {
                    kwEl.textContent = r.val.toFixed(1) + " kW";
                    kwEl.style.color = r.mode === "battery" ? "#a855f7" : "#10b981";
                }
                if (modeEl) {
                    modeEl.textContent = r.mode === "battery" ? "STORAGE" : "SOLAR";
                    modeEl.style.color = r.mode === "battery"
                        ? "rgba(168,85,247,.75)" : "rgba(16,185,129,.75)";
                }
            }

            function smoothstep(edge0, edge1, value) {
                var t = Math.max(0, Math.min(1, (value - edge0) / (edge1 - edge0)));
                return t * t * (3 - 2 * t);
            }

            function updateSceneVars(p) {
                var sunset = smoothstep(0.34, 0.45, p) * (1 - smoothstep(0.48, 0.58, p));
                var night = smoothstep(0.45, 0.56, p) * (1 - smoothstep(0.86, 0.96, p));
                var dawn = p > 0.86
                    ? smoothstep(0.86, 0.98, p)
                    : (p < 0.12 ? 1 - smoothstep(0.02, 0.12, p) : 0);
                var warm = Math.max(sunset, dawn * 0.72);

                visual.style.setProperty("--pvs-sunset-alpha", warm.toFixed(3));
                visual.style.setProperty("--pvs-night-alpha", night.toFixed(3));
                visual.style.setProperty("--pvs-cycle-pct", (p * 100).toFixed(2) + "%");
            }

            // ── Bar + kW update ─────────────────────────────────────
            function updateBar() {
                var p = currentT / cycleS;
                var pct = (p * 100).toFixed(2);
                fill.style.setProperty("--f", (100 - p * 100).toFixed(2) + "%");
                head.style.left = pct + "%";
                bar.setAttribute("aria-valuenow", Math.round(p * 100));
                updateSceneVars(p);
                updateKw();
            }

            // ── Seek all cycle animations to an absolute time ───────
            function seekAllTo(tSeconds) {
                var tMs = tSeconds * 1000;
                getCycleAnims().forEach(function(a) {
                    try { a.currentTime = tMs; } catch (e) { /* skip */ }
                });
            }

            // ── Scrub helpers ───────────────────────────────────────
            function scrubTo(clientX) {
                var rect = track.getBoundingClientRect();
                var p = Math.min(1, Math.max(0, (clientX - rect.left) / rect.width));
                currentT = p * cycleS;
                seekAllTo(currentT);
                updateBar();
            }

            function startScrub(clientX) {
                isScrubbing = true;
                bar.classList.add("is-scrubbing");
                // Collect animations once before pausing
                var anims = getCycleAnims();
                anims.forEach(function(a) { try { a.pause(); } catch (e) {} });
                scrubTo(clientX);
            }

            function endScrub() {
                if (!isScrubbing) return;
                isScrubbing = false;
                bar.classList.remove("is-scrubbing");
                // Resume from wherever user left the scrub head
                getCycleAnims().forEach(function(a) {
                    try {
                        // Re-sync position before resuming to ensure no drift
                        a.currentTime = currentT * 1000;
                        a.play();
                    } catch (e) {}
                });
            }

            // ── Mouse events ────────────────────────────────────────
            bar.addEventListener("mousedown", function(e) {
                e.preventDefault();
                startScrub(e.clientX);
            });
            document.addEventListener("mousemove", function(e) {
                if (isScrubbing) scrubTo(e.clientX);
            });
            document.addEventListener("mouseup", function() { endScrub(); });

            // ── Touch events ────────────────────────────────────────
            bar.addEventListener("touchstart", function(e) {
                e.preventDefault();
                startScrub(e.touches[0].clientX);
            }, { passive: false });
            document.addEventListener("touchmove", function(e) {
                if (!isScrubbing) return;
                e.preventDefault();
                scrubTo(e.touches[0].clientX);
            }, { passive: false });
            document.addEventListener("touchend", function() { endScrub(); });
            document.addEventListener("touchcancel", function() { endScrub(); });

            // Wheel / trackpad scrub: scroll the playbar like a replay timeline, then keep auto-playing.
            bar.addEventListener("wheel", function(e) {
                e.preventDefault();
                var rawDelta = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
                var direction = rawDelta >= 0 ? 1 : -1;
                var step = cycleS / 70;
                currentT = (currentT + direction * step + cycleS) % cycleS;
                seekAllTo(currentT);
                updateBar();
            }, { passive: false });

            // ── Keyboard ────────────────────────────────────────────
            bar.addEventListener("keydown", function(e) {
                var step = cycleS / 50;
                if (e.key === "ArrowRight" || e.key === "ArrowUp") {
                    currentT = (currentT + step) % cycleS;
                } else if (e.key === "ArrowLeft" || e.key === "ArrowDown") {
                    currentT = ((currentT - step) + cycleS) % cycleS;
                } else if (e.key === "Home") {
                    currentT = 0;
                } else if (e.key === "End") {
                    currentT = cycleS * 0.99;
                } else {
                    return;
                }
                seekAllTo(currentT);
                updateBar();
                e.preventDefault();
            });

            // ── rAF loop — auto-advances bar in sync with CSS anims ─
            function frame(ts) {
                if (!isScrubbing) {
                    if (lastTs !== null) {
                        var dt = Math.min((ts - lastTs) / 1000, 0.1);
                        currentT = (currentT + dt) % cycleS;
                    }
                    updateBar();
                }
                lastTs = ts;
                requestAnimationFrame(frame);
            }

            syncStart();
            updateBar();
            requestAnimationFrame(frame);
        }

        document.querySelectorAll(".pv-visual").forEach(initPvCycleBar);
    })();
    // ── end PV timeline ─────────────────────────────────────────────────
})();
