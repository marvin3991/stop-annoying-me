#Requires -Version 7.0
<#
.SYNOPSIS
    以 docs/design-notes.md 定義的幾何路徑產生應用程式圖示 app.ico。

.DESCRIPTION
    用 WPF 把向量路徑渲染成多種尺寸的 PNG，再組成單一 .ico 檔。
    圖示設計調整時只要改這支腳本裡的路徑與顏色，重跑即可，不必手動修圖。

.EXAMPLE
    pwsh -File tools/generate-icon.ps1
#>

[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\StopAnnoyingMe.App\Assets\app.ico')
)

$ErrorActionPreference = 'Stop'
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

# 圖示設計：對話泡泡（外來打擾）＋中央加號（立刻記一筆），EvenOdd 讓泡泡中空。
$pathData = 'F0 M 40,36 H 216 V 180 H 136 L 72,220 V 180 H 40 Z ' +
            'M 64,60 H 192 V 156 H 128 L 96,176 V 156 H 64 Z ' +
            'M 116,76 H 140 V 100 H 164 V 124 H 140 V 148 H 116 V 124 H 92 V 100 H 116 Z'
$backgroundColor = '#12171C'
$foregroundColor = '#4FC3B6'
$cornerRadius = 48.0
$designSize = 256.0

# 16 起跳涵蓋系統匣，256 供 Explorer 大圖示使用。
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)

function New-IconPng {
    param([int]$Size)

    $geometry = [System.Windows.Media.Geometry]::Parse($pathData)
    $background = [System.Windows.Media.SolidColorBrush]::new(
        [System.Windows.Media.ColorConverter]::ConvertFromString($backgroundColor))
    $foreground = [System.Windows.Media.SolidColorBrush]::new(
        [System.Windows.Media.ColorConverter]::ConvertFromString($foregroundColor))

    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $rect = [System.Windows.Rect]::new(0, 0, $designSize, $designSize)
        $context.DrawRoundedRectangle($background, $null, $rect, $cornerRadius, $cornerRadius)
        $context.DrawGeometry($foreground, $null, $geometry)
    }
    finally {
        $context.Close()
    }

    # 以設計尺寸繪製後再縮放，小尺寸才不會因為重新佈局而走樣。
    $scale = $Size / $designSize
    $visual.Transform = [System.Windows.Media.ScaleTransform]::new($scale, $scale)

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))

    $stream = [System.IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        # 前置逗號避免 PowerShell 把位元組陣列展開成一串個別位元組
        return , $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

$frames = foreach ($size in $sizes) {
    [byte[]]$data = New-IconPng -Size $size
    if ($data.Length -lt 64) {
        throw "尺寸 $size 的 PNG 資料異常（只有 $($data.Length) bytes）"
    }

    [pscustomobject]@{ Size = $size; Data = $data }
}

$outputFull = [System.IO.Path]::GetFullPath($OutputPath)
$outputDir = [System.IO.Path]::GetDirectoryName($outputFull)
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# ICO 檔結構：ICONDIR(6) + ICONDIRENTRY(16 × N) + 影像資料
# Vista 之後的 Windows 支援 ICO 內直接放 PNG，省下 BMP+AND mask 的處理。
$fileStream = [System.IO.File]::Create($outputFull)
try {
    $writer = [System.IO.BinaryWriter]::new($fileStream)

    $writer.Write([uint16]0)                 # Reserved
    $writer.Write([uint16]1)                 # Type: 1 = icon
    $writer.Write([uint16]$frames.Count)     # 影像數量

    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        # 256 在 1 byte 欄位中以 0 表示
        $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)      # 寬
        $writer.Write([byte]$dimension)      # 高
        $writer.Write([byte]0)               # 調色盤色數：0 = 不使用調色盤
        $writer.Write([byte]0)               # Reserved
        $writer.Write([uint16]1)             # 色彩平面
        $writer.Write([uint16]32)            # 每像素位元數
        $writer.Write([uint32]$frame.Data.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame.Data)
    }

    $writer.Flush()
}
finally {
    $fileStream.Dispose()
}

$totalBytes = (Get-Item -LiteralPath $outputFull).Length
Write-Host "已產生 $outputFull（$($frames.Count) 種尺寸，$totalBytes bytes）"
