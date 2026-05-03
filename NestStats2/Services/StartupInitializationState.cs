using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace NestStats2.Services;

public sealed class StartupInitializationState
{
    private readonly object _syncRoot = new();
    private readonly string? _logoDataUrl;

    public StartupInitializationState(IWebHostEnvironment environment)
    {
        var preferredLogoPath = Path.Combine(environment.WebRootPath ?? string.Empty, "logo.png");
        if (File.Exists(preferredLogoPath))
        {
            var bytes = File.ReadAllBytes(preferredLogoPath);
            _logoDataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }
    }

    public StartupInitializationSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return new StartupInitializationSnapshot(
                IsReady,
                HasFailed,
                Stage,
                Detail,
                Error);
        }
    }

    public bool IsReady { get; private set; }

    public bool HasFailed { get; private set; }

    public string Stage { get; private set; } = "Startup";

    public string Detail { get; private set; } = "Preparing NestStats.";

    public string? Error { get; private set; }

    public void Report(string stage, string detail)
    {
        lock (_syncRoot)
        {
            Stage = string.IsNullOrWhiteSpace(stage) ? "Startup" : stage;
            Detail = string.IsNullOrWhiteSpace(detail) ? "Preparing NestStats." : detail;
        }
    }

    public void MarkReady()
    {
        lock (_syncRoot)
        {
            IsReady = true;
            HasFailed = false;
            Error = null;
            Stage = "Ready";
            Detail = "NestStats is ready.";
        }
    }

    public void MarkFailed(Exception exception)
    {
        lock (_syncRoot)
        {
            IsReady = false;
            HasFailed = true;
            Stage = "Startup failed";
            Detail = "Initialization did not complete.";
            Error = exception.Message;
        }
    }

    public string RenderHtml(string returnUrl, bool isEnglish)
    {
        var snapshot = GetSnapshot();
        var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        var encodedReturnUrl = WebUtility.HtmlEncode(safeReturnUrl);
        var encodedStage = WebUtility.HtmlEncode(snapshot.Stage);
        var encodedDetail = WebUtility.HtmlEncode(snapshot.Detail);
        var encodedError = WebUtility.HtmlEncode(snapshot.Error ?? string.Empty);
        var encodedLogo = WebUtility.HtmlEncode(_logoDataUrl ?? string.Empty);
        var isFailed = snapshot.HasFailed ? "true" : "false";
        var htmlLang = isEnglish ? "en" : "sk";
        var startupTitle = isEnglish ? "App is starting" : "Aplikácia sa spúšťa";
        var startupDescription = isEnglish
            ? "The server is booting in the background. When it is ready, this page will automatically continue."
            : "Server štartuje na pozadí. Keď bude pripravený, táto stránka sa automaticky prepne ďalej.";
        var startupMeta = isEnglish
            ? "The application is being prepared. Everything will be available shortly."
            : "Aplikácia sa pripravuje. O chvíľu bude všetko dostupné.";
        var startupEyebrow = isEnglish ? "NestStats startup" : "NestStats spúšťanie";

        var html = $$"""
        <!DOCTYPE html>
        <html lang="{{htmlLang}}">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>NestStats startup</title>
            <style>
                :root {
                    color-scheme: light;
                    --bg: #f7f5ef;
                    --panel: rgba(255,255,255,0.92);
                    --ink: #101828;
                    --muted: #667085;
                    --line: #e7dfcf;
                    --accent: #c98a1c;
                    --accent-2: #f2bc57;
                    --danger: #b42318;
                }

                * { box-sizing: border-box; }
                body {
                    margin: 0;
                    min-height: 100vh;
                    display: grid;
                    place-items: center;
                    padding: 24px;
                    font-family: "Segoe UI", Arial, sans-serif;
                    color: var(--ink);
                    background:
                        radial-gradient(circle at top, rgba(201,138,28,0.18), transparent 34%),
                        linear-gradient(180deg, #fbfaf7 0%, var(--bg) 100%);
                }

                .startup-shell {
                    width: min(560px, 100%);
                    padding: 28px;
                    border: 1px solid var(--line);
                    border-radius: 24px;
                    background: var(--panel);
                    box-shadow: 0 18px 50px rgba(16,24,40,0.08);
                }

                .brand {
                    display: flex;
                    align-items: center;
                    gap: 14px;
                    margin-bottom: 8px;
                }

                .brand-logo {
                    width: 64px;
                    height: 64px;
                    object-fit: contain;
                    border-radius: 18px;
                    background: rgba(255,255,255,0.9);
                    border: 1px solid rgba(231,223,207,0.9);
                    box-shadow: 0 10px 24px rgba(16,24,40,0.08);
                    padding: 8px;
                    flex-shrink: 0;
                }

                .brand-copy {
                    display: flex;
                    flex-direction: column;
                    gap: 6px;
                    min-width: 0;
                }

                .eyebrow {
                    display: inline-flex;
                    align-items: center;
                    gap: 8px;
                    font-size: 12px;
                    font-weight: 700;
                    letter-spacing: 0.08em;
                    text-transform: uppercase;
                    color: #8a5a00;
                }

                .eyebrow::before {
                    content: "";
                    width: 8px;
                    height: 8px;
                    border-radius: 999px;
                    background: var(--accent);
                    box-shadow: 0 0 0 6px rgba(201,138,28,0.14);
                    animation: pulse 1.4s ease-in-out infinite;
                }

                h1 {
                    margin: 18px 0 8px;
                    font-size: clamp(28px, 4vw, 38px);
                    line-height: 1.05;
                    letter-spacing: -0.03em;
                }

                p {
                    margin: 0;
                    color: var(--muted);
                    font-size: 15px;
                    line-height: 1.6;
                }

                .status {
                    margin-top: 22px;
                    padding: 16px 18px;
                    border: 1px solid var(--line);
                    border-radius: 16px;
                    background: rgba(255,255,255,0.84);
                }

                .status strong {
                    display: block;
                    font-size: 14px;
                    margin-bottom: 6px;
                }

                .bar {
                    position: relative;
                    height: 12px;
                    margin-top: 18px;
                    overflow: hidden;
                    border-radius: 999px;
                    background: #efe7d7;
                }

                .bar::before {
                    content: "";
                    position: absolute;
                    inset: 0;
                    width: 38%;
                    border-radius: inherit;
                    background: linear-gradient(90deg, var(--accent), var(--accent-2));
                    animation: slide 1.1s ease-in-out infinite;
                }

                .meta {
                    margin-top: 14px;
                    font-size: 13px;
                    color: var(--muted);
                }

                .error {
                    margin-top: 14px;
                    padding: 12px 14px;
                    border-radius: 14px;
                    background: #fef3f2;
                    border: 1px solid #fecdca;
                    color: var(--danger);
                    display: none;
                }

                .error.is-visible {
                    display: block;
                }

                @keyframes slide {
                    0% { transform: translateX(-120%); }
                    100% { transform: translateX(320%); }
                }

                @keyframes pulse {
                    0%, 100% { transform: scale(1); opacity: 1; }
                    50% { transform: scale(0.72); opacity: 0.5; }
                }
            </style>
        </head>
        <body>
            <main class="startup-shell">
                <div class="brand">
                    {{(string.IsNullOrWhiteSpace(_logoDataUrl) ? string.Empty : $"<img class=\"brand-logo\" src=\"{encodedLogo}\" alt=\"NestStats logo\">")}}
                    <div class="brand-copy">
                        <span class="eyebrow">{{startupEyebrow}}</span>
                    </div>
                </div>
                <h1>{{startupTitle}}</h1>
                <p>{{startupDescription}}</p>

                <section class="status" aria-live="polite">
                    <strong id="startup-stage">{{encodedStage}}</strong>
                    <p id="startup-detail">{{encodedDetail}}</p>
                    <div class="bar" aria-hidden="true"></div>
                    <div class="meta">{{startupMeta}}</div>
                    <div class="error{{(snapshot.HasFailed ? " is-visible" : string.Empty)}}" id="startup-error">{{encodedError}}</div>
                </section>
            </main>

            <script>
                const startupReturnUrl = {{System.Text.Json.JsonSerializer.Serialize(safeReturnUrl)}};
                const startupStateUrl = "/startup-status";
                const startupFailed = {{isFailed}};

                async function checkStartup() {
                    try {
                        const response = await fetch(startupStateUrl, { cache: "no-store" });
                        if (!response.ok) {
                            return;
                        }

                        const state = await response.json();
                        const stage = document.getElementById("startup-stage");
                        const detail = document.getElementById("startup-detail");
                        const error = document.getElementById("startup-error");

                        if (stage && state.stage) {
                            stage.textContent = state.stage;
                        }

                        if (detail && state.detail) {
                            detail.textContent = state.detail;
                        }

                        if (error) {
                            if (state.hasFailed && state.error) {
                                error.textContent = state.error;
                                error.classList.add("is-visible");
                            } else {
                                error.textContent = "";
                                error.classList.remove("is-visible");
                            }
                        }

                        if (state.isReady) {
                            window.location.replace(startupReturnUrl);
                        }
                    } catch {
                    }
                }

                if (!startupFailed) {
                    window.setInterval(checkStartup, 900);
                    window.setTimeout(checkStartup, 160);
                }
            </script>
        </body>
        </html>
        """;

        return html;
    }
}

public sealed record StartupInitializationSnapshot(
    bool IsReady,
    bool HasFailed,
    string Stage,
    string Detail,
    string? Error);
