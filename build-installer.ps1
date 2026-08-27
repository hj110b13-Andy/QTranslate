#Requires -Version 5
<#
    Rebuilds dist\QTranslate-Setup.exe.

    The installer carries a copy of the QTranslate program folder as an embedded
    zip, so the payload has to be repacked whenever the patched Service.js - or
    the QTranslate installation it is taken from - changes.

    Needs the .NET SDK 9 (https://dotnet.microsoft.com/download).
#>
[CmdletBinding()]
param(
    # Where to take the QTranslate program files from.
    [string] $Source = 'C:\Program Files (x86)\QTranslate'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

$patch = Join-Path $root 'patch\Google Translate\Service.js'
$resources = Join-Path $root 'installer\Resources'
$zip = Join-Path $resources 'QTranslate.zip'
$optionsTemplate = Join-Path $resources 'Options.default.json'

if (-not (Test-Path -LiteralPath $patch)) { throw "找不到修正檔：$patch" }
if (-not (Test-Path -LiteralPath (Join-Path $Source 'QTranslate.exe'))) { throw "找不到 QTranslate：$Source" }
if (-not (Test-Path -LiteralPath $optionsTemplate)) { throw "找不到設定範本：$optionsTemplate" }

# ---- 1. Stage the program folder and drop the patched service in ----
Step '準備封裝內容'
$stage = Join-Path $env:TEMP ('qtpayload_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    Get-ChildItem -LiteralPath $Source | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse -Force
    }

    $serviceTarget = Join-Path $stage 'Services\Google Translate\Service.js'
    Copy-Item -LiteralPath $patch -Destination $serviceTarget -Force

    # 原版的 Uninstall.exe 不隨封裝散布：安裝程式自己提供解除安裝，
    # 留著它反而會有兩個移除途徑，其中一個會刪掉另一個。
    $stockUninstaller = Join-Path $stage 'Uninstall.exe'
    if (Test-Path -LiteralPath $stockUninstaller) {
        Remove-Item -LiteralPath $stockUninstaller -Force
        Write-Host '    已排除原版的 Uninstall.exe'
    }

    $count = (Get-ChildItem -LiteralPath $stage -Recurse -File).Count
    Write-Host "    $count 個檔案，Service.js $((Get-Item $serviceTarget).Length) 位元組"

    # ---- 2. Pack ----
    Step '封裝'
    New-Item -ItemType Directory -Force -Path $resources | Out-Null
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "    $([math]::Round((Get-Item $zip).Length / 1MB, 2)) MB"
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}

# ---- 3. Guard against shipping a personal OCR key ----
Step '檢查設定範本'
$template = Get-Content -LiteralPath $optionsTemplate -Raw
if ($template -notmatch '"OcrApiKey"\s*:\s*""') {
    throw '設定範本裡的 OcrApiKey 不是空的，請先清空再打包。'
}
Write-Host '    OCR 金鑰為空，OK'

# ---- 4. Build ----
Step '編譯'
$project = Join-Path $root 'installer\QTranslateFix.csproj'
$publish = Join-Path $root 'installer\publish'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet publish $project -c Release -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "編譯失敗（結束碼 $LASTEXITCODE）。" }

# ---- 5. Ship ----
Step '輸出'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exe = Join-Path $dist 'QTranslate-Setup.exe'
Copy-Item -LiteralPath (Join-Path $publish 'QTranslate-Setup.exe') -Destination $exe -Force

Write-Host ''
Write-Host "完成：$exe" -ForegroundColor Green
Write-Host "      $([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB"
