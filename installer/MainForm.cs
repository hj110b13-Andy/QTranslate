namespace QTranslateFix;

sealed class MainForm : Form
{
    readonly bool _uninstallMode;

    readonly TextBox _folder = new();
    readonly Button _browse = new();
    readonly CheckBox _startMenu = new();
    readonly CheckBox _desktop = new();
    readonly CheckBox _startup = new();
    readonly RadioButton _settingsReplace = new();
    readonly RadioButton _settingsMerge = new();
    readonly RadioButton _settingsNone = new();
    readonly GroupBox _optionsBox = new();
    readonly GroupBox _settingsBox = new();
    readonly Button _primary = new();
    readonly Button _uninstall = new();
    readonly TextBox _log = new();

    public MainForm(bool uninstallMode)
    {
        _uninstallMode = uninstallMode;

        Text = uninstallMode ? "QTranslate 解除安裝" : "QTranslate 安裝程式";
        Font = new Font("Microsoft JhengHei UI", 9.75f);
        ClientSize = new Size(660, 700);
        MinimumSize = new Size(600, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;

        BuildLayout();
        Prefill();
    }

    void BuildLayout()
    {
        var title = new Label
        {
            Text = Deployer.DisplayName,
            Font = new Font("Microsoft JhengHei UI", 15f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 20),
        };

        var blurb = new Label
        {
            Text = "已修好 Google 翻譯（原本的連線端點被 Google 停用），並改善 PDF 與\n"
                 + "螢幕辨識文字的斷句、段落與標題處理。",
            AutoSize = false,
            Size = new Size(610, 44),
            Location = new Point(26, 56),
            ForeColor = Color.FromArgb(90, 90, 90),
        };

        var folderLabel = new Label
        {
            Text = "安裝位置",
            AutoSize = true,
            Location = new Point(24, 112),
        };

        _folder.Location = new Point(24, 134);
        _folder.Size = new Size(506, 26);
        _folder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _browse.Text = "瀏覽…";
        _browse.Location = new Point(540, 133);
        _browse.Size = new Size(96, 28);
        _browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _browse.Click += (_, _) => Browse();

        _optionsBox.Text = "捷徑與啟動";
        _optionsBox.Location = new Point(24, 176);
        _optionsBox.Size = new Size(612, 108);
        _optionsBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _startMenu.Text = "建立開始功能表捷徑";
        _startMenu.AutoSize = true;
        _startMenu.Location = new Point(16, 26);

        _desktop.Text = "建立桌面捷徑";
        _desktop.AutoSize = true;
        _desktop.Location = new Point(16, 50);

        _startup.Text = "開機時自動啟動（最小化到系統列）";
        _startup.AutoSize = true;
        _startup.Location = new Point(16, 74);

        _optionsBox.Controls.AddRange(new Control[] { _startMenu, _desktop, _startup });

        _settingsBox.Text = "設定";
        _settingsBox.Location = new Point(24, 296);
        _settingsBox.Size = new Size(612, 112);
        _settingsBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _settingsReplace.Text = "完整套用（覆蓋這台電腦既有的設定）";
        _settingsReplace.AutoSize = true;
        _settingsReplace.Location = new Point(16, 26);

        _settingsMerge.Text = "只套用必要設定（保留這台電腦的其他設定）";
        _settingsMerge.AutoSize = true;
        _settingsMerge.Location = new Point(16, 52);

        _settingsNone.Text = "不變更設定";
        _settingsNone.AutoSize = true;
        _settingsNone.Location = new Point(16, 78);

        _settingsBox.Controls.AddRange(new Control[] { _settingsReplace, _settingsMerge, _settingsNone });

        var hint = new Label
        {
            Text = "曾經裝過其他版本的 QTranslate 時請選「完整套用」——解除安裝不會清掉設定檔，\n"
                 + "舊版的快速鍵會留下來。原設定會先備份，已填好的 OCR 金鑰不會被清除。",
            AutoSize = false,
            Size = new Size(612, 44),
            Location = new Point(26, 414),
            ForeColor = Color.FromArgb(120, 120, 120),
        };

        _primary.Text = _uninstallMode ? "解除安裝" : "安裝";
        _primary.Size = new Size(130, 38);
        _primary.Location = new Point(24, 462);
        _primary.Click += (_, _) => Run(_uninstallMode);

        _uninstall.Text = "解除安裝";
        _uninstall.Size = new Size(130, 38);
        _uninstall.Location = new Point(164, 462);
        _uninstall.Visible = !_uninstallMode;
        _uninstall.Click += (_, _) => Run(uninstall: true);

        var close = new Button
        {
            Text = "關閉",
            Size = new Size(100, 38),
            Location = new Point(536, 462),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        close.Click += (_, _) => Close();

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.FromArgb(248, 248, 248);
        _log.Font = new Font("Consolas", 9.5f);
        _log.Location = new Point(24, 512);
        _log.Size = new Size(612, 166);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        Controls.AddRange(new Control[]
        {
            title, blurb, folderLabel, _folder, _browse, _optionsBox, _settingsBox, hint,
            _primary, _uninstall, close, _log,
        });
    }

    void Prefill()
    {
        var existing = QTranslateLocator.Find();
        // An existing install (found via HKCU or the stock HKLM entry) always
        // wins, so upgrades land where the program already is. A brand new
        // install defaults to %LocalAppData% - writable without elevation -
        // rather than Program Files. Program Files is still one click away
        // via "瀏覽…" for anyone who wants a machine-wide install.
        _folder.Text = existing ?? QTranslateLocator.DefaultInstallFolder();

        _startMenu.Checked = !_uninstallMode;
        _desktop.Checked = false;
        _startup.Checked = !_uninstallMode;
        _settingsReplace.Checked = true;

        if (_uninstallMode)
        {
            _optionsBox.Enabled = false;
            _settingsBox.Enabled = false;
            _browse.Enabled = false;
            _folder.ReadOnly = true;
            Log("準備解除安裝。確認上方位置無誤後按「解除安裝」。");
            return;
        }

        Log(existing is null
            ? "這台電腦還沒有 QTranslate，將進行全新安裝。"
            : "偵測到既有安裝，將直接覆蓋更新：" + existing);
        Log("確認選項後按「安裝」。");
    }

    OptionsPatcher.Mode? SelectedMode()
    {
        if (_settingsReplace.Checked)
        {
            return OptionsPatcher.Mode.Replace;
        }
        if (_settingsMerge.Checked)
        {
            return OptionsPatcher.Mode.Merge;
        }
        return null;
    }

    void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "選擇 QTranslate 的安裝位置",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_folder.Text) ? _folder.Text : "",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folder.Text = dialog.SelectedPath;
        }
    }

    void Run(bool uninstall)
    {
        var folder = _folder.Text.Trim();
        if (string.IsNullOrEmpty(folder))
        {
            MessageBox.Show(this, "請先指定安裝位置。", "缺少資訊", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (uninstall)
        {
            var confirm = MessageBox.Show(this,
                $"將移除 QTranslate 程式檔案、捷徑與開機啟動設定：\n\n{folder}\n\n"
                + "你的設定與翻譯紀錄會保留。確定要繼續嗎？",
                "確認解除安裝", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        SetBusy(true);
        try
        {
            Log("");
            var deployer = new Deployer(Log);

            if (uninstall)
            {
                deployer.Uninstall(folder);
            }
            else
            {
                deployer.Install(folder, new Deployer.Options(
                    DesktopShortcut: _desktop.Checked,
                    StartMenuShortcut: _startMenu.Checked,
                    RunAtStartup: _startup.Checked,
                    SettingsMode: SelectedMode()));
            }
        }
        catch (Exception ex)
        {
            Log("");
            Log("失敗：" + ex.Message);
            MessageBox.Show(this, ex.Message, "發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        _primary.Enabled = !busy;
        _uninstall.Enabled = !busy;
        _browse.Enabled = !busy && !_uninstallMode;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        Application.DoEvents();
    }

    void Log(string line)
    {
        _log.AppendText(line + Environment.NewLine);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        Application.DoEvents();
    }
}
