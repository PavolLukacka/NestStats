$dotnet = "C:\Users\mepla\.dotnet\dotnet.exe"

if (-not (Test-Path $dotnet)) {
    throw ".NET 10 SDK was not found at $dotnet"
}

& $dotnet run @args
