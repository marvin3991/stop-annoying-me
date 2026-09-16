#Requires -Version 7.0
<#
.SYNOPSIS
    建置可直接執行的發行版本，輸出到 dist\。

.DESCRIPTION
    會先跑完整單元測試，測試沒過就不產生輸出，避免把壞掉的版本交出去。
    產出為相依框架（framework-dependent）的單一執行檔，需要目標電腦已安裝
    .NET 10 Desktop Runtime；加上 -SelfContained 則不需要，但檔案約 150 MB。

.EXAMPLE
    pwsh -File tools/publish.ps1

.EXAMPLE
    pwsh -File tools/publish.ps1 -SelfContained
#>

[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$SkipTests,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist')
)

$ErrorActionPreference = 'Stop'
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        Write-Host '→ 執行單元測試' -ForegroundColor Cyan
        dotnet test tests/StopAnnoyingMe.Core.Tests --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "單元測試失敗（結束碼 $LASTEXITCODE），已中止發行。"
        }
    }
    else {
        Write-Warning '已略過單元測試'
    }

    Write-Host '→ 重新產生應用程式圖示' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'generate-icon.ps1')

    $outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $outputFull) {
        Remove-Item -LiteralPath $outputFull -Recurse -Force
    }

    Write-Host '→ 發行' -ForegroundColor Cyan
    $arguments = @(
        'publish', 'src/StopAnnoyingMe.App',
        '-c', 'Release',
        '-r', 'win-x64',
        '-o', $outputFull,
        '--nologo',
        "-p:SelfContained=$($SelfContained.IsPresent.ToString().ToLowerInvariant())",
        '-p:PublishSingleFile=true',
        # SQLite 的 e_sqlite3.dll 是原生程式庫，不夾帶進來單一執行檔會缺檔
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none'
    )
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "發行失敗（結束碼 $LASTEXITCODE）。"
    }

    $executable = Join-Path $outputFull 'StopAnnoyingMe.exe'
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "找不到預期的輸出檔：$executable"
    }

    $sizeMb = [math]::Round((Get-Item -LiteralPath $executable).Length / 1MB, 1)
    Write-Host ''
    Write-Host "✓ 完成：$executable（$sizeMb MB）" -ForegroundColor Green
    if (-not $SelfContained) {
        Write-Host '  需要目標電腦已安裝 .NET 10 Desktop Runtime。'
    }
    Get-ChildItem -LiteralPath $outputFull | Select-Object Name, @{ N = 'Size'; E = { $_.Length } }
}
finally {
    Pop-Location
}
