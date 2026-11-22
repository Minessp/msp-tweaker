# Renders Logo.xaml to Assets\msp-tweaker.ico. WPF rendering needs STA:
#   powershell.exe -NoProfile -STA -File tools\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$root = Split-Path $PSScriptRoot
$logo = ([Windows.Markup.XamlReader]::Parse((Get-Content "$root\Logo.xaml" -Raw)))['Logo']

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = foreach ($s in $sizes) {
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen(); $dc.DrawImage($logo, (New-Object Windows.Rect 0, 0, $s, $s)); $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $s, $s, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); , $ms.ToArray()
}

# ICO = header, one directory entry per size, then the PNG payloads
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $b = [byte]($sizes[$i] % 256) # 256 is stored as 0
    $w.Write($b); $w.Write($b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p) }
New-Item -ItemType Directory -Force "$root\Assets" | Out-Null
[IO.File]::WriteAllBytes("$root\Assets\msp-tweaker.ico", $ms.ToArray())
"wrote Assets\msp-tweaker.ico ($($ms.Length) bytes)"
