#Requires -Version 5
<#
    QTranslate 設定診斷

    用途：找出「結束 QTranslate 再重啟之後，設定就不見了」的原因。

    直接雙擊 Diagnose.cmd 執行即可，不需要系統管理員權限。
    跑完會把結果存成 QTranslate-診斷.txt，可以直接把內容貼回來。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$lines = New-Object System.Collections.Generic.List[string]

function Say($text) {
    Write-Host $text
    $lines.Add($text)
}
function Section($title) {
    Say ''
    Say "===== $title ====="
}

<#
    QTranslate 把快速鍵存成 (修飾鍵 << 8) | 虛擬鍵碼。
    修飾鍵的位元：Alt=1、Ctrl=2、Shift=4、Win=8，而 0x80 代表「連點兩下」。

    所以 33280 (0x8200) 是「連點兩下 Ctrl」，
    而 512 (0x0200) 少了 0x80，會變成「單擊 Ctrl」。
#>
function Decode-HotKey([int]$value) {
    if ($value -eq 0) { return '(未設定)' }

    $mods = $value -shr 8
    $vk = $value -band 0xFF
    $parts = @()

    if ($mods -band 0x02) { $parts += 'Ctrl' }
    if ($mods -band 0x04) { $parts += 'Shift' }
    if ($mods -band 0x01) { $parts += 'Alt' }
    if ($mods -band 0x08) { $parts += 'Win' }

    $key = switch ($vk) {
        0   { '' }
        13  { 'Enter' }
        32  { 'Space' }
        default {
            if ($vk -ge 0x30 -and $vk -le 0x5A) { [char]$vk }
            elseif ($vk -ge 0x70 -and $vk -le 0x87) { 'F' + ($vk - 0x6F) }
            else { "VK_0x{0:X2}" -f $vk }
        }
    }

    $text = ($parts + $key | Where-Object { $_ }) -join '+'
    if ($mods -band 0x80) { $text = "連點兩下 $text" }
    return $text
}

Say "QTranslate 設定診斷    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Say "電腦: $env:COMPUTERNAME    使用者: $env:USERNAME"

# ---------------------------------------------------------------- 設定檔
Section '設定檔'

$appData = [Environment]::GetFolderPath('ApplicationData')
Say "目前處理程序看到的 APPDATA: $appData"
$optionsDir = Join-Path $appData 'QTranslate'
$options = Join-Path $optionsDir 'Options.json'

if (-not (Test-Path -LiteralPath $optionsDir)) {
    Say "[問題] 設定資料夾不存在: $optionsDir"
} else {
    Say "設定資料夾: $optionsDir"
    Get-ChildItem -LiteralPath $optionsDir -File | ForEach-Object {
        Say ("  {0,-26} {1,9} bytes   最後修改 {2}" -f $_.Name, $_.Length, $_.LastWriteTime)
    }
}

if (-not (Test-Path -LiteralPath $options)) {
    Say '[問題] 找不到 Options.json —— QTranslate 從來沒有成功儲存過設定。'
} else {
    $item = Get-Item -LiteralPath $options
    Say "唯讀屬性: $($item.IsReadOnly)"
    if ($item.IsReadOnly) { Say '[問題] 檔案是唯讀的，QTranslate 存不進去。' }

    # 內容是否為合法 JSON
    try {
        $json = Get-Content -LiteralPath $options -Raw -Encoding UTF8 | ConvertFrom-Json
        Say 'JSON 格式: 正常'
        foreach ($probe in @(
                @{ Path = 'General.MouseMode';              Label = '滑鼠模式顯示方式'; Expect = 1 }
                @{ Path = 'General.MouseModeOn';            Label = '滑鼠模式啟用';   Expect = $true }
                @{ Path = 'Advanced.EnableMouseModeOnCtrl'; Label = '按住 Ctrl 才翻譯'; Expect = $true }
                @{ Path = 'Advanced.RemoveLineBreaks';      Label = '移除換行字元';   Expect = $false }
                @{ Path = 'General.LocaleFoderName';        Label = '介面語言';       Expect = 'Chinese (Traditional)' })) {
            $parts = $probe.Path.Split('.')
            $value = $json
            foreach ($p in $parts) { $value = if ($null -ne $value) { $value.$p } else { $null } }
            $flag = if ($null -ne $probe.Expect -and "$value" -ne "$($probe.Expect)") { "   <- 預期 $($probe.Expect)" } else { '' }
            Say ("  {0,-18} = {1}{2}" -f $probe.Label, $value, $flag)
        }

        # 快速鍵單獨處理：數字看不出對錯，解讀成人看得懂的形式並比對預期值。
        foreach ($hk in @(
                @{ Key = 'HotKeyTextRecognition'; Label = '畫面框選翻譯'; Expect = 33280 }
                @{ Key = 'HotKeyMainWindow';      Label = '主視窗';       Expect = 849 }
                @{ Key = 'HotKeyPopupWindow';     Label = '彈出視窗翻譯'; Expect = 593 }
                @{ Key = 'HotKeyListenText';      Label = '朗讀選取文字'; Expect = 581 })) {
            $value = $json.HotKeys.$($hk.Key)
            if ($null -eq $value) {
                Say ("  {0,-18} = (設定檔裡沒有這一項)" -f $hk.Label)
                continue
            }
            $value = [int]$value
            Say ("  {0,-18} = {1}  ({2})" -f $hk.Label, $value, (Decode-HotKey $value))
            if ($value -ne $hk.Expect) {
                Say ("    [問題] 預期 {0} ({1})，實際是 {2}。" -f $hk.Expect, (Decode-HotKey $hk.Expect), $value)

                $wantsDoubleTap = (($hk.Expect -shr 8) -band 0x80) -ne 0
                $hasDoubleTap = (($value -shr 8) -band 0x80) -ne 0
                if ($wantsDoubleTap -and -not $hasDoubleTap) {
                    Say '           少了 0x80 這個「連點兩下」旗標，所以單擊就會觸發。'
                }
            }
        }
        if ($json.HotKeys.EnableHotKeys -eq $false) {
            Say '  [問題] 全域快速鍵被停用（EnableHotKeys = False）。'
        }
        $key = $json.Advanced.OcrApiKey
        Say ("  {0,-16} = {1}" -f 'OCR 金鑰', $(if ([string]::IsNullOrEmpty($key)) { '(空白)' } else { '已填入 (' + $key.Length + ' 字元)' }))
    } catch {
        Say "[問題] JSON 解析失敗: $($_.Exception.Message)"
        Say '        QTranslate 讀不到設定就會改用預設值，離開時再把預設值寫回去，'
        Say '        看起來就像「設定每次都不見」。'
    }

    # 實際試寫
    $probeFile = Join-Path $optionsDir 'diag-write-test.tmp'
    try {
        [IO.File]::WriteAllText($probeFile, 'test')
        Remove-Item -LiteralPath $probeFile -Force
        Say '寫入測試: 可以寫入'
    } catch {
        Say "[問題] 寫入測試失敗: $($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------- 執行中的程序
Section '執行中的 QTranslate'

$procs = @(Get-Process QTranslate -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) {
    Say '目前沒有執行。（建議讓 QTranslate 開著再跑一次這個診斷）'
} else {
    Say "執行中的實體數: $($procs.Count)"
    if ($procs.Count -gt 1) {
        Say '[問題] 同時有多個 QTranslate 在執行，它們會互相覆蓋設定檔。'
    }
    foreach ($p in $procs) {
        # 這個診斷本身是以一般權限執行的。若連執行檔路徑都讀不到，
        # 代表那個處理程序的權限比我們高，也就是 QTranslate 在提權執行。
        $path = $null
        try { $path = $p.Path } catch { }

        if ($path) {
            Say "  PID $($p.Id)  $path"
        } else {
            Say "  PID $($p.Id)  (讀不到執行檔路徑)"
            Say '    [問題] QTranslate 以系統管理員身分執行。'
            Say '           如果提權時輸入的是另一個管理員帳號，設定會寫到那個帳號的'
            Say '           APPDATA，你下次以自己的帳號開啟時就讀不到，看起來就像設定不見。'
        }

        try {
            $owner = (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" -ErrorAction Stop).GetOwner()
            if ($owner.User) {
                Say "    執行帳號: $($owner.Domain)\$($owner.User)"
                if ($owner.User -ne $env:USERNAME) {
                    Say '    [問題] 執行帳號與你目前登入的帳號不同，'
                    Say '           設定會被寫到那個帳號的 APPDATA，不是你的。'
                }
            }
        } catch { }
    }
}

# ---------------------------------------------------------------- 安裝位置
Section '安裝位置'

$found = @()
foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA, $env:APPDATA)) {
    if (-not $root) { continue }
    $candidate = Join-Path $root 'QTranslate\QTranslate.exe'
    if (Test-Path -LiteralPath $candidate) {
        $v = (Get-Item -LiteralPath $candidate).VersionInfo.FileVersion
        Say "  $candidate   版本 $v"
        $found += $candidate
    }
}
if ($found.Count -eq 0) { Say '  找不到任何 QTranslate 安裝。' }
if ($found.Count -gt 1) {
    Say '[問題] 這台電腦有多份 QTranslate，可能會互相干擾。'
}

# ---------------------------------------------------------------- 開機啟動
Section '開機自動啟動'

$runKeys = @(
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')
$entries = 0
foreach ($k in $runKeys) {
    if (-not (Test-Path $k)) { continue }
    (Get-ItemProperty $k).PSObject.Properties |
        Where-Object { $_.Value -is [string] -and $_.Value -like '*QTranslate*' } |
        ForEach-Object {
            Say "  $k"
            Say "    $($_.Name) = $($_.Value)"
            $entries++
        }
}
foreach ($f in @([Environment]::GetFolderPath('Startup'), [Environment]::GetFolderPath('CommonStartup'))) {
    Get-ChildItem -LiteralPath $f -Filter '*QTrans*' -ErrorAction SilentlyContinue | ForEach-Object {
        Say "  啟動資料夾: $($_.FullName)"
        $entries++
    }
}
if ($entries -eq 0) { Say '  沒有設定開機啟動。' }
if ($entries -gt 1) { Say '[問題] 有多個開機啟動項目，可能會同時啟動多個 QTranslate。' }

# ---------------------------------------------------------------- 捷徑
Section '捷徑是否被設定為系統管理員身分執行'

$shell = New-Object -ComObject WScript.Shell
foreach ($f in @([Environment]::GetFolderPath('Desktop'),
                 (Join-Path ([Environment]::GetFolderPath('Programs')) 'QTranslate'))) {
    Get-ChildItem -LiteralPath $f -Filter '*QTrans*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {
        # 捷徑檔第 21 個位元組的 0x20 旗標代表「以系統管理員身分執行」
        $bytes = [IO.File]::ReadAllBytes($_.FullName)
        $elevated = ($bytes.Length -gt 21) -and (($bytes[21] -band 0x20) -ne 0)
        Say "  $($_.FullName)"
        Say "    以系統管理員身分執行: $elevated"
        if ($elevated) {
            Say '    [問題] 這會讓 QTranslate 以提權身分執行，設定可能寫到別的帳號底下。'
        }
    }
}

# ---------------------------------------------------------------- 收尾
Section '結果'

$problems = $lines | Where-Object { $_ -match '\[問題\]' }
if ($problems.Count -eq 0) {
    Say '沒有發現明顯問題。'
    Say '請讓 QTranslate 開著再跑一次這個診斷，並比較結束前後 Options.json 的最後修改時間。'
} else {
    Say "發現 $($problems.Count) 個可疑之處："
    $problems | ForEach-Object { Say "  $_" }
}

$report = Join-Path ([Environment]::GetFolderPath('Desktop')) 'QTranslate-診斷.txt'
try {
    Set-Content -LiteralPath $report -Value $lines -Encoding UTF8
    Say ''
    Say "報告已存到桌面: $report"
} catch {
    Say "報告存檔失敗: $($_.Exception.Message)"
}

Write-Host ''
Read-Host '按 Enter 關閉'
