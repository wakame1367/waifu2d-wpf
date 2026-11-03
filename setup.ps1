# Fetches the packages build.ps1 needs: nuget.exe into tools/, then the Roslyn toolset and
# SkiaSharp into packages/. Run once after cloning.
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path tools\nuget.exe)) {
        New-Item -ItemType Directory -Force tools | Out-Null
        Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' `
            -OutFile tools\nuget.exe
    }
    $packages = @(
        'Microsoft.Net.Compilers.Toolset -Version 4.14.0',
        'SkiaSharp -Version 3.119.1',
        'SkiaSharp.NativeAssets.Win32 -Version 3.119.1',
        'System.Buffers -Version 4.5.1',
        'System.Memory -Version 4.5.5',
        'System.Numerics.Vectors -Version 4.5.0',
        'System.Runtime.CompilerServices.Unsafe -Version 4.5.3',
        'System.ValueTuple -Version 4.5.0'
    )
    foreach ($p in $packages) {
        & tools\nuget.exe install $p -OutputDirectory packages -NonInteractive
    }
    Write-Output "done: run build.ps1 next"
} finally {
    Pop-Location
}
