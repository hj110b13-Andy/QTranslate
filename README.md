# QTranslate 修正版

修好 QTranslate 6.10.0 的翻譯功能，並改善 PDF 與螢幕辨識文字的斷句處理。

原版之所以不能用，是因為它連的 Google 端點已被停用，回傳的不是翻譯結果而是一頁
防濫用頁面，所以畫面永遠空白。這裡把它改連仍然可用的端點，並在送出前重建被硬換行
打散的段落與標題。

---

## 安裝

1. 下載安裝檔：**[QTranslate-Setup.exe](https://github.com/hj110b13-Andy/QTranslate/releases/latest/download/QTranslate-Setup.exe)**

   （或到 [Releases 頁面](https://github.com/hj110b13-Andy/QTranslate/releases/latest) 下載）

2. 雙擊執行，按「安裝」

   > 第一次執行時 Windows 會顯示「Windows 已保護您的電腦」。
   > 點「**其他資訊**」→「**仍要執行**」即可。

3. 設定選「**完整套用**」（預設值），按「安裝」
4. 安裝完成後，開啟 QTranslate →「**選項 → 進階 → OCR API key**」，
   填入 OCR 金鑰

### 這台電腦以前裝過別的 QTranslate 版本？

**一定要選「完整套用」。**

解除安裝 QTranslate **不會**刪掉 `%APPDATA%\QTranslate\Options.json`，所以
舊版的快速鍵與滑鼠設定會留在那個檔案裡繼續生效——就算把新舊版本都解除安裝、
再重新安裝也一樣。

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
| **連點兩下 Ctrl** | 框選畫面範圍 → 點「翻譯」 |
| Ctrl+Q | 選取文字 → 彈出視窗翻譯 |
| Ctrl+Alt+Q | 選取文字 → 主視窗翻譯 |
| Ctrl+E | 朗讀選取的文字 |

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

## 解除安裝

從「應用程式與功能」找到 **QTranslate 6.10.0 (修正版)** 解除安裝，
或直接再執行一次安裝檔並按「解除安裝」。

個人設定與翻譯紀錄保留在 `%APPDATA%\QTranslate`，不會被移除。

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
