# QTranslate 修正版

修好 QTranslate 6.10.0 的翻譯功能，並改善 PDF 與螢幕辨識文字的斷句處理。

原版之所以不能用，是因為它連的 Google 端點已被停用，回傳的不是翻譯結果而是一頁
防濫用頁面，所以畫面永遠空白。這裡把它改連仍然可用的端點，並在送出前重建被硬換行
打散的段落與標題。

---

## 安裝

1. 下載安裝檔：**[QTranslate-Setup.exe](https://github.com/hj110b13-Andy/QTranslate/releases/latest/download/QTranslate-Setup.exe)**

   （或到 [Releases 頁面](https://github.com/hj110b13-Andy/QTranslate/releases/latest) 下載）

2. 雙擊執行

   > 第一次執行時 Windows 會顯示「Windows 已保護您的電腦」。
   > 點「**其他資訊**」→「**仍要執行**」即可。

3. 設定選「**完整套用**」（預設值），按「安裝」

   > **v6.10.7 起，預設安裝到 `%LocalAppData%`，完全不需要跳 UAC。**
   > 如果改選「瀏覽…」把安裝位置換成 Program Files，那一步才會另外跳出
   > UAC 提權對話框，不是雙擊執行時就跳出來。多數情況直接用預設位置
   > 就好，不需要改。

4. 安裝完成後，開啟 QTranslate →「**選項 → 進階 → OCR API key**」，
   填入 OCR 金鑰

### 這台電腦以前裝過別的 QTranslate 版本？

**一定要選「完整套用」。**

解除安裝 QTranslate **不會**刪掉安裝資料夾底下的 `Data\Options.json`
（QTranslate 是可攜式風格的程式，設定跟著安裝資料夾走，不在系統的
`%APPDATA%`），所以舊版的快速鍵與滑鼠設定會留在那個檔案裡繼續生效——
就算把新舊版本都解除安裝、再重新安裝也一樣。

「完整套用」會整份覆蓋設定檔，讓每一台電腦的操作完全一致。原本的設定會先
備份成 `Options.json.before-fix`，**已經填好的 OCR 金鑰也會保留**。

其他兩個選項：

- **只套用必要設定** — 只改快速鍵、滑鼠模式等必要項目，保留這台電腦的其他
  個人設定。適合已經照自己習慣調整過、只想更新翻譯修正的機器。
- **不變更設定** — 只更新程式檔案。

金鑰到 <https://ocr.space/ocrapi> 用 email 免費申請（每月 25,000 次），
同一組金鑰可以在所有電腦上使用。**只有畫面翻譯需要金鑰**，選字翻譯不用。

安裝檔自帶 .NET 執行環境，目標電腦不需要預先安裝任何東西。其餘設定安裝時
都會自動套用好。

---

## 使用

| 操作 | 功能 |
| --- | --- |
| **按住 Ctrl + 滑鼠選取文字** | 直接跳出翻譯 |
| **Ctrl+Shift+S** | 框選畫面範圍 → 點「翻譯」 |
| Ctrl+Q | 選取文字 → 彈出視窗翻譯 |
| Ctrl+Alt+Q | 選取文字 → 主視窗翻譯 |
| Ctrl+E | 朗讀選取的文字 |

> **畫面框選翻譯用 Ctrl+Shift+S，不是「連點兩下 Ctrl」。**
> 這不是排版問題，是刻意的行為改變：QTranslate.exe 把「連點兩下 Ctrl」註冊成
> 沒有實體鍵的純修飾鍵熱鍵，靠它自己的計時邏輯判斷第二次按壓，這段邏輯在
> 部分電腦上不可靠，會讓單擊 Ctrl 就誤觸發畫面翻譯。Ctrl+Shift+S 是一般的
> 修飾鍵+按鍵組合，Windows 只有在三者同時按下才會觸發，不需要任何計時判斷，
> 不會有這個問題。**只要不把任何功能改回連點兩下 Ctrl，這個安裝檔的預設值
> 就完全不會誤觸發**——如果你自己在選項裡把某個功能手動改成連點兩下，
> 單擊 Ctrl 又會誤觸發那個功能，這是 QTranslate.exe 本體的限制，不是設定
> 檔的問題。如果你已經習慣連點兩下 Ctrl，這是要付出的代價，但目前沒有更好
> 的解法——曾經嘗試直接修補 QTranslate.exe 本體讓連點兩下變可靠，結果反而
> 讓連點兩下完全失效，已經捨棄那個方向。

程式常駐在系統列，開機自動啟動。左鍵點系統列圖示可以開關滑鼠模式。

### 關於斷句

從 PDF 複製的文字每行結尾都帶著硬換行，直接翻譯的話每一行會被當成獨立句子，
`...to power the digital / inputs.` 就會被拆成兩段不相干的譯文，標題也會被
後面的內文吞掉。

這個版本會在送出前判斷哪些換行只是排版折行、哪些是刻意的，把段落接回去、
標題獨立出來。所以譯文的結構是對的，**但主視窗上半部顯示的原文仍會保留
PDF 原本的斷行**——那只是顯示，不影響翻譯結果。

> **請勿開啟「選項 → 進階 → 移除換行字元」。**
> 那個選項會在文字進入程式時就把換行全部處理掉，標題與段落會黏在一起，
> 而且無法補救。

---

## 疑難排解：裝完之後設定套用不上，或每次重開就不見了

**v6.10.8 已經修好這個問題。** 根因其實很單純：QTranslate 是可攜式風格的
程式，它把 `Options.json` 存在**安裝資料夾底下的 `Data\` 子資料夾**（例如
`%LocalAppData%\QTranslate\Data\Options.json`），完全不使用系統的
`%APPDATA%` 漫遊路徑。v6.10.8 之前，安裝程式一直把設定寫進
`%APPDATA%\QTranslate\Options.json`——一個 QTranslate.exe 從來不會去讀的
檔案，所以不管安裝程式寫得再對，QTranslate 開啟時一律套用內建出廠預設值，
使用者自己在選項裡調整過的設定，關閉程式後也不會被存回這個 exe 真正在讀
的檔案，看起來就像「設定套用不上」或「重開就不見了」。

如果你是 v6.10.7 或更早的版本遇到這個問題，**更新到 v6.10.8、選「完整
套用」重裝一次**就會修好，不需要再做任何額外的排查。安裝完之後打開
「選項 → 快速鍵」，「顯示主視窗」應該顯示 `Ctrl+Alt+Q`（不是
`Double Ctrl`），「文字辨識」應該顯示 `Ctrl+Shift+S`（不是空白），這樣
就代表設定確實生效了。

如果更新到 v6.10.8 之後還是有問題，在那台電腦上下載這**兩個**檔案，
**放在同一個資料夾**，然後雙擊 `Diagnose.cmd`：

- **[Diagnose.cmd](https://github.com/hj110b13-Andy/QTranslate/releases/latest/download/Diagnose.cmd)**
- **[Diagnose.ps1](https://github.com/hj110b13-Andy/QTranslate/releases/latest/download/Diagnose.ps1)**

`Diagnose.cmd` 只是個啟動器，實際的檢查在 `Diagnose.ps1` 裡，**少一個就跑不起來**。
放哪個資料夾都可以（桌面、下載資料夾都行），只要兩個在一起。

**不需要系統管理員權限**——而且請不要用系統管理員身分執行，否則偵測會失準。

它會檢查：

- 先定位 QTranslate 的安裝位置，再檢查該位置底下 `Data\Options.json`
  是否存在、可否寫入、內容是不是合法 JSON、快速鍵是否符合預期
- QTranslate 是否以系統管理員身分執行（提權時若用了別的管理員帳號，
  設定會寫到那個帳號的個人資料夾）
- 是否同時有多個 QTranslate 在執行，互相覆蓋設定
- 是否有多份安裝、多個開機啟動項目
- 捷徑是否被設定成「以系統管理員身分執行」

結果會存成桌面上的 `QTranslate-診斷.txt`。

## 解除安裝

從「應用程式與功能」找到 **QTranslate 6.10.0 (修正版)** 解除安裝，
或直接再執行一次安裝檔並按「解除安裝」。

個人設定與翻譯紀錄保留在安裝資料夾底下的 `Data\` 子資料夾，不會被移除
（解除安裝只清掉程式檔案，會跳過 `Data\` 這個子資料夾）。

### 已經裝了原版，要先解除安裝嗎？

**不用。** 直接裝上去即可，安裝程式會接手：

- 移除原版在「應用程式與功能」裡的項目，所以清單裡**只會有一筆**
- 移除原版的 `Uninstall.exe`，避免兩個互相衝突的移除途徑
  （原版的解除安裝會把整個資料夾刪掉，連帶讓這裡的解除安裝失效）

唯一需要手動處理的情況：那台電腦有**第二份 QTranslate 裝在不同資料夾**。
兩份都可能被開機自動啟動拉起來，同時執行會互相覆蓋設定檔。
`Diagnose.cmd` 會偵測並回報這種情形。

---

## 其他

- **已經裝好 QTranslate、只想更新翻譯修正**：對 `Fix-QTranslate.cmd` 按右鍵
  →「以系統管理員身分執行」。它只替換一個檔案，不動任何設定。
  `Restore-Original.cmd` 可以還原。
- **技術細節**：見 [docs/技術細節.md](docs/技術細節.md)

### 重新打包與發佈新版

改過 `patch/Google Translate/Service.js` 之後：

```powershell
.\build-installer.ps1        # 需要 .NET SDK 9
```

新的安裝檔會產生在 `dist\`。要讓其他電腦拿到，**發成一個新的 Release**，
不要只依賴 repo 裡的檔案：

```bash
git tag v6.10.2
git push origin v6.10.2
# 到 GitHub 的 Releases 頁面建立 release，把 dist\QTranslate-Setup.exe 拖上去
```

Release 的附件有固定的直接下載網址、不需要 Git LFS，而且**不計入 LFS 的
1 GB 額度**。

> `dist/QTranslate-Setup.exe` 這個檔案在 repo 裡由 Git LFS 管理。
> 從網頁下載它要點進檔案頁面按 Download 按鈕——直接用
> `raw.githubusercontent.com` 的連結只會拿到一個 133 位元組的指標檔。
> 用 Release 的下載連結就沒有這個問題，所以上面才建議走 Release。

QTranslate 是 QuestSoft 的免費軟體，著作權屬於原作者。
官方網站：<https://quest-app.appspot.com>
