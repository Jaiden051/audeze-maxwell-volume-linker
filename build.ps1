$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appSource = Join-Path $scriptDir "VolumeLinker.cs"
$appExe = Join-Path $scriptDir "VolumeLinker.exe"
$setupSource = Join-Path $scriptDir "Setup.cs"
$setupExe = Join-Path $scriptDir "AudezeMaxwellVolumeLinker-Setup.exe"
$iconGenSource = Join-Path $scriptDir "GenerateIcon.cs"
$appIconFile = Join-Path $scriptDir "app.ico"
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $cscPath)) {
    Write-Error "C# compiler not found at $cscPath"
    exit 1
}

# Generate app.ico if needed so the .exe files look polished in File Explorer
if (-not (Test-Path $appIconFile)) {
    $iconCode = @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class IconGen {
    static void Main(string[] args) {
        using (var bmp = new Bitmap(64, 64))
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var bg = new SolidBrush(Color.FromArgb(16, 185, 129))) {
                g.FillEllipse(bg, 2, 2, 60, 60);
            }
            using (var pen = new Pen(Color.White, 5.0f)) {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, 14, 13, 36, 32, 180, 180);
                using (var cup = new SolidBrush(Color.White)) {
                    g.FillRectangle(cup, 12, 28, 8, 16);
                    g.FillRectangle(cup, 44, 28, 8, 16);
                }
                g.DrawLine(pen, 24, 36, 40, 36);
            }
            using (var ms = new MemoryStream()) {
                bmp.Save(ms, ImageFormat.Png);
                byte[] pngBytes = ms.ToArray();
                using (var fs = new FileStream(args[0], FileMode.Create))
                using (var bw = new BinaryWriter(fs)) {
                    bw.Write((ushort)0); // reserved
                    bw.Write((ushort)1); // type: icon
                    bw.Write((ushort)1); // count: 1
                    bw.Write((byte)64);  // width
                    bw.Write((byte)64);  // height
                    bw.Write((byte)0);   // colors
                    bw.Write((byte)0);   // reserved
                    bw.Write((ushort)1); // planes
                    bw.Write((ushort)32);// bpp
                    bw.Write((uint)pngBytes.Length);
                    bw.Write((uint)22);  // offset
                    bw.Write(pngBytes);
                }
            }
        }
    }
}
"@
    Set-Content -Path $iconGenSource -Value $iconCode -Encoding UTF8
    $iconGenExe = Join-Path $scriptDir "IconGen.exe"
    & $cscPath /nologo /target:exe /out:$iconGenExe /reference:System.Drawing.dll $iconGenSource
    & $iconGenExe $appIconFile
    Remove-Item $iconGenSource, $iconGenExe -Force -ErrorAction SilentlyContinue
}

# Stop existing instance if running so we can overwrite the exe
Get-Process -Name "VolumeLinker" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 250

Write-Host "1/2 Compiling VolumeLinker.exe..." -ForegroundColor Cyan
& $cscPath /nologo /target:winexe /out:$appExe /win32icon:$appIconFile /optimize+ /platform:anycpu /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll $appSource

if ($LASTEXITCODE -ne 0) {
    Write-Error "VolumeLinker.exe build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}
Write-Host "    -> Built $appExe" -ForegroundColor Green

Write-Host "2/2 Compiling standalone installer AudezeMaxwellVolumeLinker-Setup.exe..." -ForegroundColor Cyan
& $cscPath /nologo /target:winexe /out:$setupExe /win32icon:$appIconFile /optimize+ /platform:anycpu /resource:"$appExe,VolumeLinker.exe" /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll $setupSource

if ($LASTEXITCODE -ne 0) {
    Write-Error "Installer build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}
Write-Host "    -> Built $setupExe" -ForegroundColor Green
