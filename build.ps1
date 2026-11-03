# Builds waifu2d.exe against .NET Framework 4.8's own compiler: no SDK, no Visual Studio,
# no XAML. SkiaSharp comes out of packages/ (tools\nuget.exe install SkiaSharp).
# Run: powershell -NoProfile -File build.ps1

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
    # Roslyn's csc, which reads the modern C# the sources are written in; .NET Framework's own
    # ($fw\csc.exe) is C# 5.
    $csc = 'packages\Microsoft.Net.Compilers.Toolset.4.14.0\tasks\net472\csc.exe'
    New-Item -ItemType Directory -Force bin | Out-Null

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /utf8output `
        /out:bin\waifu2d.exe `
        /r:$fw\System.dll `
        /r:$fw\System.Core.dll `
        /r:$fw\System.Xaml.dll `
        /r:$fw\System.Numerics.dll `
        /r:$fw\WPF\WindowsBase.dll `
        /r:$fw\WPF\PresentationCore.dll `
        /r:$fw\WPF\PresentationFramework.dll `
        /r:packages\SkiaSharp.3.119.1\lib\net462\SkiaSharp.dll `
        /r:packages\System.Buffers.4.5.1\lib\net461\System.Buffers.dll `
        /r:packages\System.Memory.4.5.5\lib\net461\System.Memory.dll `
        /r:packages\System.Numerics.Vectors.4.5.0\lib\net46\System.Numerics.Vectors.dll `
        /r:packages\System.Runtime.CompilerServices.Unsafe.4.5.3\lib\net461\System.Runtime.CompilerServices.Unsafe.dll `
        /r:packages\System.ValueTuple.4.5.0\lib\net461\System.ValueTuple.dll `
        Rgba.cs Psd.cs Rig.cs RigMotion.cs ClosedParts.cs RigRenderer.cs MicLevel.cs Waifu2dWindow.cs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Copy-Item packages\SkiaSharp.3.119.1\lib\net462\SkiaSharp.dll bin\
    Copy-Item packages\SkiaSharp.NativeAssets.Win32.3.119.1\runtimes\win-x64\native\libSkiaSharp.dll bin\
    Copy-Item packages\System.Buffers.4.5.1\lib\net461\System.Buffers.dll bin\
    Copy-Item packages\System.Memory.4.5.5\lib\net461\System.Memory.dll bin\
    Copy-Item packages\System.Numerics.Vectors.4.5.0\lib\net46\System.Numerics.Vectors.dll bin\
    Copy-Item packages\System.Runtime.CompilerServices.Unsafe.4.5.3\lib\net461\System.Runtime.CompilerServices.Unsafe.dll bin\
    Copy-Item packages\System.ValueTuple.4.5.0\lib\net461\System.ValueTuple.dll bin\

    Write-Output "built bin\waifu2d.exe"
} finally {
    Pop-Location
}
