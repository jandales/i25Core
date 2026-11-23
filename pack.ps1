# Builds the four i25Core NuGet packages into artifacts/packages.
#   .\pack.ps1                 uses the version from Directory.Build.props
#   .\pack.ps1 -Version 1.2.0  overrides it
param([string]$Version = "")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "artifacts/packages"
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem $out -Filter "i25Core.*" -ErrorAction SilentlyContinue | Remove-Item -Force

$projects = @(
    "i25Core.Core/i25Core.Core/i25Core.Core.csproj",
    "i25Core.Infrastructure/i25Core.Infrastructure/i25Core.Infrastructure.csproj",
    "i25Core.BusinessLayer/i25Core.BusinessLayer.csproj",
    "i25Core.AspNetCore/i25Core.AspNetCore.csproj"
)

foreach ($project in $projects) {
    $args = @("pack", (Join-Path $root $project), "-c", "Release", "-o", $out, "--nologo")
    if ($Version) { $args += "-p:Version=$Version" }
    dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "Packing $project failed." }
}

Get-ChildItem $out -Filter "*.nupkg" | ForEach-Object { "{0,-45} {1,8:N0} KB" -f $_.Name, ($_.Length / 1KB) }
