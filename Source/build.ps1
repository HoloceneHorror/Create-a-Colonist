# Builds the mod DLL into ../1.7/Assemblies using the .NET 10 SDK.
#   powershell -ExecutionPolicy Bypass -File build.ps1        release build (what gets published)
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Dev   adds the in-game test tooling (never publish this)
param([switch]$Dev)
$ErrorActionPreference = "Stop"
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet10\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$proj = Join-Path $PSScriptRoot "CreateAColonist\CreateAColonist.csproj"
$devFlag = if ($Dev) { "-p:DevBuild=true" } else { "-p:DevBuild=false" }
# --no-incremental so switching between dev and release always rebuilds.
& $dotnet build $proj -c Release -nologo -v quiet --no-incremental $devFlag
if ($LASTEXITCODE -eq 0) { Write-Host ("Built " + $(if ($Dev) { "DEV build (test tooling included, do not publish)" } else { "RELEASE build" })) }
exit $LASTEXITCODE
