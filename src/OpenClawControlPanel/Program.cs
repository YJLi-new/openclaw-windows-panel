using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace OpenClawControlPanel
{
    internal static class Program
    {
        private const string DefaultGatewayRootUrl = "http://127.0.0.1:18790/";
        private const string DefaultDockerComposeCommand = "docker compose";
        private const string DefaultGatewayServiceName = "openclaw-gateway";
        private const string DefaultWslDockerHost = "unix:///var/run/docker.sock";
        private const int SettingsSchemaVersion = 2;
        private const string DefaultDashboardBrowserTarget = "auto";
        private const string DefaultWslChromeBackend = "wayland";
        private const string DefaultWslNativeOpenclawCommand = "openclaw";
        private const string DefaultWinNativeOpenclawCommand = "openclaw";
        private static readonly string AppBaseDirWindows = DetermineAppBaseDirWindows();
        private static readonly string DefaultBaseDirWindows = AppBaseDirWindows;
        private static readonly string DefaultBaseDirWsl = ConvertWindowsPathToWsl(AppBaseDirWindows);
        private static readonly string SettingsFileWindows = Path.Combine(AppBaseDirWindows, "openclaw-control-panel-settings.json");
        private static readonly string ErrorLogFileWindows = Path.Combine(AppBaseDirWindows, "openclaw-control-panel-error.log");
        private static readonly string IconFileWindows = Path.Combine(AppBaseDirWindows, "openclaw-control-panel.ico");
        private static readonly object ErrorLogLock = new object();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(PreferredAppMode appMode);

        private enum PreferredAppMode
        {
            Default = 0,
            AllowDark = 1,
            ForceDark = 2,
            ForceLight = 3,
            Max = 4
        }

        private static readonly IntPtr DpiAwarenessContextPerMonitorV2 = new IntPtr(-4);

        [STAThread]
        private static void Main()
        {
            try
            {
                ConfigureDpiAwareness();
                TryEnableProcessDarkMode();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    TryWriteErrorLog("ui-thread-unhandled", e.Exception);
                    try
                    {
                        MessageBox.Show(
                            "OpenClaw UI crashed on the UI thread.\n\n" + e.Exception.Message +
                            "\n\nSee: " + ErrorLogFileWindows,
                            "OpenClaw Console",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    catch
                    {
                    }
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    if (ex == null)
                    {
                        ex = new Exception("Unhandled non-Exception object: " + (e.ExceptionObject ?? "null"));
                    }
                    TryWriteErrorLog("appdomain-unhandled", ex);
                };
                TryWriteDiagnostic("startup", "application starting");
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                TryWriteErrorLog("main-fatal", ex);
                MessageBox.Show(
                    "OpenClaw console failed to start.\n\n" + ex.Message +
                    "\n\nSee: " + ErrorLogFileWindows,
                    "OpenClaw Console",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void ConfigureDpiAwareness()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    if (SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorV2))
                    {
                        return;
                    }
                }
            }
            catch
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }

        private static void TryEnableProcessDarkMode()
        {
            try
            {
                SetPreferredAppMode(PreferredAppMode.AllowDark);
            }
            catch
            {
            }
        }

        private static string DetermineAppBaseDirWindows()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(baseDir))
                {
                    return TrimTrailingPathSeparators(baseDir);
                }
            }
            catch
            {
            }

            try
            {
                string currentDir = Environment.CurrentDirectory;
                if (!string.IsNullOrWhiteSpace(currentDir))
                {
                    return TrimTrailingPathSeparators(currentDir);
                }
            }
            catch
            {
            }

            return ".";
        }

        private static string TrimTrailingPathSeparators(string path)
        {
            string value = (path ?? string.Empty).Trim();
            while (value.Length > 3 &&
                   (value.EndsWith("\\", StringComparison.Ordinal) || value.EndsWith("/", StringComparison.Ordinal)))
            {
                value = value.Substring(0, value.Length - 1);
            }
            return value;
        }

        private static string ConvertWindowsPathToWsl(string path)
        {
            string value = TrimTrailingPathSeparators(path).Replace('/', '\\');
            if (value.Length >= 3 &&
                char.IsLetter(value[0]) &&
                value[1] == ':' &&
                (value[2] == '\\' || value[2] == '/'))
            {
                string drive = char.ToLowerInvariant(value[0]).ToString();
                string rest = value.Substring(3).Replace('\\', '/');
                return rest.Length == 0 ? "/mnt/" + drive : "/mnt/" + drive + "/" + rest;
            }
            return "/mnt/e/OPC";
        }

        private static IEnumerable<string> EnumerateErrorLogPaths()
        {
            yield return ErrorLogFileWindows;
            string fallback = null;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(baseDir))
                {
                    fallback = Path.Combine(baseDir, "openclaw-control-panel-error.log");
                }
            }
            catch
            {
                fallback = null;
            }

            if (!string.IsNullOrWhiteSpace(fallback) &&
                !string.Equals(fallback, ErrorLogFileWindows, StringComparison.OrdinalIgnoreCase))
            {
                yield return fallback;
            }
        }

        private static void TryWriteLogLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            try
            {
                lock (ErrorLogLock)
                {
                    foreach (string path in EnumerateErrorLogPaths())
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(path);
                            if (!string.IsNullOrWhiteSpace(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            File.AppendAllText(path, text + Environment.NewLine, Encoding.UTF8);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryWriteErrorLog(string tag, Exception ex)
        {
            var safe = ex ?? new Exception("Unknown error.");
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string label = string.IsNullOrWhiteSpace(tag) ? "error" : tag;
            TryWriteLogLine(ts + " [" + label + "] " + safe);
        }

        private static void TryWriteDiagnostic(string tag, string message)
        {
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string label = string.IsNullOrWhiteSpace(tag) ? "diag" : tag;
            string msg = string.IsNullOrWhiteSpace(message) ? "-" : message;
            TryWriteLogLine(ts + " [" + label + "] " + msg);
        }

        private sealed class MainForm : Form
        {
            private enum UiLanguage
            {
                English,
                ChineseSimplified,
                ChineseTraditional
            }

            private enum UiTheme
            {
                Light,
                Dark
            }

            private enum DeploymentMode
            {
                Auto,
                WslDocker,
                WslNative,
                WinDocker,
                WinNative
            }

            private enum ProxyMode
            {
                System,
                Off,
                WslClashAuto,
                Custom
            }

            private enum CommandRuntime
            {
                WslBash,
                WindowsPowerShell
            }

            private UiLanguage _language = UiLanguage.English;
            private UiTheme _theme = UiTheme.Light;
            private int _settingsSchemaVersion = SettingsSchemaVersion;
            private bool _autoDetectMode = true;
            private DeploymentMode _manualDeploymentMode = DeploymentMode.WslDocker;
            private DeploymentMode _lastDetectedMode = DeploymentMode.WslDocker;
            private DeploymentMode _effectiveMode = DeploymentMode.WslDocker;
            private ProxyMode _proxyMode = ProxyMode.WslClashAuto;
            private string _customHttpProxy = string.Empty;
            private string _customHttpsProxy = string.Empty;
            private string _customAllProxy = string.Empty;
            private string _customNoProxy = "localhost,127.0.0.1,::1";
            private string _wslSudoPasswordProtected = string.Empty;
            private string _dashboardBrowserTarget = DefaultDashboardBrowserTarget;
            private string _wslChromeBackend = DefaultWslChromeBackend;
            private string _windowsProjectDir = DefaultBaseDirWindows;
            private string _wslProjectDir = DefaultBaseDirWsl;
            private string _wslOpenclawDir = DefaultBaseDirWsl + "/openclaw";
            private string _wslDataDir = DefaultBaseDirWsl + "/openclaw-data";
            private string _wslStartScriptPath = DefaultBaseDirWsl + "/openclaw-start-fast.sh";
            private string _wslOpenDashboardScriptPath = DefaultBaseDirWsl + "/openclaw-open-dashboard-wsl.sh";
            private string _wslNativeProjectDir = DefaultBaseDirWsl + "/openclaw";
            private string _wslNativeOpenclawCommand = DefaultWslNativeOpenclawCommand;
            private string _wslNativeInstallCommand = "curl -fsSL https://openclaw.ai/install.sh | bash";
            private string _winDockerOpenclawDir = DefaultBaseDirWindows + "\\openclaw";
            private string _winDockerDataDir = DefaultBaseDirWindows + "\\openclaw-data";
            private string _winNativeProjectDir = DefaultBaseDirWindows;
            private string _winNativeOpenclawCommand = DefaultWinNativeOpenclawCommand;
            private string _winNativeInstallCommand = "iwr -useb https://openclaw.ai/install.ps1 | iex";
            private string _gatewayRootUrl = DefaultGatewayRootUrl;
            private string _dockerComposeCommand = DefaultDockerComposeCommand;
            private string _gatewayServiceName = DefaultGatewayServiceName;

            private readonly Label _titleLabel;
            private readonly Label _subtitleLabel;
            private readonly Label _footerLabel;
            private readonly Label _modeTopLabel;
            private readonly Label _logTitleLabel;
            private readonly Button _statusBadge;
            private readonly Label _statusHint;
            private readonly RichTextBox _logBox;
            private readonly ProgressBar _progressBar;
            private readonly Button _btnStart;
            private readonly Button _btnStop;
            private readonly Button _btnOpenDashboard;
            private readonly Button _btnCheck;
            private readonly Button _btnSettings;
            private readonly Panel _headerPanel;
            private readonly Panel _heroCardPanel;
            private readonly Panel _actionsPanel;
            private readonly Panel _logCardPanel;
            private readonly Timer _busyTimer;
            private string _busyText = "";
            private int _busyTick;
            private int _settingsDialogAttemptCounter;
            private string _statusTitleEnglish = "Idle";
            private string _statusTitleChinese = "待检查";
            private string _statusDetailEnglish = "Click 'Check Status/Health' first.";
            private string _statusDetailChinese = "请先点击“检查状态/健康度”。";

            private bool _busy;
            private bool _initialRevealDone;
            private int _windowCornerRadius = 18;
            private Color _cardBackgroundColor = Color.White;
            private Color _cardBorderColor = Color.FromArgb(220, 226, 236);
            private Color _headerGradientStartColor = Color.FromArgb(24, 52, 122);
            private Color _headerGradientEndColor = Color.FromArgb(14, 120, 155);
            private Color _logTimestampColor = Color.FromArgb(241, 217, 104);
            private Color _logTextColor = Color.FromArgb(214, 227, 255);
            private Color _logSuccessColor = Color.FromArgb(124, 214, 142);
            private Color _logErrorColor = Color.FromArgb(242, 131, 131);
            private Color _logMutedColor = Color.FromArgb(176, 187, 205);
            private StatusTone _statusTone = StatusTone.Neutral;
            private static readonly byte[] WslSudoPasswordEntropy = Encoding.UTF8.GetBytes("OpenClawControlPanel.WslSudoPassword.v1");

            [DllImport("dwmapi.dll")]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool SetWindowPos(
                IntPtr hWnd,
                IntPtr hWndInsertAfter,
                int X,
                int Y,
                int cx,
                int cy,
                uint uFlags);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);

            [DllImport("user32.dll")]
            private static extern bool ReleaseCapture();

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

            [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
            private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

            [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
            private static extern int SetPreferredAppMode(PreferredAppMode appMode);

            private const int DwmaUseImmersiveDarkMode = 20;
            private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;
            private const int DwmaBorderColor = 34;
            private const int DwmaCaptionColor = 35;
            private const int DwmaTextColor = 36;
            private const uint SwpNoSize = 0x0001;
            private const uint SwpNoMove = 0x0002;
            private const uint SwpNoZOrder = 0x0004;
            private const uint SwpNoActivate = 0x0010;
            private const uint SwpFrameChanged = 0x0020;
            private const int WsExComposited = 0x02000000;
            private const int WmNCHitTest = 0x0084;
            private const int WmNCLButtonDown = 0x00A1;
            private const int HtClient = 1;
            private const int HtCaption = 2;
            private const int HtLeft = 10;
            private const int HtRight = 11;
            private const int HtTop = 12;
            private const int HtTopLeft = 13;
            private const int HtTopRight = 14;
            private const int HtBottom = 15;
            private const int HtBottomLeft = 16;
            private const int HtBottomRight = 17;
            private const int ResizeBorder = 8;
            private const int WmThemeChanged = 0x031A;
            private const int EmSetCueBanner = 0x1501;

            private enum PreferredAppMode
            {
                Default = 0,
                AllowDark = 1,
                ForceDark = 2,
                ForceLight = 3,
                Max = 4
            }

            private enum StatusTone
            {
                Neutral,
                Good,
                Warn,
                Bad
            }

            private sealed class ActionButtonVisual
            {
                public string Text = string.Empty;
                public Color FillColor = Color.LightGray;
                public bool ParseLeadingIcon = true;
                public bool EnableHoverEffects = true;
                public bool DrawShadow = true;
                public int CornerRadius = 12;
                public float IconSizeDelta = 2F;
                public FontStyle TextStyle = FontStyle.Bold;
                public bool UseCenterTextLayout = false;
                public float CenterOffsetX = 0F;
                public float CenterOffsetY = 0F;
            }

            private sealed class CommandSpec
            {
                public CommandRuntime Runtime;
                public string Command = string.Empty;
                public int TimeoutSeconds = 60;
                public string ActionName = string.Empty;
                public DeploymentMode Mode;
            }

            private sealed class CommandResult
            {
                public int ExitCode;
                public string Output;
            }

            private sealed class EnumComboItem<T>
            {
                public T Value;
                public string Label;

                public EnumComboItem(T value, string label)
                {
                    Value = value;
                    Label = label ?? string.Empty;
                }

                public override string ToString()
                {
                    return Label ?? string.Empty;
                }
            }

            public MainForm()
            {
                Text = "OpenClaw Windows Console";
                StartPosition = FormStartPosition.CenterScreen;
                MinimumSize = new Size(980, 700);
                Size = new Size(1060, 760);
                BackColor = Color.FromArgb(242, 245, 250);
                Font = new Font("Segoe UI", 10F, FontStyle.Regular);
                Padding = new Padding(10);
                FormBorderStyle = FormBorderStyle.Sizable;
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                UpdateStyles();
                Opacity = 0D;
                AutoScaleMode = AutoScaleMode.Dpi;
                AutoScaleDimensions = new SizeF(96F, 96F);
                Paint += MainFormPaint;
                try
                {
                    string iconPath = IconFileWindows;
                    if (File.Exists(iconPath))
                    {
                        Icon = new Icon(iconPath);
                    }
                }
                catch
                {
                }

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 4,
                    BackColor = Color.Transparent
                };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));

                _headerPanel = BuildHeader(out _titleLabel, out _subtitleLabel, out _modeTopLabel, out _btnSettings, out _heroCardPanel);
                _actionsPanel = BuildActions(out _btnStart, out _btnStop, out _btnOpenDashboard, out _btnCheck, out _progressBar);
                _logCardPanel = BuildLogCard(out _logTitleLabel);

                _footerLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(92, 103, 126),
                    Text = "Project: " + _windowsProjectDir + "    Gateway: " + NormalizeGatewayRootUrl(_gatewayRootUrl)
                };

                root.Controls.Add(_headerPanel, 0, 0);
                root.Controls.Add(_actionsPanel, 0, 1);
                root.Controls.Add(_logCardPanel, 0, 2);
                root.Controls.Add(_footerLabel, 0, 3);

                Controls.Add(root);
                EnableDoubleBuffering(root);

                _statusBadge = (Button)_headerPanel.Tag;
                _statusHint = (Label)_actionsPanel.Tag;
                _logBox = (RichTextBox)_logCardPanel.Tag;
                _busyTimer = new Timer();
                _busyTimer.Interval = 900;
                _busyTimer.Tick += BusyTimerOnTick;

                _btnSettings.Click += delegate
                {
                    int attemptId = ++_settingsDialogAttemptCounter;
                    Program.TryWriteDiagnostic("settings-click", "attempt=" + attemptId + " button clicked");
                    try
                    {
                        ShowSettingsDialog(attemptId);
                        Program.TryWriteDiagnostic("settings-click", "attempt=" + attemptId + " dialog closed normally");
                    }
                    catch (Exception ex)
                    {
                        AppendLog(Tr("Settings dialog crashed: ", "设置窗口崩溃：") + ex.Message);
                        AppendLog(Tr("Error log: ", "错误日志：") + ErrorLogFileWindows);
                        Program.TryWriteErrorLog("settings-dialog-crash-attempt-" + attemptId, ex);
                        MessageBox.Show(
                            Tr("Settings failed to open. Please check:\n", "设置窗口打开失败，请查看：\n") +
                            ErrorLogFileWindows + "\n\n" + ex.Message,
                            Tr("Settings Error", "设置错误"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                };

                LoadPreferences();
                ApplyThemeVisuals();
                ApplyLanguageTexts();
                SetStatusNeutral("Idle", "待检查", "Click 'Check Status/Health' first.", "请先点击“检查状态/健康度”。");
                AppendLog(Tr("Console started. Project dir: ", "控制台已启动。项目目录：") + _windowsProjectDir);
                AppendLog(Tr("Dashboard: ", "面板地址：") + BuildDashboardChatUrl());
                AppendLog(
                    Tr("Detected mode: ", "检测到模式：") +
                    Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)) +
                    (_autoDetectMode ? Tr(" (auto)", "（自动）") : Tr(" (manual)", "（手动）")));
                Shown += delegate
                {
                    UpdateWindowRoundRegion();
                    RevealWindowAfterInitialLayout();
                };
                Resize += delegate
                {
                    UpdateWindowRoundRegion();
                };
            }

            private void RevealWindowAfterInitialLayout()
            {
                if (_initialRevealDone)
                {
                    return;
                }
                _initialRevealDone = true;

                BeginInvoke((Action)delegate
                {
                    try
                    {
                        PerformLayout();
                        Refresh();
                        Update();
                    }
                    finally
                    {
                        if (Opacity < 1D)
                        {
                            Opacity = 1D;
                        }
                    }

                    if (!_busy)
                    {
                        CheckHealth();
                    }
                });
            }

            private static void EnableDoubleBuffering(Control control)
            {
                if (control == null)
                {
                    return;
                }

                if (!SystemInformation.TerminalServerSession)
                {
                    try
                    {
                        typeof(Control).InvokeMember(
                            "DoubleBuffered",
                            System.Reflection.BindingFlags.SetProperty |
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic,
                            null,
                            control,
                            new object[] { true });
                    }
                    catch
                    {
                    }
                }

                foreach (Control child in control.Controls)
                {
                    EnableDoubleBuffering(child);
                }
            }

            private void BusyTimerOnTick(object sender, EventArgs e)
            {
                if (!_busy || string.IsNullOrWhiteSpace(_busyText))
                {
                    return;
                }
                _busyTick = (_busyTick + 1) % 4;
                _statusHint.Text = _busyText + new string('.', _busyTick);
            }

            private string Tr(string english, string chinese)
            {
                if (_language == UiLanguage.English)
                {
                    return english;
                }
                if (_language == UiLanguage.ChineseTraditional)
                {
                    return ConvertToTraditionalChinese(chinese);
                }
                return chinese;
            }

            private static string ConvertToTraditionalChinese(string input)
            {
                string text = input ?? string.Empty;
                if (text.Length == 0)
                {
                    return text;
                }

                string[,] replacements = new string[,]
                {
                    { "简体中文", "簡體中文" },
                    { "繁体中文", "繁體中文" },
                    { "设置", "設定" },
                    { "设置窗口", "設定視窗" },
                    { "语言", "語言" },
                    { "主题", "主題" },
                    { "路径", "路徑" },
                    { "模式", "模式" },
                    { "网关", "網關" },
                    { "状态", "狀態" },
                    { "检查", "檢查" },
                    { "系统", "系統" },
                    { "项目", "專案" },
                    { "启动", "啟動" },
                    { "已启动", "已啟動" },
                    { "停止", "停止" },
                    { "应用", "套用" },
                    { "运行", "執行" },
                    { "执行", "執行" },
                    { "初始化", "初始化" },
                    { "当前", "目前" },
                    { "检测", "偵測" },
                    { "自动", "自動" },
                    { "网络", "網路" },
                    { "代理", "代理" },
                    { "输出", "輸出" },
                    { "错误", "錯誤" },
                    { "失败", "失敗" },
                    { "无", "無" },
                    { "关闭", "關閉" },
                    { "字体", "字體" },
                    { "浅色", "淺色" },
                    { "深色", "深色" },
                    { "已切换", "已切換" },
                    { "为中文", "為中文" },
                    { "已更新", "已更新" },
                    { "请先", "請先" },
                    { "请查看", "請查看" },
                    { "没", "沒" },
                    { "还", "還" },
                    { "连", "連" },
                    { "达", "達" },
                    { "后", "後" },
                    { "仅", "僅" },
                    { "读", "讀" },
                    { "写", "寫" },
                    { "选项", "選項" },
                    { "常规", "一般" },
                    { "提示", "提示" },
                    { "如果", "如果" },
                    { "无法", "無法" },
                    { "版本", "版本" },
                    { "键", "鍵" },
                    { "日志", "日誌" },
                    { "内容", "內容" },
                    { "复制", "複製" },
                    { "连接", "連線" },
                    { "连通", "連通" },
                    { "确定", "確定" },
                    { "密码", "密碼" }
                };

                int length = replacements.GetLength(0);
                for (int i = 0; i < length; i++)
                {
                    text = text.Replace(replacements[i, 0], replacements[i, 1]);
                }
                return text;
            }

            private void LoadPreferences()
            {
                _language = UiLanguage.English;
                _theme = UiTheme.Light;
                _settingsSchemaVersion = SettingsSchemaVersion;
                _autoDetectMode = true;
                _manualDeploymentMode = DeploymentMode.WslDocker;
                _lastDetectedMode = DeploymentMode.WslDocker;
                _effectiveMode = DeploymentMode.WslDocker;
                _proxyMode = ProxyMode.WslClashAuto;
                _customHttpProxy = string.Empty;
                _customHttpsProxy = string.Empty;
                _customAllProxy = string.Empty;
                _customNoProxy = "localhost,127.0.0.1,::1";
                _wslSudoPasswordProtected = string.Empty;
                _dashboardBrowserTarget = DefaultDashboardBrowserTarget;
                _wslChromeBackend = DefaultWslChromeBackend;
                _windowsProjectDir = NormalizeWindowsPath(DefaultBaseDirWindows);
                _wslProjectDir = NormalizeWslPath(DefaultBaseDirWsl);
                _wslOpenclawDir = NormalizeWslPath(_wslProjectDir + "/openclaw");
                _wslDataDir = NormalizeWslPath(_wslProjectDir + "/openclaw-data");
                _wslStartScriptPath = NormalizeWslPath(_wslProjectDir + "/openclaw-start-fast.sh");
                _wslOpenDashboardScriptPath = NormalizeWslPath(_wslProjectDir + "/openclaw-open-dashboard-wsl.sh");
                _wslNativeProjectDir = NormalizeWslPath(DefaultBaseDirWsl + "/openclaw");
                _wslNativeOpenclawCommand = NormalizeCommandText(DefaultWslNativeOpenclawCommand, DefaultWslNativeOpenclawCommand);
                _wslNativeInstallCommand = "curl -fsSL https://openclaw.ai/install.sh | bash";
                _winDockerOpenclawDir = NormalizeWindowsPath(DefaultBaseDirWindows + "\\openclaw");
                _winDockerDataDir = NormalizeWindowsPath(DefaultBaseDirWindows + "\\openclaw-data");
                _winNativeProjectDir = NormalizeWindowsPath(DefaultBaseDirWindows);
                _winNativeOpenclawCommand = NormalizeCommandText(DefaultWinNativeOpenclawCommand, DefaultWinNativeOpenclawCommand);
                _winNativeInstallCommand = "iwr -useb https://openclaw.ai/install.ps1 | iex";
                _gatewayRootUrl = NormalizeGatewayRootUrl(DefaultGatewayRootUrl);
                _dockerComposeCommand = NormalizeDockerComposeCommand(DefaultDockerComposeCommand);
                _gatewayServiceName = NormalizeGatewayServiceName(DefaultGatewayServiceName);
                try
                {
                    if (!File.Exists(SettingsFileWindows))
                    {
                        return;
                    }

                    string raw = File.ReadAllText(SettingsFileWindows);
                    _settingsSchemaVersion = Math.Max(1, ExtractJsonInt(raw, "schema_version", 1));

                    string languageValue = ExtractJsonString(raw, "language").ToLowerInvariant();
                    string themeValue = ExtractJsonString(raw, "theme").ToLowerInvariant();
                    if (languageValue == "zh" || languageValue == "zh-hans" || languageValue == "zh_cn")
                    {
                        _language = UiLanguage.ChineseSimplified;
                    }
                    else if (languageValue == "zh-hant" || languageValue == "zh-tw" || languageValue == "zh_tw" || languageValue == "tc")
                    {
                        _language = UiLanguage.ChineseTraditional;
                    }
                    if (themeValue == "dark")
                    {
                        _theme = UiTheme.Dark;
                    }

                    _autoDetectMode = ExtractJsonBool(raw, "auto_detect", _autoDetectMode);
                    _manualDeploymentMode = ParseDeploymentMode(
                        ExtractJsonString(raw, "manual_override"),
                        _manualDeploymentMode);
                    _lastDetectedMode = ParseDeploymentMode(
                        ExtractJsonString(raw, "last_detected"),
                        _lastDetectedMode);

                    _proxyMode = ParseProxyMode(ExtractJsonString(raw, "proxy_mode"), _proxyMode);
                    _customHttpProxy = ExtractJsonString(raw, "custom_http_proxy");
                    _customHttpsProxy = ExtractJsonString(raw, "custom_https_proxy");
                    _customAllProxy = ExtractJsonString(raw, "custom_all_proxy");
                    string noProxy = ExtractJsonString(raw, "custom_no_proxy");
                    if (!string.IsNullOrWhiteSpace(noProxy))
                    {
                        _customNoProxy = noProxy.Trim();
                    }
                    string sudoPasswordProtected = FirstNonEmpty(
                        ExtractJsonString(raw, "wsl_sudo_password_dpapi"),
                        ExtractJsonString(raw, "wsl_sudo_password_encrypted"));
                    if (!string.IsNullOrWhiteSpace(sudoPasswordProtected))
                    {
                        _wslSudoPasswordProtected = sudoPasswordProtected.Trim();
                    }
                    string browserTarget = ExtractJsonString(raw, "browser_target");
                    if (!string.IsNullOrWhiteSpace(browserTarget))
                    {
                        _dashboardBrowserTarget = browserTarget.Trim();
                    }
                    string wslChromeBackend = ExtractJsonString(raw, "wsl_chrome_backend");
                    if (!string.IsNullOrWhiteSpace(wslChromeBackend))
                    {
                        _wslChromeBackend = NormalizeWslChromeBackend(wslChromeBackend);
                    }

                    string windowsDir = ExtractJsonString(raw, "windows_project_dir");
                    if (!string.IsNullOrWhiteSpace(windowsDir))
                    {
                        _windowsProjectDir = NormalizeWindowsPath(windowsDir);
                    }
                    string wslDir = ExtractJsonString(raw, "wsl_project_dir");
                    if (!string.IsNullOrWhiteSpace(wslDir))
                    {
                        _wslProjectDir = NormalizeWslPath(wslDir);
                    }

                    string openclawDir = FirstNonEmpty(
                        ExtractJsonString(raw, "wsl_docker_openclaw_dir"),
                        ExtractJsonString(raw, "wsl_openclaw_dir"));
                    string dataDir = FirstNonEmpty(
                        ExtractJsonString(raw, "wsl_docker_data_dir"),
                        ExtractJsonString(raw, "wsl_data_dir"));
                    string startScript = FirstNonEmpty(
                        ExtractJsonString(raw, "wsl_docker_start_script"),
                        ExtractJsonString(raw, "wsl_start_script"));
                    string dashboardScript = FirstNonEmpty(
                        ExtractJsonString(raw, "wsl_docker_dashboard_script"),
                        ExtractJsonString(raw, "wsl_dashboard_script"));
                    string gatewayUrl = ExtractJsonString(raw, "gateway_root_url");
                    string dockerComposeCommand = ExtractJsonString(raw, "docker_compose_command");
                    string gatewayServiceName = ExtractJsonString(raw, "gateway_service_name");
                    string wslNativeProject = ExtractJsonString(raw, "wsl_native_project_dir");
                    string wslNativeOpenclawCommand = ExtractJsonString(raw, "wsl_native_openclaw_command");
                    string wslNativeInstall = ExtractJsonString(raw, "wsl_native_install_command");
                    string winDockerDir = ExtractJsonString(raw, "win_docker_openclaw_dir");
                    string winDockerDataDir = ExtractJsonString(raw, "win_docker_data_dir");
                    string winNativeProject = ExtractJsonString(raw, "win_native_project_dir");
                    string winNativeOpenclawCommand = ExtractJsonString(raw, "win_native_openclaw_command");
                    string winNativeInstall = ExtractJsonString(raw, "win_native_install_command");

                    _wslOpenclawDir = ResolveWslPath(openclawDir, _wslProjectDir + "/openclaw", _wslProjectDir);
                    _wslDataDir = ResolveWslPath(dataDir, _wslProjectDir + "/openclaw-data", _wslProjectDir);
                    _wslStartScriptPath = ResolveWslPath(startScript, _wslProjectDir + "/openclaw-start-fast.sh", _wslProjectDir);
                    _wslOpenDashboardScriptPath = ResolveWslPath(dashboardScript, _wslProjectDir + "/openclaw-open-dashboard-wsl.sh", _wslProjectDir);
                    _wslNativeProjectDir = ResolveWslPath(wslNativeProject, _wslProjectDir + "/openclaw", _wslProjectDir);
                    _wslNativeOpenclawCommand = NormalizeCommandText(
                        wslNativeOpenclawCommand,
                        DefaultWslNativeOpenclawCommand);
                    if (!string.IsNullOrWhiteSpace(wslNativeInstall))
                    {
                        _wslNativeInstallCommand = wslNativeInstall.Trim();
                    }
                    _winDockerOpenclawDir = NormalizeWindowsPath(string.IsNullOrWhiteSpace(winDockerDir) ? _windowsProjectDir + "\\openclaw" : winDockerDir);
                    _winDockerDataDir = NormalizeWindowsPath(string.IsNullOrWhiteSpace(winDockerDataDir) ? _windowsProjectDir + "\\openclaw-data" : winDockerDataDir);
                    _winNativeProjectDir = NormalizeWindowsPath(string.IsNullOrWhiteSpace(winNativeProject) ? _windowsProjectDir : winNativeProject);
                    _winNativeOpenclawCommand = NormalizeCommandText(
                        winNativeOpenclawCommand,
                        DefaultWinNativeOpenclawCommand);
                    if (!string.IsNullOrWhiteSpace(winNativeInstall))
                    {
                        _winNativeInstallCommand = winNativeInstall.Trim();
                    }
                    _gatewayRootUrl = NormalizeGatewayRootUrl(string.IsNullOrWhiteSpace(gatewayUrl) ? DefaultGatewayRootUrl : gatewayUrl);
                    _dockerComposeCommand = NormalizeDockerComposeCommand(string.IsNullOrWhiteSpace(dockerComposeCommand) ? DefaultDockerComposeCommand : dockerComposeCommand);
                    _gatewayServiceName = NormalizeGatewayServiceName(string.IsNullOrWhiteSpace(gatewayServiceName) ? DefaultGatewayServiceName : gatewayServiceName);

                    if (_settingsSchemaVersion < SettingsSchemaVersion)
                    {
                        TryBackupLegacySettings(raw);
                        _settingsSchemaVersion = SettingsSchemaVersion;
                        SavePreferences();
                    }
                }
                catch
                {
                }

                ResolveEffectiveMode(true, false);
            }

            private void SavePreferences()
            {
                try
                {
                    string dir = Path.GetDirectoryName(SettingsFileWindows) ?? AppBaseDirWindows;
                    Directory.CreateDirectory(dir);
                    string languageValue = "en";
                    if (_language == UiLanguage.ChineseSimplified)
                    {
                        languageValue = "zh-hans";
                    }
                    else if (_language == UiLanguage.ChineseTraditional)
                    {
                        languageValue = "zh-hant";
                    }
                    string themeValue = _theme == UiTheme.Dark ? "dark" : "light";
                    string windowsProjectDir = JsonEscape(NormalizeWindowsPath(_windowsProjectDir));
                    string wslProjectDir = JsonEscape(NormalizeWslPath(_wslProjectDir));
                    string wslOpenclawDir = JsonEscape(NormalizeWslPath(_wslOpenclawDir));
                    string wslDataDir = JsonEscape(NormalizeWslPath(_wslDataDir));
                    string wslStartScript = JsonEscape(NormalizeWslPath(_wslStartScriptPath));
                    string wslDashboardScript = JsonEscape(NormalizeWslPath(_wslOpenDashboardScriptPath));
                    string wslNativeProjectDir = JsonEscape(NormalizeWslPath(_wslNativeProjectDir));
                    string wslNativeCommand = JsonEscape(NormalizeCommandText(_wslNativeOpenclawCommand, DefaultWslNativeOpenclawCommand));
                    string wslNativeInstall = JsonEscape(_wslNativeInstallCommand ?? string.Empty);
                    string winDockerOpenclawDir = JsonEscape(NormalizeWindowsPath(_winDockerOpenclawDir));
                    string winDockerDataDir = JsonEscape(NormalizeWindowsPath(_winDockerDataDir));
                    string winNativeProjectDir = JsonEscape(NormalizeWindowsPath(_winNativeProjectDir));
                    string winNativeCommand = JsonEscape(NormalizeCommandText(_winNativeOpenclawCommand, DefaultWinNativeOpenclawCommand));
                    string winNativeInstall = JsonEscape(_winNativeInstallCommand ?? string.Empty);
                    string gatewayRootUrl = JsonEscape(NormalizeGatewayRootUrl(_gatewayRootUrl));
                    string dockerComposeCommand = JsonEscape(NormalizeDockerComposeCommand(_dockerComposeCommand));
                    string gatewayServiceName = JsonEscape(NormalizeGatewayServiceName(_gatewayServiceName));
                    string modeManual = JsonEscape(ModeToConfigValue(_manualDeploymentMode));
                    string modeLast = JsonEscape(ModeToConfigValue(_lastDetectedMode));
                    string proxyMode = JsonEscape(ProxyModeToConfigValue(_proxyMode));
                    string browserTarget = JsonEscape(string.IsNullOrWhiteSpace(_dashboardBrowserTarget) ? DefaultDashboardBrowserTarget : _dashboardBrowserTarget.Trim());
                    string wslChromeBackend = JsonEscape(NormalizeWslChromeBackend(_wslChromeBackend));
                    string customHttpProxy = JsonEscape(_customHttpProxy ?? string.Empty);
                    string customHttpsProxy = JsonEscape(_customHttpsProxy ?? string.Empty);
                    string customAllProxy = JsonEscape(_customAllProxy ?? string.Empty);
                    string customNoProxy = JsonEscape(string.IsNullOrWhiteSpace(_customNoProxy) ? "localhost,127.0.0.1,::1" : _customNoProxy.Trim());
                    string wslSudoPasswordProtected = JsonEscape(_wslSudoPasswordProtected ?? string.Empty);
                    File.WriteAllText(
                        SettingsFileWindows,
                        "{\n" +
                        "  \"schema_version\": " + SettingsSchemaVersion + ",\n" +
                        "  \"language\": \"" + languageValue + "\",\n" +
                        "  \"theme\": \"" + themeValue + "\",\n" +
                        "  \"mode\": {\n" +
                        "    \"auto_detect\": " + (_autoDetectMode ? "true" : "false") + ",\n" +
                        "    \"manual_override\": \"" + modeManual + "\",\n" +
                        "    \"last_detected\": \"" + modeLast + "\"\n" +
                        "  },\n" +
                        "  \"profiles\": {\n" +
                        "    \"wsl_docker\": {\n" +
                        "      \"windows_project_dir\": \"" + windowsProjectDir + "\",\n" +
                        "      \"wsl_project_dir\": \"" + wslProjectDir + "\",\n" +
                        "      \"wsl_docker_openclaw_dir\": \"" + wslOpenclawDir + "\",\n" +
                        "      \"wsl_docker_data_dir\": \"" + wslDataDir + "\",\n" +
                        "      \"wsl_docker_start_script\": \"" + wslStartScript + "\",\n" +
                        "      \"wsl_docker_dashboard_script\": \"" + wslDashboardScript + "\"\n" +
                        "    },\n" +
                        "    \"wsl_native\": {\n" +
                        "      \"wsl_native_project_dir\": \"" + wslNativeProjectDir + "\",\n" +
                        "      \"wsl_native_openclaw_command\": \"" + wslNativeCommand + "\",\n" +
                        "      \"wsl_native_install_command\": \"" + wslNativeInstall + "\"\n" +
                        "    },\n" +
                        "    \"win_docker\": {\n" +
                        "      \"win_docker_openclaw_dir\": \"" + winDockerOpenclawDir + "\",\n" +
                        "      \"win_docker_data_dir\": \"" + winDockerDataDir + "\"\n" +
                        "    },\n" +
                        "    \"win_native\": {\n" +
                        "      \"win_native_project_dir\": \"" + winNativeProjectDir + "\",\n" +
                        "      \"win_native_openclaw_command\": \"" + winNativeCommand + "\",\n" +
                        "      \"win_native_install_command\": \"" + winNativeInstall + "\"\n" +
                        "    }\n" +
                        "  },\n" +
                        "  \"docker\": {\n" +
                        "    \"docker_compose_command\": \"" + dockerComposeCommand + "\",\n" +
                        "    \"gateway_service_name\": \"" + gatewayServiceName + "\"\n" +
                        "  },\n" +
                        "  \"network\": {\n" +
                        "    \"proxy_mode\": \"" + proxyMode + "\",\n" +
                        "    \"custom_http_proxy\": \"" + customHttpProxy + "\",\n" +
                        "    \"custom_https_proxy\": \"" + customHttpsProxy + "\",\n" +
                        "    \"custom_all_proxy\": \"" + customAllProxy + "\",\n" +
                        "    \"custom_no_proxy\": \"" + customNoProxy + "\",\n" +
                        "    \"wsl_sudo_password_dpapi\": \"" + wslSudoPasswordProtected + "\"\n" +
                        "  },\n" +
                        "  \"dashboard\": {\n" +
                        "    \"gateway_root_url\": \"" + gatewayRootUrl + "\",\n" +
                        "    \"browser_target\": \"" + browserTarget + "\",\n" +
                        "    \"wsl_chrome_backend\": \"" + wslChromeBackend + "\"\n" +
                        "  }\n" +
                        "}\n",
                        Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    AppendLog(Tr("Failed to save settings: ", "保存设置失败：") + ex.Message);
                }
            }

            private static void TryBackupLegacySettings(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }
                try
                {
                    string dir = Path.GetDirectoryName(SettingsFileWindows) ?? AppBaseDirWindows;
                    Directory.CreateDirectory(dir);
                    string backup = Path.Combine(dir, "openclaw-control-panel-settings.v1.bak.json");
                    if (!File.Exists(backup))
                    {
                        File.WriteAllText(backup, raw, Encoding.UTF8);
                    }
                }
                catch
                {
                }
            }

            private static string JsonEscape(string value)
            {
                return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            }

            private static string ExtractJsonString(string raw, string key)
            {
                if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(key))
                {
                    return string.Empty;
                }
                string token = "\"" + key + "\"";
                int keyIndex = raw.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                {
                    return string.Empty;
                }
                int colonIndex = raw.IndexOf(':', keyIndex + token.Length);
                if (colonIndex < 0)
                {
                    return string.Empty;
                }
                int quoteStart = raw.IndexOf('"', colonIndex + 1);
                if (quoteStart < 0)
                {
                    return string.Empty;
                }
                var sb = new StringBuilder();
                bool escape = false;
                for (int i = quoteStart + 1; i < raw.Length; i++)
                {
                    char c = raw[i];
                    if (escape)
                    {
                        switch (c)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            default: sb.Append(c); break;
                        }
                        escape = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    sb.Append(c);
                }
                return sb.ToString();
            }

            private static bool ExtractJsonBool(string raw, string key, bool fallback)
            {
                if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(key))
                {
                    return fallback;
                }
                string token = "\"" + key + "\"";
                int keyIndex = raw.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                {
                    return fallback;
                }
                int colonIndex = raw.IndexOf(':', keyIndex + token.Length);
                if (colonIndex < 0)
                {
                    return fallback;
                }
                string tail = raw.Substring(colonIndex + 1).TrimStart();
                if (tail.StartsWith("true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (tail.StartsWith("false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return fallback;
            }

            private static int ExtractJsonInt(string raw, string key, int fallback)
            {
                if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(key))
                {
                    return fallback;
                }
                string token = "\"" + key + "\"";
                int keyIndex = raw.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                {
                    return fallback;
                }
                int colonIndex = raw.IndexOf(':', keyIndex + token.Length);
                if (colonIndex < 0)
                {
                    return fallback;
                }
                int i = colonIndex + 1;
                while (i < raw.Length && char.IsWhiteSpace(raw[i]))
                {
                    i++;
                }
                int start = i;
                if (i < raw.Length && (raw[i] == '-' || raw[i] == '+'))
                {
                    i++;
                }
                while (i < raw.Length && char.IsDigit(raw[i]))
                {
                    i++;
                }
                if (i <= start)
                {
                    return fallback;
                }
                int parsed;
                if (int.TryParse(raw.Substring(start, i - start), out parsed))
                {
                    return parsed;
                }
                return fallback;
            }

            private static string FirstNonEmpty(params string[] values)
            {
                if (values == null)
                {
                    return string.Empty;
                }
                for (int i = 0; i < values.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(values[i]))
                    {
                        return values[i];
                    }
                }
                return string.Empty;
            }

            private static string NormalizeWindowsPath(string input)
            {
                string value = (input ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    return DefaultBaseDirWindows;
                }
                value = value.Replace('/', '\\');
                while (value.Length > 3 && value.EndsWith("\\", StringComparison.Ordinal))
                {
                    value = value.Substring(0, value.Length - 1);
                }
                return value;
            }

            private static string NormalizeWslPath(string input)
            {
                string value = (input ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    return DefaultBaseDirWsl;
                }
                value = value.Replace('\\', '/');
                while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
                {
                    value = value.Substring(0, value.Length - 1);
                }
                return value;
            }

            private static string ResolveWslPath(string input, string fallbackAbsolute, string baseDir)
            {
                string value = (input ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    return NormalizeWslPath(fallbackAbsolute);
                }
                value = value.Replace('\\', '/');
                if (value.StartsWith("/", StringComparison.Ordinal))
                {
                    return NormalizeWslPath(value);
                }
                return NormalizeWslPath(NormalizeWslPath(baseDir) + "/" + value.TrimStart('/'));
            }

            private static string NormalizeGatewayRootUrl(string url)
            {
                string value = (url ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    value = DefaultGatewayRootUrl;
                }
                if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    value = "http://" + value;
                }
                if (!value.EndsWith("/", StringComparison.Ordinal))
                {
                    value += "/";
                }
                return value;
            }

            private static string NormalizeDockerComposeCommand(string input)
            {
                string value = (input ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    return DefaultDockerComposeCommand;
                }
                if (value.IndexOfAny(new[] { '\r', '\n', ';', '|', '&', '<', '>', '"', '\'', '`', '$' }) >= 0)
                {
                    return DefaultDockerComposeCommand;
                }
                return value;
            }

            private static string NormalizeGatewayServiceName(string input)
            {
                string value = (input ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    return DefaultGatewayServiceName;
                }
                return value;
            }

            private static string NormalizeCommandText(string input, string fallback)
            {
                string value = (input ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    return fallback;
                }
                if (value.IndexOfAny(new[] { '\r', '\n', ';', '|', '&', '<', '>', '`' }) >= 0)
                {
                    return fallback;
                }
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    bool safe =
                        (c >= 'a' && c <= 'z') ||
                        (c >= 'A' && c <= 'Z') ||
                        (c >= '0' && c <= '9') ||
                        c == ' ' || c == '.' || c == ':' || c == '/' || c == '\\' || c == '_' || c == '-';
                    if (!safe)
                    {
                        return fallback;
                    }
                }
                return value;
            }

            private static string NormalizeWslChromeBackend(string input)
            {
                string value = (input ?? string.Empty).Trim().ToLowerInvariant();
                if (value == "x11" || value == "wayland" || value == "auto")
                {
                    return value;
                }
                return DefaultWslChromeBackend;
            }

            private static void TrySetCueBanner(TextBox box, string cueText)
            {
                if (box == null)
                {
                    return;
                }
                try
                {
                    SendMessage(box.Handle, EmSetCueBanner, 0, cueText ?? string.Empty);
                }
                catch
                {
                }
            }

            private static bool ContainsLineBreak(string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return false;
                }
                return text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0;
            }

            private static string ProtectSecretForCurrentUser(string plainText)
            {
                if (string.IsNullOrEmpty(plainText))
                {
                    return string.Empty;
                }
                try
                {
                    byte[] raw = Encoding.UTF8.GetBytes(plainText);
                    byte[] protectedBytes = ProtectedData.Protect(raw, WslSudoPasswordEntropy, DataProtectionScope.CurrentUser);
                    return Convert.ToBase64String(protectedBytes);
                }
                catch
                {
                    return string.Empty;
                }
            }

            private static string UnprotectSecretForCurrentUser(string protectedValue)
            {
                if (string.IsNullOrWhiteSpace(protectedValue))
                {
                    return string.Empty;
                }
                try
                {
                    byte[] protectedBytes = Convert.FromBase64String(protectedValue.Trim());
                    byte[] raw = ProtectedData.Unprotect(protectedBytes, WslSudoPasswordEntropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(raw);
                }
                catch
                {
                    return string.Empty;
                }
            }

            private string GetStoredWslSudoPasswordPlain()
            {
                return UnprotectSecretForCurrentUser(_wslSudoPasswordProtected);
            }

            private static DeploymentMode ParseDeploymentMode(string raw, DeploymentMode fallback)
            {
                string value = (raw ?? string.Empty).Trim().ToLowerInvariant();
                if (value == "auto") return DeploymentMode.Auto;
                if (value == "wsl_docker" || value == "wsldocker") return DeploymentMode.WslDocker;
                if (value == "wsl_native" || value == "wslnative") return DeploymentMode.WslNative;
                if (value == "win_docker" || value == "windows_docker" || value == "windocker") return DeploymentMode.WinDocker;
                if (value == "win_native" || value == "windows_native" || value == "winnative") return DeploymentMode.WinNative;
                return fallback;
            }

            private static string ModeToConfigValue(DeploymentMode mode)
            {
                switch (mode)
                {
                    case DeploymentMode.WslDocker: return "wsl_docker";
                    case DeploymentMode.WslNative: return "wsl_native";
                    case DeploymentMode.WinDocker: return "win_docker";
                    case DeploymentMode.WinNative: return "win_native";
                    case DeploymentMode.Auto:
                    default:
                        return "auto";
                }
            }

            private static ProxyMode ParseProxyMode(string raw, ProxyMode fallback)
            {
                string value = (raw ?? string.Empty).Trim().ToLowerInvariant();
                if (value == "system") return ProxyMode.System;
                if (value == "off") return ProxyMode.Off;
                if (value == "wsl_clash_auto" || value == "wslclashauto") return ProxyMode.WslClashAuto;
                if (value == "custom") return ProxyMode.Custom;
                return fallback;
            }

            private static string ProxyModeToConfigValue(ProxyMode mode)
            {
                switch (mode)
                {
                    case ProxyMode.Off: return "off";
                    case ProxyMode.WslClashAuto: return "wsl_clash_auto";
                    case ProxyMode.Custom: return "custom";
                    case ProxyMode.System:
                    default:
                        return "system";
                }
            }

            private static string EscapePowerShellSingleQuoted(string value)
            {
                return (value ?? string.Empty).Replace("'", "''");
            }

            private static string BashQuote(string value)
            {
                return "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";
            }

            private string BuildDashboardChatUrl()
            {
                return NormalizeGatewayRootUrl(_gatewayRootUrl) + "chat?session=main";
            }

            private static string ModeLabelEnglish(DeploymentMode mode)
            {
                switch (mode)
                {
                    case DeploymentMode.WslDocker: return "WSL + Docker";
                    case DeploymentMode.WslNative: return "WSL Native";
                    case DeploymentMode.WinDocker: return "Windows + Docker";
                    case DeploymentMode.WinNative: return "Windows Native";
                    case DeploymentMode.Auto:
                    default:
                        return "Auto";
                }
            }

            private static string ModeLabelChinese(DeploymentMode mode)
            {
                switch (mode)
                {
                    case DeploymentMode.WslDocker: return "WSL + Docker";
                    case DeploymentMode.WslNative: return "WSL 原生";
                    case DeploymentMode.WinDocker: return "Windows + Docker";
                    case DeploymentMode.WinNative: return "Windows 原生";
                    case DeploymentMode.Auto:
                    default:
                        return "自动";
                }
            }

            private static DeploymentMode ForceConcreteMode(DeploymentMode mode)
            {
                if (mode == DeploymentMode.Auto)
                {
                    return DeploymentMode.WslDocker;
                }
                return mode;
            }

            private string BuildWslProxyPrelude()
            {
                switch (_proxyMode)
                {
                    case ProxyMode.Off:
                        return string.Join(
                            "\n",
                            "unset HTTP_PROXY HTTPS_PROXY ALL_PROXY NO_PROXY",
                            "unset http_proxy https_proxy all_proxy no_proxy");
                    case ProxyMode.Custom:
                        return string.Join(
                            "\n",
                            "export HTTP_PROXY=" + BashQuote(_customHttpProxy ?? string.Empty),
                            "export HTTPS_PROXY=" + BashQuote(_customHttpsProxy ?? string.Empty),
                            "export ALL_PROXY=" + BashQuote(_customAllProxy ?? string.Empty),
                            "export NO_PROXY=" + BashQuote(string.IsNullOrWhiteSpace(_customNoProxy) ? "localhost,127.0.0.1,::1" : _customNoProxy.Trim()),
                            "export http_proxy=\"$HTTP_PROXY\"",
                            "export https_proxy=\"$HTTPS_PROXY\"",
                            "export all_proxy=\"$ALL_PROXY\"",
                            "export no_proxy=\"$NO_PROXY\"");
                    case ProxyMode.WslClashAuto:
                        return string.Join(
                            "\n",
                            "WSL_HOST_IP=$(ip route 2>/dev/null | awk '/default/ {print $3; exit}')",
                            "if [ -n \"$WSL_HOST_IP\" ]; then",
                            "  export HTTPS_PROXY=\"http://$WSL_HOST_IP:4780\"",
                            "  export HTTP_PROXY=\"$HTTPS_PROXY\"",
                            "  export ALL_PROXY=\"socks5h://$WSL_HOST_IP:4781\"",
                            "  export NO_PROXY=\"localhost,127.0.0.1,::1\"",
                            "  export http_proxy=\"$HTTP_PROXY\"",
                            "  export https_proxy=\"$HTTPS_PROXY\"",
                            "  export all_proxy=\"$ALL_PROXY\"",
                            "  export no_proxy=\"$NO_PROXY\"",
                            "fi");
                    case ProxyMode.System:
                    default:
                        return "true";
                }
            }

            private string BuildWslDockerHostPrelude()
            {
                return string.Join(
                    "\n",
                    "export DOCKER_HOST=" + BashQuote(DefaultWslDockerHost),
                    "unset DOCKER_CONTEXT");
            }

            private string BuildPowerShellProxyPrelude()
            {
                switch (_proxyMode)
                {
                    case ProxyMode.Off:
                        return string.Join(
                            "\n",
                            "$env:HTTP_PROXY = ''",
                            "$env:HTTPS_PROXY = ''",
                            "$env:ALL_PROXY = ''",
                            "$env:NO_PROXY = ''");
                    case ProxyMode.Custom:
                        return string.Join(
                            "\n",
                            "$env:HTTP_PROXY = '" + EscapePowerShellSingleQuoted(_customHttpProxy ?? string.Empty) + "'",
                            "$env:HTTPS_PROXY = '" + EscapePowerShellSingleQuoted(_customHttpsProxy ?? string.Empty) + "'",
                            "$env:ALL_PROXY = '" + EscapePowerShellSingleQuoted(_customAllProxy ?? string.Empty) + "'",
                            "$env:NO_PROXY = '" + EscapePowerShellSingleQuoted(string.IsNullOrWhiteSpace(_customNoProxy) ? "localhost,127.0.0.1,::1" : _customNoProxy.Trim()) + "'");
                    case ProxyMode.System:
                    case ProxyMode.WslClashAuto:
                    default:
                        return "$null = $null";
                }
            }

            private string GetWslNativeDashboardCliCommand()
            {
                string command = NormalizeCommandText(_wslNativeOpenclawCommand, DefaultWslNativeOpenclawCommand);
                string lowered = command.Replace('\\', '/').Trim().ToLowerInvariant();
                if (lowered.EndsWith("/openclaw-wsl-admin-bridge.sh", StringComparison.Ordinal) ||
                    lowered.EndsWith("/openclaw-wsl-native-helper.sh", StringComparison.Ordinal) ||
                    lowered.EndsWith("/openclaw-wsl-native-dashboard.sh", StringComparison.Ordinal))
                {
                    return "/usr/local/bin/openclaw";
                }
                return command;
            }

            private int GetGatewayPort()
            {
                try
                {
                    var uri = new Uri(NormalizeGatewayRootUrl(_gatewayRootUrl), UriKind.Absolute);
                    return uri.IsDefaultPort ? (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80) : uri.Port;
                }
                catch
                {
                    return 18790;
                }
            }

            private DeploymentMode DetectBestMode()
            {
                if (ProbeModeWslDocker())
                {
                    return DeploymentMode.WslDocker;
                }
                if (ProbeModeWslNative())
                {
                    return DeploymentMode.WslNative;
                }
                if (ProbeModeWinDocker())
                {
                    return DeploymentMode.WinDocker;
                }
                if (ProbeModeWinNative())
                {
                    return DeploymentMode.WinNative;
                }
                return DeploymentMode.WslDocker;
            }

            private void ResolveEffectiveMode(bool forceDetect, bool announce)
            {
                DeploymentMode previous = _effectiveMode;
                if (_autoDetectMode)
                {
                    if (forceDetect || _lastDetectedMode == DeploymentMode.Auto)
                    {
                        _lastDetectedMode = DetectBestMode();
                    }
                    _effectiveMode = ForceConcreteMode(_lastDetectedMode);
                }
                else
                {
                    _effectiveMode = ForceConcreteMode(_manualDeploymentMode);
                }

                if (announce && previous != _effectiveMode)
                {
                    AppendLog(
                        Tr("Mode switched to ", "模式已切换为 ") +
                        Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)) + ".");
                }
            }

            private bool ProbeModeWslDocker()
            {
                string openclawDir = NormalizeWslPath(_wslOpenclawDir);
                string cmd = string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    BuildWslDockerHostPrelude(),
                    "if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1 && [ -f " + BashQuote(openclawDir + "/docker-compose.yml") + " ]; then",
                    "  echo mode_ok=1",
                    "  exit 0",
                    "fi",
                    "echo mode_ok=0",
                    "exit 1");
                var result = RunCommandCapture(new CommandSpec { Runtime = CommandRuntime.WslBash, Command = cmd, TimeoutSeconds = 12, ActionName = "probe-wsl-docker", Mode = DeploymentMode.WslDocker });
                return result.ExitCode == 0;
            }

            private bool ProbeModeWslNative()
            {
                string cmd = string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    "if command -v " + BashQuote(NormalizeCommandText(_wslNativeOpenclawCommand, DefaultWslNativeOpenclawCommand)) + " >/dev/null 2>&1; then",
                    "  echo mode_ok=1",
                    "  exit 0",
                    "fi",
                    "echo mode_ok=0",
                    "exit 1");
                var result = RunCommandCapture(new CommandSpec { Runtime = CommandRuntime.WslBash, Command = cmd, TimeoutSeconds = 10, ActionName = "probe-wsl-native", Mode = DeploymentMode.WslNative });
                return result.ExitCode == 0;
            }

            private bool ProbeModeWinDocker()
            {
                string cmd = string.Join(
                    "\n",
                    "$ErrorActionPreference = 'SilentlyContinue'",
                    BuildPowerShellProxyPrelude(),
                    "docker info *> $null",
                    "if ($LASTEXITCODE -eq 0) { exit 0 }",
                    "exit 1");
                var result = RunCommandCapture(new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = cmd, TimeoutSeconds = 10, ActionName = "probe-win-docker", Mode = DeploymentMode.WinDocker });
                return result.ExitCode == 0;
            }

            private bool ProbeModeWinNative()
            {
                string openclaw = EscapePowerShellSingleQuoted(NormalizeCommandText(_winNativeOpenclawCommand, DefaultWinNativeOpenclawCommand));
                string cmd = string.Join(
                    "\n",
                    "$ErrorActionPreference = 'SilentlyContinue'",
                    "$found = Get-Command '" + openclaw + "' -ErrorAction SilentlyContinue",
                    "if ($found) { exit 0 }",
                    "exit 1");
                var result = RunCommandCapture(new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = cmd, TimeoutSeconds = 10, ActionName = "probe-win-native", Mode = DeploymentMode.WinNative });
                return result.ExitCode == 0;
            }

            private string BuildWslDockerStartCommand()
            {
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    BuildWslDockerHostPrelude(),
                    "cd " + BashQuote(_wslProjectDir),
                    BashQuote(_wslStartScriptPath));
            }

            private string BuildWslDockerEnsureDockerOnlyCommand()
            {
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    BuildWslDockerHostPrelude(),
                    "cd " + BashQuote(_wslProjectDir),
                    BashQuote(_wslStartScriptPath) + " --ensure-docker-only");
            }

            private string BuildWslDockerStopCommand()
            {
                string compose = NormalizeDockerComposeCommand(_dockerComposeCommand);
                string service = NormalizeGatewayServiceName(_gatewayServiceName);
                string ensureDocker = BuildWslDockerEnsureDockerOnlyCommand();
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    BuildWslDockerHostPrelude(),
                    "if " + ensureDocker + " >/tmp/openclaw-ensure-docker.log 2>&1; then",
                    "  :",
                    "else",
                    "  echo \"[panel] Docker daemon unreachable; nothing to stop.\"",
                    "  tail -n 20 /tmp/openclaw-ensure-docker.log 2>/dev/null || true",
                    "  exit 0",
                    "fi",
                    "cd " + BashQuote(_wslOpenclawDir),
                    compose + " stop " + BashQuote(service));
            }

            private string BuildWslDockerOpenDashboardCommand()
            {
                string chromeBackend = NormalizeWslChromeBackend(_wslChromeBackend);
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    "export OPENCLAW_CHROME_BACKEND=" + BashQuote(chromeBackend),
                    "cd " + BashQuote(_wslProjectDir),
                    BashQuote(_wslOpenDashboardScriptPath));
            }

            private string BuildWslDockerStatusCommand()
            {
                string tokenPath = NormalizeWslPath(_wslDataDir + "/.gateway-token");
                string openclawDir = NormalizeWslPath(_wslOpenclawDir);
                string rootUrl = NormalizeGatewayRootUrl(_gatewayRootUrl);
                string healthUrl = rootUrl.TrimEnd('/') + "/health";
                string compose = NormalizeDockerComposeCommand(_dockerComposeCommand);
                string service = NormalizeGatewayServiceName(_gatewayServiceName);
                string ensureDocker = BuildWslDockerEnsureDockerOnlyCommand();
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    BuildWslDockerHostPrelude(),
                    "echo \"mode=wsl_docker\"",
                    "echo \"action=status\"",
                    "echo \"time=$(date '+%F %T')\"",
                    "docker_bootstrap=\"skipped\"",
                    "if " + ensureDocker + " >/tmp/openclaw-ensure-docker.log 2>&1; then",
                    "  docker_bootstrap=\"ok\"",
                    "else",
                    "  docker_bootstrap=\"failed\"",
                    "fi",
                    "echo \"docker_bootstrap=${docker_bootstrap}\"",
                    "if [ \"$docker_bootstrap\" = \"failed\" ]; then",
                    "  echo \"docker_bootstrap_log_tail_begin=1\"",
                    "  tail -n 12 /tmp/openclaw-ensure-docker.log 2>/dev/null || true",
                    "  echo \"docker_bootstrap_log_tail_end=1\"",
                    "fi",
                    "if docker info >/dev/null 2>&1; then",
                    "  echo \"docker=up\"",
                    "else",
                    "  echo \"docker=down\"",
                    "fi",
                    "if [ -f " + BashQuote(tokenPath) + " ] && [ -s " + BashQuote(tokenPath) + " ]; then",
                    "  echo \"token=ok\"",
                    "else",
                    "  echo \"token=missing\"",
                    "fi",
                    "gateway_state=\"unknown\"",
                    "if docker info >/dev/null 2>&1; then",
                    "  if cd " + BashQuote(openclawDir) + " 2>/dev/null; then",
                    "    if " + compose + " ps --status running --services 2>/dev/null | grep -Fxq " + BashQuote(service) + "; then",
                    "      gateway_state=\"running\"",
                    "    elif " + compose + " ps --services 2>/dev/null | grep -Fxq " + BashQuote(service) + "; then",
                    "      gateway_state=\"stopped\"",
                    "    else",
                    "      gateway_state=\"missing\"",
                    "    fi",
                    "  else",
                    "    gateway_state=\"missing\"",
                    "  fi",
                    "fi",
                    "echo \"gateway_container=${gateway_state}\"",
                    "echo \"gateway=${gateway_state}\"",
                    "if command -v curl >/dev/null 2>&1; then",
                    "  root_code=\"$(curl -m 4 -s -o /dev/null -w '%{http_code}' " + BashQuote(rootUrl) + " 2>/dev/null)\"",
                    "  health_code=\"$(curl -m 4 -s -o /dev/null -w '%{http_code}' " + BashQuote(healthUrl) + " 2>/dev/null)\"",
                    "  [ -n \"$root_code\" ] || root_code=\"000\"",
                    "  [ -n \"$health_code\" ] || health_code=\"000\"",
                    "  echo \"http_root=${root_code}\"",
                    "  echo \"http_health=${health_code}\"",
                    "fi",
                    "echo \"ok=1\"");
            }

            private string BuildWslNativeStartCommand()
            {
                string openclaw = NormalizeCommandText(_wslNativeOpenclawCommand, DefaultWslNativeOpenclawCommand);
                int port = GetGatewayPort();
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    "cd " + BashQuote(_wslNativeProjectDir),
                    "OPENCLAW_BIN=" + BashQuote(openclaw),
                    "\"$OPENCLAW_BIN\" gateway start >/tmp/openclaw-native-start.log 2>&1",
                    "start_ec=$?",
                    "cat /tmp/openclaw-native-start.log 2>/dev/null || true",
                    "if [ $start_ec -eq 0 ]; then",
                    "  echo \"ok=1\"",
                    "  exit 0",
                    "fi",
                    "nohup \"$OPENCLAW_BIN\" gateway run --bind loopback --port " + port + " --allow-unconfigured >/tmp/openclaw-native-gateway.log 2>&1 &",
                    "sleep 1",
                    "if pgrep -f \"openclaw gateway\" >/dev/null 2>&1; then",
                    "  echo \"fallback=run-background\"",
                    "  echo \"ok=1\"",
                    "  exit 0",
                    "fi",
                    "echo \"ok=0\"",
                    "exit 1");
            }

            private string BuildWslNativeStopCommand()
            {
                string openclaw = NormalizeCommandText(_wslNativeOpenclawCommand, DefaultWslNativeOpenclawCommand);
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    "OPENCLAW_BIN=" + BashQuote(openclaw),
                    "\"$OPENCLAW_BIN\" gateway stop >/tmp/openclaw-native-stop.log 2>&1",
                    "stop_ec=$?",
                    "cat /tmp/openclaw-native-stop.log 2>/dev/null || true",
                    "if [ $stop_ec -ne 0 ]; then",
                    "  pkill -f \"openclaw gateway\" >/dev/null 2>&1 || true",
                    "fi",
                    "echo \"ok=1\"",
                    "exit 0");
            }

            private string BuildWslNativeOpenDashboardCommand()
            {
                string openclaw = GetWslNativeDashboardCliCommand();
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    "OPENCLAW_BIN=" + BashQuote(openclaw),
                    "dashboard_output=\"$($OPENCLAW_BIN dashboard --no-open 2>&1)\"",
                    "dash_ec=$?",
                    "if [ -n \"$dashboard_output\" ]; then",
                    "  printf '%s\\n' \"$dashboard_output\"",
                    "fi",
                    "exit $dash_ec");
            }

            private string BuildWslNativeStatusCommand()
            {
                string openclaw = GetWslNativeDashboardCliCommand();
                string rootUrl = NormalizeGatewayRootUrl(_gatewayRootUrl);
                string healthUrl = rootUrl.TrimEnd('/') + "/health";
                return string.Join(
                    "\n",
                    "set +e",
                    BuildWslProxyPrelude(),
                    "echo \"mode=wsl_native\"",
                    "echo \"action=status\"",
                    "echo \"time=$(date '+%F %T')\"",
                    "echo \"docker=n/a\"",
                    "OPENCLAW_BIN=" + BashQuote(openclaw),
                    "if command -v \"$OPENCLAW_BIN\" >/dev/null 2>&1 || [ -x \"$OPENCLAW_BIN\" ]; then",
                    "  dashboard_output=\"$($OPENCLAW_BIN dashboard --no-open 2>/dev/null)\"",
                    "else",
                    "  dashboard_output=\"\"",
                    "fi",
                    "if printf '%s\\n' \"$dashboard_output\" | grep -Eiq '(^Dashboard URL:\\s*https?://.*[#?]token=|^dashboard_url=https?://.*[#?]token=|^https?://.*[#?]token=)'; then echo \"token=ok\"; else echo \"token=missing\"; fi",
                    "if pgrep -f \"openclaw gateway\" >/dev/null 2>&1; then",
                    "  echo \"gateway=running\"",
                    "  echo \"gateway_container=running\"",
                    "else",
                    "  echo \"gateway=stopped\"",
                    "  echo \"gateway_container=stopped\"",
                    "fi",
                    "if command -v curl >/dev/null 2>&1; then",
                    "  root_code=\"$(curl -m 4 -s -o /dev/null -w '%{http_code}' " + BashQuote(rootUrl) + " 2>/dev/null)\"",
                    "  health_code=\"$(curl -m 4 -s -o /dev/null -w '%{http_code}' " + BashQuote(healthUrl) + " 2>/dev/null)\"",
                    "  [ -n \"$root_code\" ] || root_code=\"000\"",
                    "  [ -n \"$health_code\" ] || health_code=\"000\"",
                    "  if [ \"$root_code\" = \"000\" ] && [ \"$health_code\" = \"200\" ]; then root_code=\"200\"; fi",
                    "  echo \"http_root=${root_code}\"",
                    "  echo \"http_health=${health_code}\"",
                    "else",
                    "  echo \"http_root=000\"",
                    "  echo \"http_health=000\"",
                    "fi",
                    "echo \"ok=1\"");
            }

            private string BuildWinDockerStartCommand()
            {
                string projectDir = EscapePowerShellSingleQuoted(NormalizeWindowsPath(_winDockerOpenclawDir));
                string compose = EscapePowerShellSingleQuoted(NormalizeDockerComposeCommand(_dockerComposeCommand));
                string service = EscapePowerShellSingleQuoted(NormalizeGatewayServiceName(_gatewayServiceName));
                return string.Join(
                    "\n",
                    "$ErrorActionPreference = 'Continue'",
                    BuildPowerShellProxyPrelude(),
                    "$project = '" + projectDir + "'",
                    "if (!(Test-Path -LiteralPath $project)) { Write-Output 'ok=0'; Write-Output 'Project dir missing.'; exit 2 }",
                    "Push-Location $project",
                    "& cmd.exe /d /s /c \"" + compose.Replace("\"", "\"\"") + " up -d " + service.Replace("\"", "\"\"") + "\"",
                    "$ec = $LASTEXITCODE",
                    "Pop-Location",
                    "if ($ec -eq 0) { Write-Output 'ok=1'; exit 0 }",
                    "Write-Output 'ok=0'",
                    "exit $ec");
            }

            private string BuildWinDockerStopCommand()
            {
                string projectDir = EscapePowerShellSingleQuoted(NormalizeWindowsPath(_winDockerOpenclawDir));
                string compose = EscapePowerShellSingleQuoted(NormalizeDockerComposeCommand(_dockerComposeCommand));
                string service = EscapePowerShellSingleQuoted(NormalizeGatewayServiceName(_gatewayServiceName));
                return string.Join(
                    "\n",
                    "$ErrorActionPreference = 'Continue'",
                    BuildPowerShellProxyPrelude(),
                    "docker info *> $null",
                    "if ($LASTEXITCODE -ne 0) { Write-Output 'Docker unavailable, skip stop.'; Write-Output 'ok=1'; exit 0 }",
                    "$project = '" + projectDir + "'",
                    "if (!(Test-Path -LiteralPath $project)) { Write-Output 'ok=1'; exit 0 }",
                    "Push-Location $project",
                    "& cmd.exe /d /s /c \"" + compose.Replace("\"", "\"\"") + " stop " + service.Replace("\"", "\"\"") + "\"",
                    "$ec = $LASTEXITCODE",
                    "Pop-Location",
                    "if ($ec -eq 0) { Write-Output 'ok=1'; exit 0 }",
                    "Write-Output 'ok=0'",
                    "exit $ec");
            }

            private string BuildWinDockerOpenDashboardCommand()
            {
                string url = EscapePowerShellSingleQuoted(BuildDashboardChatUrl());
                return string.Join(
                    "\n",
                    "$ErrorActionPreference = 'Continue'",
                    "$url = '" + url + "'",
                    "Start-Process $url | Out-Null",
                    "Write-Output 'ok=1'",
                    "exit 0");
            }

            private string BuildWinDockerStatusCommand()
            {
                string projectDir = EscapePowerShellSingleQuoted(NormalizeWindowsPath(_winDockerOpenclawDir));
                string dataDir = EscapePowerShellSingleQuoted(NormalizeWindowsPath(_winDockerDataDir));
                string compose = EscapePowerShellSingleQuoted(NormalizeDockerComposeCommand(_dockerComposeCommand));
                string service = EscapePowerShellSingleQuoted(NormalizeGatewayServiceName(_gatewayServiceName));
                string rootUrl = EscapePowerShellSingleQuoted(NormalizeGatewayRootUrl(_gatewayRootUrl));
                string healthUrl = EscapePowerShellSingleQuoted(NormalizeGatewayRootUrl(_gatewayRootUrl).TrimEnd('/') + "/health");
                return string.Join(
                    "\n",
                    "$ErrorActionPreference = 'SilentlyContinue'",
                    BuildPowerShellProxyPrelude(),
                    "Write-Output 'mode=win_docker'",
                    "Write-Output 'action=status'",
                    "Write-Output ('time=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))",
                    "docker info *> $null",
                    "if ($LASTEXITCODE -eq 0) { Write-Output 'docker=up' } else { Write-Output 'docker=down' }",
                    "$tokenFile = Join-Path '" + dataDir + "' '.gateway-token'",
                    "if ((Test-Path -LiteralPath $tokenFile) -and ((Get-Item -LiteralPath $tokenFile).Length -gt 0)) { Write-Output 'token=ok' } else { Write-Output 'token=missing' }",
                    "$gateway = 'unknown'",
                    "docker info *> $null",
                    "if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath '" + projectDir + "')) {",
                    "  Push-Location '" + projectDir + "'",
                    "  $running = & cmd.exe /d /s /c \"" + compose.Replace("\"", "\"\"") + " ps --status running --services\" 2>$null",
                    "  $all = & cmd.exe /d /s /c \"" + compose.Replace("\"", "\"\"") + " ps --services\" 2>$null",
                    "  Pop-Location",
                    "  if ($running -contains '" + service + "') { $gateway = 'running' }",
                    "  elseif ($all -contains '" + service + "') { $gateway = 'stopped' }",
                    "  else { $gateway = 'missing' }",
                    "}",
                    "Write-Output ('gateway=' + $gateway)",
                    "Write-Output ('gateway_container=' + $gateway)",
                    "$rootCode = '000'",
                    "$healthCode = '000'",
                    "try { $rootCode = [string](Invoke-WebRequest -UseBasicParsing -Uri '" + rootUrl + "' -TimeoutSec 4).StatusCode } catch [System.Net.WebException] { if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $rootCode = [string][int]$_.Exception.Response.StatusCode } } catch { }",
                    "try { $healthCode = [string](Invoke-WebRequest -UseBasicParsing -Uri '" + healthUrl + "' -TimeoutSec 4).StatusCode } catch [System.Net.WebException] { if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $healthCode = [string][int]$_.Exception.Response.StatusCode } } catch { }",
                    "if ($rootCode -eq '000' -and $healthCode -eq '200') { $rootCode = '200' }",
                    "Write-Output ('http_root=' + $rootCode)",
                    "Write-Output ('http_health=' + $healthCode)",
                    "Write-Output 'ok=1'");
            }

            private string BuildWinNativeStartCommand()
            {
                string projectDir = EscapePowerShellSingleQuoted(NormalizeWindowsPath(_winNativeProjectDir));
                string openclaw = EscapePowerShellSingleQuoted(NormalizeCommandText(_winNativeOpenclawCommand, DefaultWinNativeOpenclawCommand));
                int port = GetGatewayPort();
                return string.Join(
                    "\n",
                    "$ErrorActionPreference = 'Continue'",
                    BuildPowerShellProxyPrelude(),
                    "if (Test-Path -LiteralPath '" + projectDir + "') { Set-Location '" + projectDir + "' }",
                    "$oc = '" + openclaw + "'",
                    "& $oc gateway start",
                    "if ($LASTEXITCODE -eq 0) { Write-Output 'ok=1'; exit 0 }",
                    "Start-Process -WindowStyle Hidden -FilePath $oc -ArgumentList @('gateway','run','--bind','loopback','--port','" + port + "','--allow-unconfigured') | Out-Null",
                    "Start-Sleep -Seconds 1",
                    "Write-Output 'ok=1'",
                    "exit 0");
            }

            private string BuildWinNativeStopCommand()
            {
                string openclaw = EscapePowerShellSingleQuoted(NormalizeCommandText(_winNativeOpenclawCommand, DefaultWinNativeOpenclawCommand));
                return string.Join(
                    "\n",
                    "$ErrorActionPreference = 'SilentlyContinue'",
                    "$oc = '" + openclaw + "'",
                    "& $oc gateway stop",
                    "if ($LASTEXITCODE -ne 0) {",
                    "  Get-Process | Where-Object { $_.ProcessName -match 'openclaw' } | Stop-Process -Force",
                    "}",
                    "Write-Output 'ok=1'",
                    "exit 0");
            }

            private string BuildWinNativeOpenDashboardCommand()
            {
                string openclaw = EscapePowerShellSingleQuoted(NormalizeCommandText(_winNativeOpenclawCommand, DefaultWinNativeOpenclawCommand));
                return string.Join(
                    "\n",
                    "$ErrorActionPreference = 'Continue'",
                    "$oc = '" + openclaw + "'",
                    "$dashboardOutput = (& $oc dashboard --no-open 2>&1 | Out-String)",
                    "$ec = $LASTEXITCODE",
                    "if (-not [string]::IsNullOrWhiteSpace($dashboardOutput)) { Write-Output $dashboardOutput.Trim() }",
                    "exit $ec");
            }

            private string BuildWinNativeStatusCommand()
            {
                string openclaw = EscapePowerShellSingleQuoted(NormalizeCommandText(_winNativeOpenclawCommand, DefaultWinNativeOpenclawCommand));
                string rootUrl = EscapePowerShellSingleQuoted(NormalizeGatewayRootUrl(_gatewayRootUrl));
                string healthUrl = EscapePowerShellSingleQuoted(NormalizeGatewayRootUrl(_gatewayRootUrl).TrimEnd('/') + "/health");
                return string.Join(
                    "\n",
                    "$ErrorActionPreference = 'SilentlyContinue'",
                    "Write-Output 'mode=win_native'",
                    "Write-Output 'action=status'",
                    "Write-Output ('time=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))",
                    "Write-Output 'docker=n/a'",
                    "$oc = '" + openclaw + "'",
                    "$dashboardOutput = (& $oc dashboard --no-open 2>$null | Out-String)",
                    "if ($dashboardOutput -match '(?im)^Dashboard URL:\\s*https?://\\S*[#?]token=' -or $dashboardOutput -match '(?im)^dashboard_url=https?://\\S*[#?]token=' -or $dashboardOutput -match '(?im)^https?://\\S*[#?]token=') { Write-Output 'token=ok' } else { Write-Output 'token=missing' }",
                    "$gatewayText = (& $oc gateway status 2>&1 | Out-String)",
                    "if ($gatewayText -match 'Runtime:\\s+running') { $g='running' } elseif ($gatewayText -match 'Runtime:\\s+stopped') { $g='stopped' } else { $g='unknown' }",
                    "Write-Output ('gateway=' + $g)",
                    "Write-Output ('gateway_container=' + $g)",
                    "$rootCode = '000'",
                    "$healthCode = '000'",
                    "try { $rootCode = [string](Invoke-WebRequest -UseBasicParsing -Uri '" + rootUrl + "' -TimeoutSec 4).StatusCode } catch [System.Net.WebException] { if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $rootCode = [string][int]$_.Exception.Response.StatusCode } } catch { }",
                    "try { $healthCode = [string](Invoke-WebRequest -UseBasicParsing -Uri '" + healthUrl + "' -TimeoutSec 4).StatusCode } catch [System.Net.WebException] { if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $healthCode = [string][int]$_.Exception.Response.StatusCode } } catch { }",
                    "if ($rootCode -eq '000' -and $healthCode -eq '200') { $rootCode = '200' }",
                    "Write-Output ('http_root=' + $rootCode)",
                    "Write-Output ('http_health=' + $healthCode)",
                    "Write-Output 'ok=1'");
            }

            private string BuildInstallCommandForMode(DeploymentMode mode)
            {
                switch (mode)
                {
                    case DeploymentMode.WslDocker:
                        return string.Join(
                            "\n",
                            "set -e",
                            BuildWslProxyPrelude(),
                            "cd " + BashQuote(_wslProjectDir),
                            BashQuote(_wslStartScriptPath));
                    case DeploymentMode.WslNative:
                        return string.Join(
                            "\n",
                            "set -e",
                            BuildWslProxyPrelude(),
                            "cd " + BashQuote(_wslProjectDir),
                            _wslNativeInstallCommand);
                    case DeploymentMode.WinDocker:
                        return BuildWinDockerStartCommand();
                    case DeploymentMode.WinNative:
                    default:
                        return string.Join(
                            "\n",
                            "$ErrorActionPreference = 'Stop'",
                            BuildPowerShellProxyPrelude(),
                            _winNativeInstallCommand,
                            "Write-Output 'ok=1'");
                }
            }

            private CommandSpec BuildStartCommandSpec()
            {
                DeploymentMode mode = _effectiveMode;
                switch (mode)
                {
                    case DeploymentMode.WslDocker:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslDockerStartCommand(), TimeoutSeconds = 75, ActionName = "start", Mode = mode };
                    case DeploymentMode.WslNative:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslNativeStartCommand(), TimeoutSeconds = 60, ActionName = "start", Mode = mode };
                    case DeploymentMode.WinDocker:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildWinDockerStartCommand(), TimeoutSeconds = 60, ActionName = "start", Mode = mode };
                    case DeploymentMode.WinNative:
                    default:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildWinNativeStartCommand(), TimeoutSeconds = 60, ActionName = "start", Mode = mode };
                }
            }

            private CommandSpec BuildStopCommandSpec()
            {
                DeploymentMode mode = _effectiveMode;
                switch (mode)
                {
                    case DeploymentMode.WslDocker:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslDockerStopCommand(), TimeoutSeconds = 45, ActionName = "stop", Mode = mode };
                    case DeploymentMode.WslNative:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslNativeStopCommand(), TimeoutSeconds = 45, ActionName = "stop", Mode = mode };
                    case DeploymentMode.WinDocker:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildWinDockerStopCommand(), TimeoutSeconds = 45, ActionName = "stop", Mode = mode };
                    case DeploymentMode.WinNative:
                    default:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildWinNativeStopCommand(), TimeoutSeconds = 45, ActionName = "stop", Mode = mode };
                }
            }

            private CommandSpec BuildOpenDashboardCommandSpec()
            {
                DeploymentMode mode = _effectiveMode;
                string browserTarget = (_dashboardBrowserTarget ?? string.Empty).Trim().ToLowerInvariant();
                if (browserTarget == "wsl_chrome")
                {
                    return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslNativeOpenDashboardCommand(), TimeoutSeconds = 120, ActionName = "dashboard", Mode = DeploymentMode.WslNative };
                }
                switch (mode)
                {
                    case DeploymentMode.WslDocker:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslDockerOpenDashboardCommand(), TimeoutSeconds = 120, ActionName = "dashboard", Mode = mode };
                    case DeploymentMode.WslNative:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslNativeOpenDashboardCommand(), TimeoutSeconds = 120, ActionName = "dashboard", Mode = mode };
                    case DeploymentMode.WinDocker:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildWinDockerOpenDashboardCommand(), TimeoutSeconds = 30, ActionName = "dashboard", Mode = mode };
                    case DeploymentMode.WinNative:
                    default:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildWinNativeOpenDashboardCommand(), TimeoutSeconds = 30, ActionName = "dashboard", Mode = mode };
                }
            }

            private CommandSpec BuildStatusCommandSpec()
            {
                DeploymentMode mode = _effectiveMode;
                switch (mode)
                {
                    case DeploymentMode.WslDocker:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslDockerStatusCommand(), TimeoutSeconds = 55, ActionName = "status", Mode = mode };
                    case DeploymentMode.WslNative:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildWslNativeStatusCommand(), TimeoutSeconds = 45, ActionName = "status", Mode = mode };
                    case DeploymentMode.WinDocker:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildWinDockerStatusCommand(), TimeoutSeconds = 45, ActionName = "status", Mode = mode };
                    case DeploymentMode.WinNative:
                    default:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildWinNativeStatusCommand(), TimeoutSeconds = 45, ActionName = "status", Mode = mode };
                }
            }

            private CommandSpec BuildInstallCommandSpec()
            {
                DeploymentMode mode = _effectiveMode;
                switch (mode)
                {
                    case DeploymentMode.WslDocker:
                    case DeploymentMode.WslNative:
                        return new CommandSpec { Runtime = CommandRuntime.WslBash, Command = BuildInstallCommandForMode(mode), TimeoutSeconds = 300, ActionName = "install", Mode = mode };
                    case DeploymentMode.WinDocker:
                    case DeploymentMode.WinNative:
                    default:
                        return new CommandSpec { Runtime = CommandRuntime.WindowsPowerShell, Command = BuildInstallCommandForMode(mode), TimeoutSeconds = 300, ActionName = "install", Mode = mode };
                }
            }

            private void SetLanguage(UiLanguage language, bool persist, bool announce)
            {
                if (_language == language)
                {
                    if (persist)
                    {
                        SavePreferences();
                    }
                    return;
                }
                _language = language;
                ApplyLanguageTexts();
                if (persist)
                {
                    SavePreferences();
                }
                if (announce)
                {
                    if (language == UiLanguage.English)
                    {
                        AppendLog("Language switched to English.");
                    }
                    else if (language == UiLanguage.ChineseTraditional)
                    {
                        AppendLog("語言已切換為繁體中文。");
                    }
                    else
                    {
                        AppendLog("语言已切换为简体中文。");
                    }
                }
            }

            private void SetTheme(UiTheme theme, bool persist, bool announce)
            {
                if (_theme == theme)
                {
                    if (persist)
                    {
                        SavePreferences();
                    }
                    return;
                }

                _theme = theme;
                ApplyThemeVisuals();
                ApplyLanguageTexts();
                if (persist)
                {
                    SavePreferences();
                }
                if (announce)
                {
                    AppendLog(theme == UiTheme.Dark
                        ? Tr("Theme switched to Dark.", "已切换为深色主题。")
                        : Tr("Theme switched to Light.", "已切换为浅色主题。"));
                }
            }

            private static string FormatRect(Rectangle rect)
            {
                return rect.X + "," + rect.Y + " " + rect.Width + "x" + rect.Height;
            }

            private static Rectangle GetWorkingAreaForOwner(Control owner)
            {
                try
                {
                    if (owner != null)
                    {
                        return Screen.FromControl(owner).WorkingArea;
                    }
                }
                catch
                {
                }

                try
                {
                    if (Screen.PrimaryScreen != null)
                    {
                        return Screen.PrimaryScreen.WorkingArea;
                    }
                }
                catch
                {
                }

                return new Rectangle(0, 0, 1024, 768);
            }

            private static void EnsureDialogOnScreen(Form dialog, Control owner, int margin, string phase)
            {
                if (dialog == null)
                {
                    return;
                }

                Rectangle wa = GetWorkingAreaForOwner(owner);
                Rectangle before = dialog.Bounds;

                int width = before.Width;
                int height = before.Height;
                int maxWidth = Math.Max(240, wa.Width - margin * 2);
                int maxHeight = Math.Max(240, wa.Height - margin * 2);
                if (width > maxWidth) width = maxWidth;
                if (height > maxHeight) height = maxHeight;

                int x = before.X;
                int y = before.Y;
                int minX = wa.Left + margin;
                int minY = wa.Top + margin;
                int maxX = wa.Right - margin - width;
                int maxY = wa.Bottom - margin - height;

                if (x < minX) x = minX;
                if (y < minY) y = minY;
                if (x > maxX) x = maxX;
                if (y > maxY) y = maxY;

                Rectangle after = new Rectangle(x, y, width, height);
                if (after != before)
                {
                    dialog.Bounds = after;
                }

                Program.TryWriteDiagnostic(
                    "settings-dialog",
                    phase + " wa=" + FormatRect(wa) + " before=" + FormatRect(before) + " after=" + FormatRect(dialog.Bounds));
            }

            private static void PlaceDialogCenteredOnOwnerScreen(Form dialog, Control owner, int margin, string phasePrefix)
            {
                if (dialog == null)
                {
                    return;
                }

                Rectangle wa = GetWorkingAreaForOwner(owner);
                int width = dialog.Width;
                int height = dialog.Height;
                int maxWidth = Math.Max(240, wa.Width - margin * 2);
                int maxHeight = Math.Max(240, wa.Height - margin * 2);
                if (width > maxWidth) width = maxWidth;
                if (height > maxHeight) height = maxHeight;

                int x = wa.Left + Math.Max(0, (wa.Width - width) / 2);
                int y = wa.Top + Math.Max(0, (wa.Height - height) / 2);
                Rectangle before = dialog.Bounds;
                dialog.Bounds = new Rectangle(x, y, width, height);
                EnsureDialogOnScreen(dialog, owner, margin, phasePrefix + " pre-show-clamp");
                Program.TryWriteDiagnostic(
                    "settings-dialog",
                    phasePrefix + " pre-show-center wa=" + FormatRect(wa) + " before=" + FormatRect(before) + " after=" + FormatRect(dialog.Bounds));
            }

            private void ShowSettingsDialog(int attemptId)
            {
                int traceStep = 0;
                var traceWatch = Stopwatch.StartNew();
                Action<string> trace = delegate(string msg)
                {
                    traceStep++;
                    Program.TryWriteDiagnostic(
                        "settings-dialog",
                        "attempt=" + attemptId + " step=" + traceStep.ToString("D2") + " t=" + traceWatch.ElapsedMilliseconds + "ms " + msg);
                };

                trace("constructing");
                using (var dialog = new Form())
                {
                    dialog.Text = Tr("Settings", "设置");
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;
                    dialog.ClientSize = new Size(1020, 700);
                    dialog.Font = new Font("Segoe UI", 9.8F, FontStyle.Regular);
                    dialog.Padding = new Padding(14);
                    dialog.KeyPreview = true;
                    dialog.KeyDown += delegate(object sender, KeyEventArgs e)
                    {
                        if (e.KeyCode == Keys.Escape)
                        {
                            try { dialog.DialogResult = DialogResult.Cancel; } catch { }
                            try { dialog.Close(); } catch { }
                            e.Handled = true;
                        }
                    };
                    trace("form-created bounds=" + FormatRect(dialog.Bounds));
                    try
                    {
                        if (Icon != null)
                        {
                            dialog.Icon = Icon;
                        }
                    }
                    catch
                    {
                    }

                    bool dark = _theme == UiTheme.Dark;
                    Color panelBack = dark ? Color.FromArgb(39, 46, 58) : Color.FromArgb(241, 244, 250);
                    Color pageBack = dark ? Color.FromArgb(34, 40, 52) : Color.FromArgb(246, 248, 252);
                    Color inputBack = dark ? Color.FromArgb(28, 34, 44) : Color.White;
                    Color inputFore = dark ? Color.FromArgb(236, 241, 250) : Color.FromArgb(32, 43, 62);
                    dialog.BackColor = dark ? Color.FromArgb(33, 39, 50) : Color.FromArgb(246, 248, 252);
                    dialog.ForeColor = dark ? Color.FromArgb(230, 236, 248) : Color.FromArgb(32, 43, 62);
                    trace("base-props bounds=" + FormatRect(dialog.Bounds) + " wa=" + FormatRect(GetWorkingAreaForOwner(this)));

                    var root = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 1,
                        RowCount = 2,
                        BackColor = pageBack
                    };
                    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
                    trace("layout-root-created");

                    var tabs = new TabControl
                    {
                        Dock = DockStyle.Fill,
                        BackColor = pageBack,
                        ForeColor = dialog.ForeColor
                    };
                    const int settingsTabWidth = 124;
                    const int settingsTabHeight = 34;
                    const int settingsTabBaselineY = 2;
                    tabs.SizeMode = TabSizeMode.Fixed;
                    tabs.ItemSize = new Size(settingsTabWidth, settingsTabHeight);
                    tabs.Font = new Font("Segoe UI Semibold", 9.8F, FontStyle.Bold);
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                    tabs.DrawItem += delegate(object sender, DrawItemEventArgs e)
                    {
                        if (e.Index < 0 || e.Index >= tabs.TabPages.Count)
                        {
                            return;
                        }
                        // Keep tab size stable for selected/unselected; only color changes.
                        bool selected = tabs.SelectedIndex == e.Index;
                        Rectangle raw = tabs.GetTabRect(e.Index);
                        Rectangle r = new Rectangle(
                            raw.X,
                            settingsTabBaselineY,
                            Math.Max(1, raw.Width - 1),
                            Math.Max(1, settingsTabHeight - 1));
                        Color fill = dark
                            ? (selected ? Color.FromArgb(63, 74, 96) : Color.FromArgb(43, 50, 64))
                            : (selected ? Color.FromArgb(224, 232, 246) : Color.FromArgb(240, 244, 251));
                        Color border = dark ? Color.FromArgb(82, 96, 122) : Color.FromArgb(188, 202, 226);
                        Color textColor = dark ? Color.FromArgb(234, 240, 251) : Color.FromArgb(37, 50, 76);
                        using (var b = new SolidBrush(fill))
                        using (var p = new Pen(border))
                        using (var t = new SolidBrush(textColor))
                        {
                            e.Graphics.FillRectangle(b, r);
                            e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                            TextRenderer.DrawText(
                                e.Graphics,
                                tabs.TabPages[e.Index].Text,
                                tabs.Font,
                                r,
                                textColor,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                        }
                    };
                    trace("layout-tabs-created");

                    var generalTab = new TabPage(Tr("GENERAL", "常规"));
                    var modeTab = new TabPage(Tr("MODE", "模式"));
                    var pathsTab = new TabPage(Tr("PATHS", "路径"));
                    var dockerTab = new TabPage(Tr("DOCKER", "Docker"));
                    var nativeTab = new TabPage(Tr("NATIVE", "原生"));
                    var networkTab = new TabPage(Tr("NETWORK", "网络"));
                    var setupTab = new TabPage(Tr("SETUP", "初始化"));
                    generalTab.UseVisualStyleBackColor = false;
                    modeTab.UseVisualStyleBackColor = false;
                    pathsTab.UseVisualStyleBackColor = false;
                    dockerTab.UseVisualStyleBackColor = false;
                    nativeTab.UseVisualStyleBackColor = false;
                    networkTab.UseVisualStyleBackColor = false;
                    setupTab.UseVisualStyleBackColor = false;
                    generalTab.BackColor = pageBack;
                    modeTab.BackColor = pageBack;
                    pathsTab.BackColor = pageBack;
                    dockerTab.BackColor = pageBack;
                    nativeTab.BackColor = pageBack;
                    networkTab.BackColor = pageBack;
                    setupTab.BackColor = pageBack;
                    generalTab.ForeColor = dialog.ForeColor;
                    modeTab.ForeColor = dialog.ForeColor;
                    pathsTab.ForeColor = dialog.ForeColor;
                    dockerTab.ForeColor = dialog.ForeColor;
                    nativeTab.ForeColor = dialog.ForeColor;
                    networkTab.ForeColor = dialog.ForeColor;
                    setupTab.ForeColor = dialog.ForeColor;
                    tabs.TabPages.Add(generalTab);
                    tabs.TabPages.Add(modeTab);
                    tabs.TabPages.Add(pathsTab);
                    tabs.TabPages.Add(dockerTab);
                    tabs.TabPages.Add(nativeTab);
                    tabs.TabPages.Add(networkTab);
                    tabs.TabPages.Add(setupTab);

                    var generalLayout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 1,
                        RowCount = 3,
                        BackColor = pageBack,
                        Padding = new Padding(8)
                    };
                    generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
                    generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
                    generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));

                    var languageGroup = new GroupBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("Language", "语言"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack
                    };
                    var languagePanel = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        FlowDirection = FlowDirection.LeftToRight,
                        Padding = new Padding(8, 8, 8, 8),
                        BackColor = panelBack,
                        WrapContents = false
                    };
                    var rbEnglish = new RadioButton
                    {
                        AutoSize = true,
                        Text = "English",
                        Checked = _language == UiLanguage.English,
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Margin = new Padding(4, 4, 24, 4)
                    };
                    var rbChinese = new RadioButton
                    {
                        AutoSize = true,
                        Text = "简体中文",
                        Checked = _language == UiLanguage.ChineseSimplified,
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Margin = new Padding(4, 4, 24, 4)
                    };
                    var rbChineseTraditional = new RadioButton
                    {
                        AutoSize = true,
                        Text = "繁體中文",
                        Checked = _language == UiLanguage.ChineseTraditional,
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Margin = new Padding(4)
                    };
                    languagePanel.Controls.Add(rbEnglish);
                    languagePanel.Controls.Add(rbChinese);
                    languagePanel.Controls.Add(rbChineseTraditional);
                    languageGroup.Controls.Add(languagePanel);

                    var themeGroup = new GroupBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("Theme", "主题"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack
                    };
                    var themePanel = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        FlowDirection = FlowDirection.LeftToRight,
                        Padding = new Padding(8, 8, 8, 8),
                        BackColor = panelBack,
                        WrapContents = false
                    };
                    var rbLight = new RadioButton
                    {
                        AutoSize = true,
                        Text = Tr("Light", "浅色"),
                        Checked = _theme == UiTheme.Light,
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Margin = new Padding(4, 4, 24, 4)
                    };
                    var rbDark = new RadioButton
                    {
                        AutoSize = true,
                        Text = Tr("Dark", "深色"),
                        Checked = _theme == UiTheme.Dark,
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Margin = new Padding(4)
                    };
                    themePanel.Controls.Add(rbLight);
                    themePanel.Controls.Add(rbDark);
                    themeGroup.Controls.Add(themePanel);

                    var modeHintGroup = new GroupBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("Current Mode", "当前模式"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack
                    };
                    var modeHintLabel = new Label
                    {
                        Dock = DockStyle.Fill,
                        AutoSize = false,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding = new Padding(12, 8, 12, 8),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Text = Tr("Effective: ", "生效模式：") +
                               Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)) + Environment.NewLine +
                               Tr("Last detected: ", "最近检测：") +
                               Tr(ModeLabelEnglish(_lastDetectedMode), ModeLabelChinese(_lastDetectedMode))
                    };
                    modeHintGroup.Controls.Add(modeHintLabel);

                    generalLayout.Controls.Add(languageGroup, 0, 0);
                    generalLayout.Controls.Add(themeGroup, 0, 1);
                    generalLayout.Controls.Add(modeHintGroup, 0, 2);
                    generalTab.Controls.Add(generalLayout);
                    trace("layout-general-ready");

                    var modeLayout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 1,
                        RowCount = 3,
                        BackColor = pageBack,
                        Padding = new Padding(10, 10, 10, 10)
                    };
                    modeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
                    modeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
                    modeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                    var chkAutoDetectMode = new CheckBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("Auto detect deployment mode", "自动检测部署模式"),
                        Checked = _autoDetectMode,
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Padding = new Padding(12, 6, 12, 6),
                        AutoSize = false
                    };

                    var manualModePanel = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 2,
                        RowCount = 1,
                        BackColor = panelBack,
                        Padding = new Padding(12, 8, 12, 8)
                    };
                    manualModePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
                    manualModePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    var lblManualMode = new Label
                    {
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor,
                        Text = Tr("Manual Mode", "手动模式")
                    };
                    var cboManualMode = new ComboBox
                    {
                        Dock = DockStyle.Fill,
                        DropDownStyle = ComboBoxStyle.DropDownList
                    };
                    cboManualMode.Items.Add(new EnumComboItem<DeploymentMode>(DeploymentMode.WslDocker, "WSL + Docker"));
                    cboManualMode.Items.Add(new EnumComboItem<DeploymentMode>(DeploymentMode.WslNative, Tr("WSL Native", "WSL 原生")));
                    cboManualMode.Items.Add(new EnumComboItem<DeploymentMode>(DeploymentMode.WinDocker, "Windows + Docker"));
                    cboManualMode.Items.Add(new EnumComboItem<DeploymentMode>(DeploymentMode.WinNative, Tr("Windows Native", "Windows 原生")));
                    for (int i = 0; i < cboManualMode.Items.Count; i++)
                    {
                        var item = cboManualMode.Items[i] as EnumComboItem<DeploymentMode>;
                        if (item != null && item.Value == ForceConcreteMode(_manualDeploymentMode))
                        {
                            cboManualMode.SelectedIndex = i;
                            break;
                        }
                    }
                    if (cboManualMode.SelectedIndex < 0)
                    {
                        cboManualMode.SelectedIndex = 0;
                    }
                    manualModePanel.Controls.Add(lblManualMode, 0, 0);
                    manualModePanel.Controls.Add(cboManualMode, 1, 0);

                    var modeHint = new Label
                    {
                        Dock = DockStyle.Fill,
                        AutoSize = false,
                        Margin = new Padding(12, 8, 12, 8),
                        TextAlign = ContentAlignment.TopLeft,
                        ForeColor = dark ? Color.FromArgb(181, 194, 216) : Color.FromArgb(74, 88, 114),
                        Text = Tr(
                            "Auto mode priority: WSL+Docker > WSL Native > Windows+Docker > Windows Native.",
                            "自动模式优先级：WSL+Docker > WSL 原生 > Windows+Docker > Windows 原生。")
                    };

                    modeLayout.Controls.Add(chkAutoDetectMode, 0, 0);
                    modeLayout.Controls.Add(manualModePanel, 0, 1);
                    modeLayout.Controls.Add(modeHint, 0, 2);
                    modeTab.Controls.Add(modeLayout);
                    trace("layout-mode-ready");

                    var pathsRoot = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 1,
                        RowCount = 2,
                        BackColor = pageBack,
                        Padding = new Padding(10, 10, 10, 10)
                    };
                    pathsRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    pathsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                    pathsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

                    var pathsGroup = new GroupBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("PATHS", "路径"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Padding = new Padding(10)
                    };
                    var pathsLayout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 2,
                        RowCount = 10,
                        BackColor = panelBack
                    };
                    pathsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205F));
                    pathsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    for (int i = 0; i < 10; i++)
                    {
                        pathsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
                    }

                    var lblWindowsProject = new Label
                    {
                        Text = Tr("Windows Project Dir", "Windows 项目目录"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWindowsProject = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _windowsProjectDir
                    };

                    var lblWslProject = new Label
                    {
                        Text = Tr("WSL Project Dir", "WSL 项目目录"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWslProject = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _wslProjectDir
                    };

                    var lblWslOpenclaw = new Label
                    {
                        Text = Tr("WSL OpenClaw Dir", "WSL OpenClaw 目录"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWslOpenclaw = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _wslOpenclawDir
                    };

                    var lblWslData = new Label
                    {
                        Text = Tr("WSL Data Dir", "WSL 数据目录"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWslData = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _wslDataDir
                    };

                    var lblStartScript = new Label
                    {
                        Text = Tr("Start Script (WSL)", "启动脚本 (WSL)"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtStartScript = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _wslStartScriptPath
                    };

                    var lblDashboardScript = new Label
                    {
                        Text = Tr("Dashboard Script (WSL)", "Dashboard 脚本 (WSL)"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtDashboardScript = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _wslOpenDashboardScriptPath
                    };

                    var lblWslNativeProject = new Label
                    {
                        Text = Tr("WSL Native Project", "WSL 原生项目目录"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWslNativeProject = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _wslNativeProjectDir
                    };

                    var lblWinDockerDir = new Label
                    {
                        Text = Tr("Win Docker OpenClaw", "Win Docker OpenClaw"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWinDockerDir = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _winDockerOpenclawDir
                    };

                    var lblWinDockerData = new Label
                    {
                        Text = Tr("Win Docker Data Dir", "Win Docker 数据目录"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWinDockerData = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _winDockerDataDir
                    };

                    var lblWinNativeProject = new Label
                    {
                        Text = Tr("Win Native Project", "Win 原生项目目录"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWinNativeProject = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _winNativeProjectDir
                    };

                    pathsLayout.Controls.Add(lblWindowsProject, 0, 0);
                    pathsLayout.Controls.Add(txtWindowsProject, 1, 0);
                    pathsLayout.Controls.Add(lblWslProject, 0, 1);
                    pathsLayout.Controls.Add(txtWslProject, 1, 1);
                    pathsLayout.Controls.Add(lblWslOpenclaw, 0, 2);
                    pathsLayout.Controls.Add(txtWslOpenclaw, 1, 2);
                    pathsLayout.Controls.Add(lblWslData, 0, 3);
                    pathsLayout.Controls.Add(txtWslData, 1, 3);
                    pathsLayout.Controls.Add(lblStartScript, 0, 4);
                    pathsLayout.Controls.Add(txtStartScript, 1, 4);
                    pathsLayout.Controls.Add(lblDashboardScript, 0, 5);
                    pathsLayout.Controls.Add(txtDashboardScript, 1, 5);
                    pathsLayout.Controls.Add(lblWslNativeProject, 0, 6);
                    pathsLayout.Controls.Add(txtWslNativeProject, 1, 6);
                    pathsLayout.Controls.Add(lblWinDockerDir, 0, 7);
                    pathsLayout.Controls.Add(txtWinDockerDir, 1, 7);
                    pathsLayout.Controls.Add(lblWinDockerData, 0, 8);
                    pathsLayout.Controls.Add(txtWinDockerData, 1, 8);
                    pathsLayout.Controls.Add(lblWinNativeProject, 0, 9);
                    pathsLayout.Controls.Add(txtWinNativeProject, 1, 9);

                    pathsGroup.Controls.Add(pathsLayout);

                    var lblPathsHint = new Label
                    {
                        Text = Tr(
                            "Tip: WSL relative paths are resolved from WSL Project Dir.",
                            "提示：WSL 相对路径将按 WSL 项目目录解析。"),
                        Dock = DockStyle.Fill,
                        AutoSize = false,
                        Margin = new Padding(6, 6, 6, 0),
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dark ? Color.FromArgb(171, 185, 208) : Color.FromArgb(74, 88, 114)
                    };

                    pathsRoot.Controls.Add(pathsGroup, 0, 0);
                    pathsRoot.Controls.Add(lblPathsHint, 0, 1);
                    pathsTab.Controls.Add(pathsRoot);
                    trace("layout-paths-ready");

                    var dockerRoot = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 1,
                        RowCount = 2,
                        BackColor = pageBack,
                        Padding = new Padding(10, 10, 10, 10)
                    };
                    dockerRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 196F));
                    dockerRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                    var dockerGroup = new GroupBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("DOCKER", "Docker"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Padding = new Padding(10)
                    };
                    var dockerLayout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 2,
                        RowCount = 3,
                        BackColor = panelBack
                    };
                    dockerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205F));
                    dockerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    dockerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                    dockerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                    dockerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                    var lblDockerCompose = new Label
                    {
                        Text = Tr("Docker Compose Cmd", "Docker Compose 命令"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtDockerCompose = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _dockerComposeCommand
                    };
                    var lblGatewayService = new Label
                    {
                        Text = Tr("Gateway Service Name", "网关服务名"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtGatewayService = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _gatewayServiceName
                    };
                    var lblGateway = new Label
                    {
                        Text = Tr("Gateway Root URL", "网关根地址"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtGateway = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = NormalizeGatewayRootUrl(_gatewayRootUrl)
                    };
                    dockerLayout.Controls.Add(lblDockerCompose, 0, 0);
                    dockerLayout.Controls.Add(txtDockerCompose, 1, 0);
                    dockerLayout.Controls.Add(lblGatewayService, 0, 1);
                    dockerLayout.Controls.Add(txtGatewayService, 1, 1);
                    dockerLayout.Controls.Add(lblGateway, 0, 2);
                    dockerLayout.Controls.Add(txtGateway, 1, 2);
                    dockerGroup.Controls.Add(dockerLayout);

                    var browserGroup = new GroupBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("DASHBOARD", "Dashboard"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Padding = new Padding(10)
                    };
                    var browserLayout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 2,
                        RowCount = 2,
                        BackColor = panelBack
                    };
                    browserLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205F));
                    browserLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    browserLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                    browserLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                    var lblBrowserTarget = new Label
                    {
                        Text = Tr("Browser Target", "浏览器目标"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var cboBrowserTarget = new ComboBox
                    {
                        Dock = DockStyle.Fill,
                        DropDownStyle = ComboBoxStyle.DropDown
                    };
                    cboBrowserTarget.Items.Add("auto");
                    cboBrowserTarget.Items.Add("wsl_chrome");
                    cboBrowserTarget.Items.Add("windows_default");
                    cboBrowserTarget.Text = string.IsNullOrWhiteSpace(_dashboardBrowserTarget) ? DefaultDashboardBrowserTarget : _dashboardBrowserTarget;

                    var lblWslChromeBackend = new Label
                    {
                        Text = Tr("WSL Chrome Backend", "WSL Chrome 后端"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var cboWslChromeBackend = new ComboBox
                    {
                        Dock = DockStyle.Fill,
                        DropDownStyle = ComboBoxStyle.DropDownList
                    };
                    cboWslChromeBackend.Items.Add(new EnumComboItem<string>("wayland", "Wayland"));
                    cboWslChromeBackend.Items.Add(new EnumComboItem<string>("x11", "X11"));
                    cboWslChromeBackend.Items.Add(new EnumComboItem<string>("auto", Tr("Auto", "自动")));
                    string selectedChromeBackend = NormalizeWslChromeBackend(_wslChromeBackend);
                    for (int i = 0; i < cboWslChromeBackend.Items.Count; i++)
                    {
                        var item = cboWslChromeBackend.Items[i] as EnumComboItem<string>;
                        if (item != null && string.Equals(item.Value, selectedChromeBackend, StringComparison.OrdinalIgnoreCase))
                        {
                            cboWslChromeBackend.SelectedIndex = i;
                            break;
                        }
                    }
                    if (cboWslChromeBackend.SelectedIndex < 0)
                    {
                        cboWslChromeBackend.SelectedIndex = 0;
                    }
                    browserLayout.Controls.Add(lblBrowserTarget, 0, 0);
                    browserLayout.Controls.Add(cboBrowserTarget, 1, 0);
                    browserLayout.Controls.Add(lblWslChromeBackend, 0, 1);
                    browserLayout.Controls.Add(cboWslChromeBackend, 1, 1);
                    browserGroup.Controls.Add(browserLayout);

                    dockerRoot.Controls.Add(dockerGroup, 0, 0);
                    dockerRoot.Controls.Add(browserGroup, 0, 1);
                    dockerTab.Controls.Add(dockerRoot);
                    trace("layout-docker-ready");

                    var nativeRoot = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 1,
                        RowCount = 1,
                        BackColor = pageBack,
                        Padding = new Padding(10, 10, 10, 10)
                    };
                    var nativeGroup = new GroupBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("NATIVE COMMANDS", "原生命令"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Padding = new Padding(10)
                    };
                    var nativeLayout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 2,
                        RowCount = 4,
                        BackColor = panelBack
                    };
                    nativeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
                    nativeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    for (int i = 0; i < 4; i++)
                    {
                        nativeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                    }
                    var lblWslNativeCommand = new Label
                    {
                        Text = Tr("WSL Native openclaw cmd", "WSL 原生 openclaw 命令"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWslNativeCommand = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _wslNativeOpenclawCommand
                    };
                    var lblWinNativeCommand = new Label
                    {
                        Text = Tr("Win Native openclaw cmd", "Win 原生 openclaw 命令"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWinNativeCommand = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _winNativeOpenclawCommand
                    };
                    var lblWslInstallCommand = new Label
                    {
                        Text = Tr("WSL install command", "WSL 安装命令"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWslInstallCommand = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _wslNativeInstallCommand
                    };
                    var lblWinInstallCommand = new Label
                    {
                        Text = Tr("Win install command", "Win 安装命令"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWinInstallCommand = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _winNativeInstallCommand
                    };
                    nativeLayout.Controls.Add(lblWslNativeCommand, 0, 0);
                    nativeLayout.Controls.Add(txtWslNativeCommand, 1, 0);
                    nativeLayout.Controls.Add(lblWinNativeCommand, 0, 1);
                    nativeLayout.Controls.Add(txtWinNativeCommand, 1, 1);
                    nativeLayout.Controls.Add(lblWslInstallCommand, 0, 2);
                    nativeLayout.Controls.Add(txtWslInstallCommand, 1, 2);
                    nativeLayout.Controls.Add(lblWinInstallCommand, 0, 3);
                    nativeLayout.Controls.Add(txtWinInstallCommand, 1, 3);
                    nativeGroup.Controls.Add(nativeLayout);
                    nativeRoot.Controls.Add(nativeGroup, 0, 0);
                    nativeTab.Controls.Add(nativeRoot);
                    trace("layout-native-ready");

                    var networkRoot = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 1,
                        RowCount = 1,
                        BackColor = pageBack,
                        Padding = new Padding(10, 10, 10, 10)
                    };
                    var networkGroup = new GroupBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Tr("PROXY", "代理"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Padding = new Padding(10)
                    };
                    var networkLayout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 2,
                        RowCount = 7,
                        BackColor = panelBack
                    };
                    networkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
                    networkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    for (int i = 0; i < 7; i++)
                    {
                        networkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                    }
                    var lblProxyMode = new Label
                    {
                        Text = Tr("Proxy Mode", "代理模式"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var cboProxyMode = new ComboBox
                    {
                        Dock = DockStyle.Fill,
                        DropDownStyle = ComboBoxStyle.DropDownList
                    };
                    cboProxyMode.Items.Add(new EnumComboItem<ProxyMode>(ProxyMode.System, Tr("System", "系统")));
                    cboProxyMode.Items.Add(new EnumComboItem<ProxyMode>(ProxyMode.WslClashAuto, Tr("WSL Clash Auto", "WSL Clash 自动")));
                    cboProxyMode.Items.Add(new EnumComboItem<ProxyMode>(ProxyMode.Custom, Tr("Custom", "自定义")));
                    cboProxyMode.Items.Add(new EnumComboItem<ProxyMode>(ProxyMode.Off, Tr("Off", "关闭")));
                    for (int i = 0; i < cboProxyMode.Items.Count; i++)
                    {
                        var item = cboProxyMode.Items[i] as EnumComboItem<ProxyMode>;
                        if (item != null && item.Value == _proxyMode)
                        {
                            cboProxyMode.SelectedIndex = i;
                            break;
                        }
                    }
                    if (cboProxyMode.SelectedIndex < 0)
                    {
                        cboProxyMode.SelectedIndex = 0;
                    }
                    var lblHttpProxy = new Label
                    {
                        Text = "HTTP_PROXY",
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtHttpProxy = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _customHttpProxy
                    };
                    var lblHttpsProxy = new Label
                    {
                        Text = "HTTPS_PROXY",
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtHttpsProxy = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _customHttpsProxy
                    };
                    var lblAllProxy = new Label
                    {
                        Text = "ALL_PROXY",
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtAllProxy = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _customAllProxy
                    };
                    var lblNoProxy = new Label
                    {
                        Text = "NO_PROXY",
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtNoProxy = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = _customNoProxy
                    };
                    var lblWslSudoPassword = new Label
                    {
                        Text = Tr("WSL sudo Password", "WSL sudo 密码"),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dialog.ForeColor
                    };
                    var txtWslSudoPassword = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        UseSystemPasswordChar = true
                    };
                    var lblWslSudoPasswordHint = new Label
                    {
                        Text = string.Empty,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = dark ? Color.FromArgb(178, 189, 210) : Color.FromArgb(86, 100, 126)
                    };
                    var chkClearWslSudoPassword = new CheckBox
                    {
                        AutoSize = true,
                        Text = Tr("Clear stored password", "清除已保存密码"),
                        ForeColor = dialog.ForeColor,
                        BackColor = panelBack,
                        Margin = new Padding(0, 8, 0, 0)
                    };
                    networkLayout.Controls.Add(lblProxyMode, 0, 0);
                    networkLayout.Controls.Add(cboProxyMode, 1, 0);
                    networkLayout.Controls.Add(lblHttpProxy, 0, 1);
                    networkLayout.Controls.Add(txtHttpProxy, 1, 1);
                    networkLayout.Controls.Add(lblHttpsProxy, 0, 2);
                    networkLayout.Controls.Add(txtHttpsProxy, 1, 2);
                    networkLayout.Controls.Add(lblAllProxy, 0, 3);
                    networkLayout.Controls.Add(txtAllProxy, 1, 3);
                    networkLayout.Controls.Add(lblNoProxy, 0, 4);
                    networkLayout.Controls.Add(txtNoProxy, 1, 4);
                    networkLayout.Controls.Add(lblWslSudoPassword, 0, 5);
                    networkLayout.Controls.Add(txtWslSudoPassword, 1, 5);
                    networkLayout.Controls.Add(chkClearWslSudoPassword, 1, 6);
                    networkLayout.Controls.Add(lblWslSudoPasswordHint, 0, 6);
                    networkGroup.Controls.Add(networkLayout);
                    networkRoot.Controls.Add(networkGroup, 0, 0);
                    networkTab.Controls.Add(networkRoot);
                    trace("layout-network-ready");

                    var setupRoot = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 1,
                        RowCount = 2,
                        BackColor = pageBack,
                        Padding = new Padding(14, 14, 14, 14)
                    };
                    setupRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                    setupRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
                    var setupHint = new Label
                    {
                        Dock = DockStyle.Fill,
                        AutoSize = false,
                        TextAlign = ContentAlignment.TopLeft,
                        ForeColor = dialog.ForeColor,
                        Text =
                            Tr("Use this action to run install/init flow for current mode.", "用于执行当前模式的安装/初始化流程。") +
                            Environment.NewLine + Environment.NewLine +
                            Tr("When mode is Auto, it will use the detected mode.", "若模式为自动，将按检测结果执行。") +
                            Environment.NewLine +
                            Tr("Recommended after changing paths or mode.", "建议在变更路径或模式后执行。")
                    };
                    var btnRunSetup = new Button
                    {
                        Text = Tr("Run Install / Init Now", "立即执行安装 / 初始化"),
                        Dock = DockStyle.Right,
                        Width = 220,
                        Height = 34,
                        FlatStyle = FlatStyle.Flat,
                        BackColor = dark ? Color.FromArgb(78, 104, 148) : Color.FromArgb(217, 228, 248),
                        ForeColor = dark ? Color.FromArgb(242, 246, 255) : Color.FromArgb(26, 46, 79)
                    };
                    btnRunSetup.FlatAppearance.BorderColor = dark ? Color.FromArgb(104, 130, 178) : Color.FromArgb(168, 184, 216);
                    bool runSetupRequested = false;
                    var setupButtonHost = new Panel
                    {
                        Dock = DockStyle.Fill,
                        BackColor = Color.Transparent
                    };
                    setupButtonHost.Controls.Add(btnRunSetup);
                    setupButtonHost.Resize += delegate
                    {
                        btnRunSetup.Location = new Point(Math.Max(0, setupButtonHost.ClientSize.Width - btnRunSetup.Width), Math.Max(0, (setupButtonHost.ClientSize.Height - btnRunSetup.Height) / 2));
                    };
                    setupRoot.Controls.Add(setupHint, 0, 0);
                    setupRoot.Controls.Add(setupButtonHost, 0, 1);
                    setupTab.Controls.Add(setupRoot);
                    trace("layout-setup-ready");

                    var buttonsPanel = new Panel
                    {
                        Dock = DockStyle.Fill,
                        BackColor = pageBack
                    };
                    var btnOk = new Button
                    {
                        Text = Tr("Apply", "应用"),
                        DialogResult = DialogResult.None,
                        Size = new Size(98, 34),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = dark ? Color.FromArgb(72, 92, 130) : Color.FromArgb(213, 224, 246),
                        ForeColor = dark ? Color.FromArgb(241, 246, 255) : Color.FromArgb(27, 45, 79),
                        Anchor = AnchorStyles.Right | AnchorStyles.Top
                    };
                    btnOk.FlatAppearance.BorderColor = dark ? Color.FromArgb(98, 118, 156) : Color.FromArgb(170, 184, 214);
                    var btnConfirm = new Button
                    {
                        Text = Tr("Done", "确定"),
                        DialogResult = DialogResult.None,
                        Size = new Size(98, 34),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = dark ? Color.FromArgb(82, 106, 146) : Color.FromArgb(201, 216, 244),
                        ForeColor = dark ? Color.FromArgb(244, 248, 255) : Color.FromArgb(24, 44, 77),
                        Anchor = AnchorStyles.Right | AnchorStyles.Top
                    };
                    btnConfirm.FlatAppearance.BorderColor = dark ? Color.FromArgb(108, 132, 176) : Color.FromArgb(162, 179, 212);
                    var btnCancel = new Button
                    {
                        Text = Tr("Cancel", "取消"),
                        DialogResult = DialogResult.Cancel,
                        Size = new Size(98, 34),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = dark ? Color.FromArgb(54, 61, 74) : Color.FromArgb(232, 236, 244),
                        ForeColor = dark ? Color.FromArgb(227, 233, 246) : Color.FromArgb(46, 59, 82),
                        Anchor = AnchorStyles.Right | AnchorStyles.Top
                    };
                    btnCancel.FlatAppearance.BorderColor = dark ? Color.FromArgb(88, 99, 118) : Color.FromArgb(182, 193, 214);

                    buttonsPanel.Controls.Add(btnConfirm);
                    buttonsPanel.Controls.Add(btnOk);
                    buttonsPanel.Controls.Add(btnCancel);
                    buttonsPanel.Resize += delegate
                    {
                        int top = 6;
                        btnCancel.Location = new Point(Math.Max(0, buttonsPanel.ClientSize.Width - btnCancel.Width), top);
                        btnOk.Location = new Point(Math.Max(0, btnCancel.Left - btnOk.Width - 10), top);
                        btnConfirm.Location = new Point(Math.Max(0, btnOk.Left - btnConfirm.Width - 10), top);
                    };

                    root.Controls.Add(tabs, 0, 0);
                    root.Controls.Add(buttonsPanel, 0, 1);
                    dialog.Controls.Add(root);
                    trace("layout-buttons-ready");

                    dialog.AcceptButton = btnConfirm;
                    dialog.CancelButton = btnCancel;

                    Action<TextBox> styleTextBox = delegate(TextBox box)
                    {
                        if (box == null)
                        {
                            return;
                        }
                        box.BackColor = inputBack;
                        box.ForeColor = inputFore;
                        box.BorderStyle = BorderStyle.FixedSingle;
                    };

                    styleTextBox(txtWindowsProject);
                    styleTextBox(txtWslProject);
                    styleTextBox(txtWslOpenclaw);
                    styleTextBox(txtWslData);
                    styleTextBox(txtStartScript);
                    styleTextBox(txtDashboardScript);
                    styleTextBox(txtWslNativeProject);
                    styleTextBox(txtWinDockerDir);
                    styleTextBox(txtWinDockerData);
                    styleTextBox(txtWinNativeProject);
                    styleTextBox(txtGateway);
                    styleTextBox(txtDockerCompose);
                    styleTextBox(txtGatewayService);
                    styleTextBox(txtWslNativeCommand);
                    styleTextBox(txtWinNativeCommand);
                    styleTextBox(txtWslInstallCommand);
                    styleTextBox(txtWinInstallCommand);
                    styleTextBox(txtHttpProxy);
                    styleTextBox(txtHttpsProxy);
                    styleTextBox(txtAllProxy);
                    styleTextBox(txtNoProxy);
                    styleTextBox(txtWslSudoPassword);

                    Action refreshProxyInputs = delegate
                    {
                        var selectedProxy = cboProxyMode.SelectedItem as EnumComboItem<ProxyMode>;
                        bool custom = selectedProxy != null && selectedProxy.Value == ProxyMode.Custom;
                        txtHttpProxy.Enabled = custom;
                        txtHttpsProxy.Enabled = custom;
                        txtAllProxy.Enabled = custom;
                        txtNoProxy.Enabled = custom;
                    };
                    cboProxyMode.SelectedIndexChanged += delegate { refreshProxyInputs(); };
                    refreshProxyInputs();

                    Action refreshWslSudoPasswordHint = delegate
                    {
                        if (chkClearWslSudoPassword.Checked)
                        {
                            lblWslSudoPasswordHint.Text = Tr("Will clear stored password after Apply.", "应用后将清除已保存密码。");
                            return;
                        }
                        if (!string.IsNullOrEmpty(txtWslSudoPassword.Text))
                        {
                            lblWslSudoPasswordHint.Text = Tr("New password entered (save on Apply).", "已输入新密码（应用后保存）。");
                            return;
                        }
                        if (!string.IsNullOrWhiteSpace(_wslSudoPasswordProtected))
                        {
                            lblWslSudoPasswordHint.Text = Tr("Stored: Yes (encrypted).", "已保存：是（加密）。");
                            return;
                        }
                        lblWslSudoPasswordHint.Text = Tr("Stored: No.", "已保存：否。");
                    };
                    txtWslSudoPassword.TextChanged += delegate
                    {
                        if (!string.IsNullOrEmpty(txtWslSudoPassword.Text))
                        {
                            chkClearWslSudoPassword.Checked = false;
                        }
                        refreshWslSudoPasswordHint();
                    };
                    chkClearWslSudoPassword.CheckedChanged += delegate
                    {
                        if (chkClearWslSudoPassword.Checked)
                        {
                            txtWslSudoPassword.Text = string.Empty;
                        }
                        refreshWslSudoPasswordHint();
                    };
                    refreshWslSudoPasswordHint();

                    Action refreshInputCueBanners = delegate
                    {
                        TrySetCueBanner(txtWindowsProject, Tr(@"e.g. E:\OpenClaw", @"例如：E:\OpenClaw"));
                        TrySetCueBanner(txtWslProject, Tr("e.g. /mnt/x/openclaw", "例如：/mnt/x/openclaw"));
                        TrySetCueBanner(txtWslOpenclaw, Tr("e.g. /mnt/x/openclaw/openclaw", "例如：/mnt/x/openclaw/openclaw"));
                        TrySetCueBanner(txtWslData, Tr("e.g. /mnt/x/openclaw/openclaw-data", "例如：/mnt/x/openclaw/openclaw-data"));
                        TrySetCueBanner(txtStartScript, Tr("e.g. /mnt/x/openclaw/openclaw-start-fast.sh", "例如：/mnt/x/openclaw/openclaw-start-fast.sh"));
                        TrySetCueBanner(txtDashboardScript, Tr("e.g. /mnt/x/openclaw/openclaw-open-dashboard-wsl.sh", "例如：/mnt/x/openclaw/openclaw-open-dashboard-wsl.sh"));
                        TrySetCueBanner(txtWslNativeProject, Tr("e.g. /mnt/x/openclaw/openclaw", "例如：/mnt/x/openclaw/openclaw"));
                        TrySetCueBanner(txtWinDockerDir, Tr(@"e.g. E:\OpenClaw\openclaw", @"例如：E:\OpenClaw\openclaw"));
                        TrySetCueBanner(txtWinDockerData, Tr(@"e.g. E:\OpenClaw\openclaw-data", @"例如：E:\OpenClaw\openclaw-data"));
                        TrySetCueBanner(txtWinNativeProject, Tr(@"e.g. E:\OpenClaw", @"例如：E:\OpenClaw"));

                        TrySetCueBanner(txtDockerCompose, Tr("e.g. docker compose", "例如：docker compose"));
                        TrySetCueBanner(txtGatewayService, Tr("e.g. openclaw-gateway", "例如：openclaw-gateway"));
                        TrySetCueBanner(txtGateway, Tr("e.g. http://127.0.0.1:18790/", "例如：http://127.0.0.1:18790/"));

                        TrySetCueBanner(txtWslNativeCommand, Tr("e.g. openclaw", "例如：openclaw"));
                        TrySetCueBanner(txtWinNativeCommand, Tr("e.g. openclaw", "例如：openclaw"));
                        TrySetCueBanner(txtWslInstallCommand, Tr("e.g. curl -fsSL https://openclaw.ai/install.sh | bash", "例如：curl -fsSL https://openclaw.ai/install.sh | bash"));
                        TrySetCueBanner(txtWinInstallCommand, Tr("e.g. iwr -useb https://openclaw.ai/install.ps1 | iex", "例如：iwr -useb https://openclaw.ai/install.ps1 | iex"));

                        TrySetCueBanner(txtHttpProxy, Tr("e.g. http://127.0.0.1:7890", "例如：http://127.0.0.1:7890"));
                        TrySetCueBanner(txtHttpsProxy, Tr("e.g. http://127.0.0.1:7890", "例如：http://127.0.0.1:7890"));
                        TrySetCueBanner(txtAllProxy, Tr("e.g. socks5h://127.0.0.1:7891", "例如：socks5h://127.0.0.1:7891"));
                        TrySetCueBanner(txtNoProxy, Tr("e.g. localhost,127.0.0.1,::1", "例如：localhost,127.0.0.1,::1"));
                        TrySetCueBanner(txtWslSudoPassword, Tr("your WSL sudo password", "你的 WSL sudo 密码"));
                    };

                    Action refreshManualModeOptions = delegate
                    {
                        DeploymentMode selectedMode = ForceConcreteMode(_manualDeploymentMode);
                        var selectedItem = cboManualMode.SelectedItem as EnumComboItem<DeploymentMode>;
                        if (selectedItem != null)
                        {
                            selectedMode = ForceConcreteMode(selectedItem.Value);
                        }

                        cboManualMode.BeginUpdate();
                        cboManualMode.Items.Clear();
                        cboManualMode.Items.Add(new EnumComboItem<DeploymentMode>(DeploymentMode.WslDocker, "WSL + Docker"));
                        cboManualMode.Items.Add(new EnumComboItem<DeploymentMode>(DeploymentMode.WslNative, Tr("WSL Native", "WSL 原生")));
                        cboManualMode.Items.Add(new EnumComboItem<DeploymentMode>(DeploymentMode.WinDocker, "Windows + Docker"));
                        cboManualMode.Items.Add(new EnumComboItem<DeploymentMode>(DeploymentMode.WinNative, Tr("Windows Native", "Windows 原生")));
                        int selectedIndex = 0;
                        for (int i = 0; i < cboManualMode.Items.Count; i++)
                        {
                            var item = cboManualMode.Items[i] as EnumComboItem<DeploymentMode>;
                            if (item != null && ForceConcreteMode(item.Value) == selectedMode)
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                        cboManualMode.SelectedIndex = selectedIndex;
                        cboManualMode.EndUpdate();
                    };

                    Action refreshProxyModeOptions = delegate
                    {
                        ProxyMode selectedMode = _proxyMode;
                        var selectedItem = cboProxyMode.SelectedItem as EnumComboItem<ProxyMode>;
                        if (selectedItem != null)
                        {
                            selectedMode = selectedItem.Value;
                        }

                        cboProxyMode.BeginUpdate();
                        cboProxyMode.Items.Clear();
                        cboProxyMode.Items.Add(new EnumComboItem<ProxyMode>(ProxyMode.System, Tr("System", "系统")));
                        cboProxyMode.Items.Add(new EnumComboItem<ProxyMode>(ProxyMode.WslClashAuto, Tr("WSL Clash Auto", "WSL Clash 自动")));
                        cboProxyMode.Items.Add(new EnumComboItem<ProxyMode>(ProxyMode.Custom, Tr("Custom", "自定义")));
                        cboProxyMode.Items.Add(new EnumComboItem<ProxyMode>(ProxyMode.Off, Tr("Off", "关闭")));
                        int selectedIndex = 0;
                        for (int i = 0; i < cboProxyMode.Items.Count; i++)
                        {
                            var item = cboProxyMode.Items[i] as EnumComboItem<ProxyMode>;
                            if (item != null && item.Value == selectedMode)
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                        cboProxyMode.SelectedIndex = selectedIndex;
                        cboProxyMode.EndUpdate();
                    };

                    Action refreshWslChromeBackendOptions = delegate
                    {
                        string selectedBackend = NormalizeWslChromeBackend(_wslChromeBackend);
                        var selectedItem = cboWslChromeBackend.SelectedItem as EnumComboItem<string>;
                        if (selectedItem != null && !string.IsNullOrWhiteSpace(selectedItem.Value))
                        {
                            selectedBackend = NormalizeWslChromeBackend(selectedItem.Value);
                        }

                        cboWslChromeBackend.BeginUpdate();
                        cboWslChromeBackend.Items.Clear();
                        cboWslChromeBackend.Items.Add(new EnumComboItem<string>("wayland", "Wayland"));
                        cboWslChromeBackend.Items.Add(new EnumComboItem<string>("x11", "X11"));
                        cboWslChromeBackend.Items.Add(new EnumComboItem<string>("auto", Tr("Auto", "自动")));
                        int selectedIndex = 0;
                        for (int i = 0; i < cboWslChromeBackend.Items.Count; i++)
                        {
                            var item = cboWslChromeBackend.Items[i] as EnumComboItem<string>;
                            if (item != null && string.Equals(item.Value, selectedBackend, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                        cboWslChromeBackend.SelectedIndex = selectedIndex;
                        cboWslChromeBackend.EndUpdate();
                    };

                    Action refreshSettingsDialogTexts = delegate
                    {
                        dialog.Text = Tr("Settings", "设置");
                        generalTab.Text = Tr("GENERAL", "常规");
                        modeTab.Text = Tr("MODE", "模式");
                        pathsTab.Text = Tr("PATHS", "路径");
                        dockerTab.Text = Tr("DOCKER", "Docker");
                        nativeTab.Text = Tr("NATIVE", "原生");
                        networkTab.Text = Tr("NETWORK", "网络");
                        setupTab.Text = Tr("SETUP", "初始化");

                        languageGroup.Text = Tr("Language", "语言");
                        rbEnglish.Text = "English";
                        rbChinese.Text = "简体中文";
                        rbChineseTraditional.Text = "繁體中文";
                        themeGroup.Text = Tr("Theme", "主题");
                        rbLight.Text = Tr("Light", "浅色");
                        rbDark.Text = Tr("Dark", "深色");
                        modeHintGroup.Text = Tr("Current Mode", "当前模式");
                        modeHintLabel.Text =
                            Tr("Effective: ", "生效模式：") +
                            Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)) + Environment.NewLine +
                            Tr("Last detected: ", "最近检测：") +
                            Tr(ModeLabelEnglish(_lastDetectedMode), ModeLabelChinese(_lastDetectedMode));

                        chkAutoDetectMode.Text = Tr("Auto detect deployment mode", "自动检测部署模式");
                        lblManualMode.Text = Tr("Manual Mode", "手动模式");
                        modeHint.Text = Tr(
                            "Auto mode priority: WSL+Docker > WSL Native > Windows+Docker > Windows Native.",
                            "自动模式优先级：WSL+Docker > WSL 原生 > Windows+Docker > Windows 原生。");

                        pathsGroup.Text = Tr("PATHS", "路径");
                        lblWindowsProject.Text = Tr("Windows Project Dir", "Windows 项目目录");
                        lblWslProject.Text = Tr("WSL Project Dir", "WSL 项目目录");
                        lblWslOpenclaw.Text = Tr("WSL OpenClaw Dir", "WSL OpenClaw 目录");
                        lblWslData.Text = Tr("WSL Data Dir", "WSL 数据目录");
                        lblStartScript.Text = Tr("Start Script (WSL)", "启动脚本 (WSL)");
                        lblDashboardScript.Text = Tr("Dashboard Script (WSL)", "Dashboard 脚本 (WSL)");
                        lblWslNativeProject.Text = Tr("WSL Native Project", "WSL 原生项目目录");
                        lblWinDockerDir.Text = Tr("Win Docker OpenClaw", "Win Docker OpenClaw");
                        lblWinDockerData.Text = Tr("Win Docker Data Dir", "Win Docker 数据目录");
                        lblWinNativeProject.Text = Tr("Win Native Project", "Win 原生项目目录");
                        lblPathsHint.Text = Tr(
                            "Tip: WSL relative paths are resolved from WSL Project Dir.",
                            "提示：WSL 相对路径将按 WSL 项目目录解析。");

                        dockerGroup.Text = Tr("DOCKER", "Docker");
                        lblDockerCompose.Text = Tr("Docker Compose Cmd", "Docker Compose 命令");
                        lblGatewayService.Text = Tr("Gateway Service Name", "网关服务名");
                        lblGateway.Text = Tr("Gateway Root URL", "网关根地址");

                        browserGroup.Text = Tr("DASHBOARD", "Dashboard");
                        lblBrowserTarget.Text = Tr("Browser Target", "浏览器目标");
                        lblWslChromeBackend.Text = Tr("WSL Chrome Backend", "WSL Chrome 后端");

                        nativeGroup.Text = Tr("NATIVE COMMANDS", "原生命令");
                        lblWslNativeCommand.Text = Tr("WSL Native openclaw cmd", "WSL 原生 openclaw 命令");
                        lblWinNativeCommand.Text = Tr("Win Native openclaw cmd", "Win 原生 openclaw 命令");
                        lblWslInstallCommand.Text = Tr("WSL install command", "WSL 安装命令");
                        lblWinInstallCommand.Text = Tr("Win install command", "Win 安装命令");

                        networkGroup.Text = Tr("PROXY", "代理");
                        lblProxyMode.Text = Tr("Proxy Mode", "代理模式");
                        lblWslSudoPassword.Text = Tr("WSL sudo Password", "WSL sudo 密码");
                        chkClearWslSudoPassword.Text = Tr("Clear stored password", "清除已保存密码");

                        setupHint.Text =
                            Tr("Use this action to run install/init flow for current mode.", "用于执行当前模式的安装/初始化流程。") +
                            Environment.NewLine + Environment.NewLine +
                            Tr("When mode is Auto, it will use the detected mode.", "若模式为自动，将按检测结果执行。") +
                            Environment.NewLine +
                            Tr("Recommended after changing paths or mode.", "建议在变更路径或模式后执行。");
                        btnRunSetup.Text = Tr("Run Install / Init Now", "立即执行安装 / 初始化");
                        btnConfirm.Text = Tr("Done", "确定");
                        btnOk.Text = Tr("Apply", "应用");
                        btnCancel.Text = Tr("Cancel", "取消");

                        refreshManualModeOptions();
                        refreshProxyModeOptions();
                        refreshWslChromeBackendOptions();
                        refreshProxyInputs();
                        refreshWslSudoPasswordHint();
                        refreshInputCueBanners();
                        tabs.Invalidate();
                    };

                    Action applySettings = delegate
                    {
                        UiLanguage oldLanguage = _language;
                        UiTheme oldTheme = _theme;
                        DeploymentMode oldManualMode = _manualDeploymentMode;
                        DeploymentMode oldEffectiveMode = _effectiveMode;
                        bool oldAutoDetect = _autoDetectMode;
                        ProxyMode oldProxyMode = _proxyMode;
                        string oldCustomHttpProxy = _customHttpProxy;
                        string oldCustomHttpsProxy = _customHttpsProxy;
                        string oldCustomAllProxy = _customAllProxy;
                        string oldCustomNoProxy = _customNoProxy;
                        string oldWslSudoPasswordProtected = _wslSudoPasswordProtected;
                        string oldWslChromeBackend = _wslChromeBackend;

                        UiLanguage newLanguage = UiLanguage.English;
                        if (rbChineseTraditional.Checked)
                        {
                            newLanguage = UiLanguage.ChineseTraditional;
                        }
                        else if (rbChinese.Checked)
                        {
                            newLanguage = UiLanguage.ChineseSimplified;
                        }

                        UiTheme newTheme = rbDark.Checked ? UiTheme.Dark : UiTheme.Light;
                        var selectedManualModeItem = cboManualMode.SelectedItem as EnumComboItem<DeploymentMode>;
                        var selectedProxyModeItem = cboProxyMode.SelectedItem as EnumComboItem<ProxyMode>;
                        bool newAutoDetect = chkAutoDetectMode.Checked;
                        DeploymentMode newManualMode = selectedManualModeItem != null ? selectedManualModeItem.Value : DeploymentMode.WslDocker;
                        ProxyMode newProxyMode = selectedProxyModeItem != null ? selectedProxyModeItem.Value : ProxyMode.System;

                        bool changedLanguage = newLanguage != oldLanguage;
                        bool changedTheme = newTheme != oldTheme;
                        string newWindowsProject = NormalizeWindowsPath(txtWindowsProject.Text);
                        string newWslProject = NormalizeWslPath(txtWslProject.Text);
                        string newWslOpenclaw = ResolveWslPath(txtWslOpenclaw.Text, newWslProject + "/openclaw", newWslProject);
                        string newWslData = ResolveWslPath(txtWslData.Text, newWslProject + "/openclaw-data", newWslProject);
                        string newStartScript = ResolveWslPath(txtStartScript.Text, newWslProject + "/openclaw-start-fast.sh", newWslProject);
                        string newDashboardScript = ResolveWslPath(txtDashboardScript.Text, newWslProject + "/openclaw-open-dashboard-wsl.sh", newWslProject);
                        string newWslNativeProject = ResolveWslPath(txtWslNativeProject.Text, newWslProject + "/openclaw", newWslProject);
                        string newWinDockerDir = NormalizeWindowsPath(txtWinDockerDir.Text);
                        string newWinDockerDataDir = NormalizeWindowsPath(txtWinDockerData.Text);
                        string newWinNativeProject = NormalizeWindowsPath(txtWinNativeProject.Text);
                        string newGatewayRoot = NormalizeGatewayRootUrl(txtGateway.Text);
                        string newDockerCompose = NormalizeDockerComposeCommand(txtDockerCompose.Text);
                        string newGatewayService = NormalizeGatewayServiceName(txtGatewayService.Text);
                        string newWslNativeCommand = NormalizeCommandText(txtWslNativeCommand.Text, DefaultWslNativeOpenclawCommand);
                        string newWinNativeCommand = NormalizeCommandText(txtWinNativeCommand.Text, DefaultWinNativeOpenclawCommand);
                        string newWslInstallCommand = (txtWslInstallCommand.Text ?? string.Empty).Trim();
                        string newWinInstallCommand = (txtWinInstallCommand.Text ?? string.Empty).Trim();
                        string newBrowserTarget = (cboBrowserTarget.Text ?? string.Empty).Trim().ToLowerInvariant();
                        if (newBrowserTarget.Length == 0) newBrowserTarget = DefaultDashboardBrowserTarget;
                        var selectedWslChromeBackendItem = cboWslChromeBackend.SelectedItem as EnumComboItem<string>;
                        string newWslChromeBackend = NormalizeWslChromeBackend(
                            selectedWslChromeBackendItem != null
                                ? selectedWslChromeBackendItem.Value
                                : cboWslChromeBackend.Text);

                        string newCustomHttpProxy = (txtHttpProxy.Text ?? string.Empty).Trim();
                        string newCustomHttpsProxy = (txtHttpsProxy.Text ?? string.Empty).Trim();
                        string newCustomAllProxy = (txtAllProxy.Text ?? string.Empty).Trim();
                        string newCustomNoProxy = string.IsNullOrWhiteSpace(txtNoProxy.Text) ? "localhost,127.0.0.1,::1" : txtNoProxy.Text.Trim();
                        string newWslSudoPasswordRaw = txtWslSudoPassword.Text ?? string.Empty;
                        bool clearWslSudoPassword = chkClearWslSudoPassword.Checked;

                        if (ContainsLineBreak(newWslSudoPasswordRaw))
                        {
                            throw new InvalidOperationException(Tr("WSL sudo password cannot contain line breaks.", "WSL sudo 密码不能包含换行。"));
                        }

                        string newWslSudoPasswordProtected = oldWslSudoPasswordProtected;
                        bool changedWslSudoPassword = false;
                        bool changedWslSudoPasswordCleared = false;
                        if (clearWslSudoPassword)
                        {
                            if (!string.IsNullOrWhiteSpace(oldWslSudoPasswordProtected))
                            {
                                newWslSudoPasswordProtected = string.Empty;
                                changedWslSudoPassword = true;
                                changedWslSudoPasswordCleared = true;
                            }
                        }
                        else if (newWslSudoPasswordRaw.Length > 0)
                        {
                            string protectedPassword = ProtectSecretForCurrentUser(newWslSudoPasswordRaw);
                            if (string.IsNullOrWhiteSpace(protectedPassword))
                            {
                                throw new InvalidOperationException(Tr("Failed to encrypt WSL sudo password on this machine.", "无法在本机加密 WSL sudo 密码。"));
                            }
                            changedWslSudoPassword = !string.Equals(oldWslSudoPasswordProtected, protectedPassword, StringComparison.Ordinal);
                            newWslSudoPasswordProtected = protectedPassword;
                        }

                        bool changedPaths =
                            !string.Equals(_windowsProjectDir, newWindowsProject, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(_wslProjectDir, newWslProject, StringComparison.Ordinal) ||
                            !string.Equals(_wslOpenclawDir, newWslOpenclaw, StringComparison.Ordinal) ||
                            !string.Equals(_wslDataDir, newWslData, StringComparison.Ordinal) ||
                            !string.Equals(_wslStartScriptPath, newStartScript, StringComparison.Ordinal) ||
                            !string.Equals(_wslOpenDashboardScriptPath, newDashboardScript, StringComparison.Ordinal) ||
                            !string.Equals(_wslNativeProjectDir, newWslNativeProject, StringComparison.Ordinal) ||
                            !string.Equals(_winDockerOpenclawDir, newWinDockerDir, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(_winDockerDataDir, newWinDockerDataDir, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(_winNativeProjectDir, newWinNativeProject, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(NormalizeGatewayRootUrl(_gatewayRootUrl), newGatewayRoot, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(_dockerComposeCommand, newDockerCompose, StringComparison.Ordinal) ||
                            !string.Equals(_gatewayServiceName, newGatewayService, StringComparison.Ordinal) ||
                            !string.Equals(_wslNativeOpenclawCommand, newWslNativeCommand, StringComparison.Ordinal) ||
                            !string.Equals(_winNativeOpenclawCommand, newWinNativeCommand, StringComparison.Ordinal) ||
                            !string.Equals(_wslNativeInstallCommand, newWslInstallCommand, StringComparison.Ordinal) ||
                            !string.Equals(_winNativeInstallCommand, newWinInstallCommand, StringComparison.Ordinal) ||
                            !string.Equals(_dashboardBrowserTarget, newBrowserTarget, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(NormalizeWslChromeBackend(oldWslChromeBackend), newWslChromeBackend, StringComparison.OrdinalIgnoreCase);

                        bool changedMode = oldAutoDetect != newAutoDetect || ForceConcreteMode(oldManualMode) != ForceConcreteMode(newManualMode);
                        bool changedNetwork =
                            oldProxyMode != newProxyMode ||
                            !string.Equals(oldCustomHttpProxy, newCustomHttpProxy, StringComparison.Ordinal) ||
                            !string.Equals(oldCustomHttpsProxy, newCustomHttpsProxy, StringComparison.Ordinal) ||
                            !string.Equals(oldCustomAllProxy, newCustomAllProxy, StringComparison.Ordinal) ||
                            !string.Equals(oldCustomNoProxy, newCustomNoProxy, StringComparison.Ordinal);
                        bool changedSecurity = changedWslSudoPassword;

                        if (!changedLanguage && !changedTheme && !changedPaths && !changedMode && !changedNetwork && !changedSecurity)
                        {
                            AppendLog(Tr("No setting changes to apply.", "没有可应用的设置变更。"));
                            return;
                        }

                        _autoDetectMode = newAutoDetect;
                        _manualDeploymentMode = ForceConcreteMode(newManualMode);
                        _proxyMode = newProxyMode;
                        _customHttpProxy = newCustomHttpProxy;
                        _customHttpsProxy = newCustomHttpsProxy;
                        _customAllProxy = newCustomAllProxy;
                        _customNoProxy = newCustomNoProxy;
                        _wslSudoPasswordProtected = newWslSudoPasswordProtected;

                        if (changedPaths)
                        {
                            _windowsProjectDir = newWindowsProject;
                            _wslProjectDir = newWslProject;
                            _wslOpenclawDir = newWslOpenclaw;
                            _wslDataDir = newWslData;
                            _wslStartScriptPath = newStartScript;
                            _wslOpenDashboardScriptPath = newDashboardScript;
                            _wslNativeProjectDir = newWslNativeProject;
                            _winDockerOpenclawDir = newWinDockerDir;
                            _winDockerDataDir = newWinDockerDataDir;
                            _winNativeProjectDir = newWinNativeProject;
                            _gatewayRootUrl = newGatewayRoot;
                            _dockerComposeCommand = newDockerCompose;
                            _gatewayServiceName = newGatewayService;
                            _wslNativeOpenclawCommand = newWslNativeCommand;
                            _winNativeOpenclawCommand = newWinNativeCommand;
                            _wslNativeInstallCommand = newWslInstallCommand.Length == 0 ? _wslNativeInstallCommand : newWslInstallCommand;
                            _winNativeInstallCommand = newWinInstallCommand.Length == 0 ? _winNativeInstallCommand : newWinInstallCommand;
                            _dashboardBrowserTarget = newBrowserTarget;
                            _wslChromeBackend = newWslChromeBackend;
                            ApplyLanguageTexts();
                            AppendLog(Tr("Paths settings updated.", "路径设置已更新。"));
                        }

                        ResolveEffectiveMode(true, changedMode);
                        bool effectiveModeChanged = oldEffectiveMode != _effectiveMode;

                        if (changedLanguage)
                        {
                            SetLanguage(newLanguage, false, true);
                        }
                        if (changedTheme)
                        {
                            SetTheme(newTheme, false, true);
                        }
                        if (!changedLanguage && !changedTheme)
                        {
                            ApplyLanguageTexts();
                        }
                        if (changedNetwork)
                        {
                            AppendLog(Tr("Network proxy settings updated.", "网络代理设置已更新。"));
                        }
                        if (changedSecurity)
                        {
                            AppendLog(changedWslSudoPasswordCleared
                                ? Tr("Stored WSL sudo password cleared.", "已清除已保存的 WSL sudo 密码。")
                                : Tr("WSL sudo password saved (encrypted).", "WSL sudo 密码已加密保存。"));
                        }
                        txtWslSudoPassword.Text = string.Empty;
                        chkClearWslSudoPassword.Checked = false;
                        refreshWslSudoPasswordHint();
                        if (effectiveModeChanged)
                        {
                            AppendLog(
                                Tr("Effective mode: ", "生效模式：") +
                                Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)));
                        }
                        SavePreferences();
                        refreshSettingsDialogTexts();
                        AppendLog(Tr("Settings applied.", "设置已应用。"));
                    };

                    btnOk.Click += delegate
                    {
                        try
                        {
                            applySettings();
                        }
                        catch (Exception ex)
                        {
                            Program.TryWriteErrorLog("settings-apply-click", ex);
                            AppendLog(Tr("Apply failed: ", "应用失败：") + ex.Message);
                        }
                    };

                    btnConfirm.Click += delegate
                    {
                        try
                        {
                            applySettings();
                            dialog.Close();
                        }
                        catch (Exception ex)
                        {
                            Program.TryWriteErrorLog("settings-confirm-click", ex);
                            AppendLog(Tr("Confirm failed: ", "确定失败：") + ex.Message);
                        }
                    };

                    btnRunSetup.Click += delegate
                    {
                        bool applySucceeded = true;
                        try
                        {
                            applySettings();
                        }
                        catch (Exception ex)
                        {
                            applySucceeded = false;
                            Program.TryWriteErrorLog("settings-setup-apply", ex);
                            AppendLog(Tr("Apply before setup failed: ", "初始化前应用设置失败：") + ex.Message);
                        }
                        if (applySucceeded)
                        {
                            runSetupRequested = true;
                            dialog.Close();
                        }
                    };

                    try
                    {
                        SetWindowTheme(txtWindowsProject.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWslProject.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWslOpenclaw.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWslData.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtStartScript.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtDashboardScript.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWslNativeProject.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWinDockerDir.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWinDockerData.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWinNativeProject.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtGateway.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtDockerCompose.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtGatewayService.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWslNativeCommand.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWinNativeCommand.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWslInstallCommand.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtWinInstallCommand.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtHttpProxy.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtHttpsProxy.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtAllProxy.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                        SetWindowTheme(txtNoProxy.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                    }
                    catch
                    {
                    }

                    trace("controls-built bounds=" + FormatRect(dialog.Bounds));
                    PlaceDialogCenteredOnOwnerScreen(dialog, this, 12, "attempt=" + attemptId);

                    bool raised = false;
                    dialog.Shown += delegate
                    {
                        trace("shown bounds=" + FormatRect(dialog.Bounds));
                        EnsureDialogOnScreen(dialog, this, 12, "attempt=" + attemptId + " shown");
                        if (!raised)
                        {
                            raised = true;
                            try
                            {
                                dialog.BeginInvoke((MethodInvoker)delegate
                                {
                                    try
                                    {
                                        trace("shown-begininvoke bounds=" + FormatRect(dialog.Bounds));
                                        EnsureDialogOnScreen(dialog, this, 12, "attempt=" + attemptId + " shown-begininvoke");
                                        dialog.BringToFront();
                                        dialog.Activate();
                                        dialog.TopMost = true;
                                        dialog.TopMost = false;
                                    }
                                    catch (Exception ex)
                                    {
                                        Program.TryWriteErrorLog("settings-dialog-raise", ex);
                                    }
                                });
                            }
                            catch
                            {
                            }
                        }
                    };
                    dialog.Activated += delegate
                    {
                        trace("activated bounds=" + FormatRect(dialog.Bounds));
                    };
                    dialog.FormClosed += delegate(object sender, FormClosedEventArgs e)
                    {
                        trace("closed result=" + dialog.DialogResult);
                    };

                    trace("before-show bounds=" + FormatRect(dialog.Bounds));
                    dialog.ShowDialog(this);
                    trace("show dialog result=" + dialog.DialogResult);
                    if (runSetupRequested)
                    {
                        trace("setup-run-requested");
                        RunInstallSetup();
                    }
                    trace("applied");
                }
            }

            private void ApplyLanguageTexts()
            {
                Text = Tr("OpenClaw Windows Console", "OpenClaw Windows 控制台");
                _titleLabel.Text = Tr("OpenClaw Control Console", "OpenClaw 控制台");
                _subtitleLabel.Text =
                    Tr("Mode: ", "当前模式：") +
                    Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)) +
                    Tr("  | launcher, dashboard, and health monitor.", "  | 启动、面板与健康监控。");
                SetActionButtonText(_btnStart, Tr("▶ Start\nOpenClaw", "▶ 启动\nOpenClaw"));
                SetActionButtonText(_btnStop, Tr("■ Stop\nOpenClaw", "■ 停止\nOpenClaw"));
                SetActionButtonText(_btnOpenDashboard, Tr("⌘ Open\nDashboard", "⌘ 打开\nDashboard"));
                SetActionButtonText(_btnCheck, Tr("✓ Check\nStatus/Health", "✓ 检查\n状态/健康度"));
                _logTitleLabel.Text = Tr("ACTIVITY LOG", "运行日志");
                _modeTopLabel.Text = Tr("Mode: ", "模式：") + Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode));
                _footerLabel.Text =
                    Tr("Project: ", "项目路径：") + _windowsProjectDir + Environment.NewLine +
                    Tr("Gateway: ", "网关：") + NormalizeGatewayRootUrl(_gatewayRootUrl);

                BackfillStatusTitleFromCurrentBadge();
                RefreshLocalizedStatusTexts();
            }

            private static void SetActionButtonText(Button button, string text)
            {
                var visual = button.Tag as ActionButtonVisual;
                if (visual == null)
                {
                    visual = new ActionButtonVisual();
                    button.Tag = visual;
                }
                visual.Text = text ?? string.Empty;
                button.Invalidate();
            }

            private static string GetActionButtonText(Button button)
            {
                var visual = button.Tag as ActionButtonVisual;
                if (visual != null && !string.IsNullOrWhiteSpace(visual.Text))
                {
                    return visual.Text;
                }
                return button.Text ?? string.Empty;
            }

            private static void SetActionButtonFill(Button button, Color fillColor)
            {
                var visual = button.Tag as ActionButtonVisual;
                if (visual != null)
                {
                    visual.FillColor = fillColor;
                }
                button.Invalidate();
            }

            private void BackfillStatusTitleFromCurrentBadge()
            {
                string current = NormalizeStatusBadgeText(GetActionButtonText(_statusBadge));
                if (current.Length == 0)
                {
                    return;
                }

                if (string.Equals(current, _statusTitleEnglish, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current, _statusTitleChinese, StringComparison.Ordinal))
                {
                    return;
                }

                string english;
                string chinese;
                if (TryMapStatusTitlePair(current, out english, out chinese))
                {
                    _statusTitleEnglish = english;
                    _statusTitleChinese = chinese;
                    return;
                }

                // Preserve current badge text when mapping is unknown, so we never snap back to "Idle".
                _statusTitleEnglish = current;
                _statusTitleChinese = current;
            }

            private static string NormalizeStatusBadgeText(string value)
            {
                string s = (value ?? string.Empty).Trim();
                if (s.Length == 0)
                {
                    return s;
                }
                if (s.EndsWith("■", StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - 1).TrimEnd();
                }
                return s;
            }

            private static bool TryMapStatusTitlePair(string value, out string english, out string chinese)
            {
                english = string.Empty;
                chinese = string.Empty;
                string key = (value ?? string.Empty).Trim();
                if (key.Length == 0)
                {
                    return false;
                }

                if (IsStatusKey(key, "Idle", "待检查")) { english = "Idle"; chinese = "待检查"; return true; }
                if (IsStatusKey(key, "Operation Error", "操作错误")) { english = "Operation Error"; chinese = "操作错误"; return true; }
                if (IsStatusKey(key, "Start Timeout", "启动超时")) { english = "Start Timeout"; chinese = "启动超时"; return true; }
                if (IsStatusKey(key, "Docker Unreachable", "Docker 不可达")) { english = "Docker Unreachable"; chinese = "Docker 不可达"; return true; }
                if (IsStatusKey(key, "Start Failed", "启动失败")) { english = "Start Failed"; chinese = "启动失败"; return true; }
                if (IsStatusKey(key, "OpenClaw Running", "OpenClaw 运行中")) { english = "OpenClaw Running"; chinese = "OpenClaw 运行中"; return true; }
                if (IsStatusKey(key, "OpenClaw Stopped", "OpenClaw 已停止")) { english = "OpenClaw Stopped"; chinese = "OpenClaw 已停止"; return true; }
                if (IsStatusKey(key, "Stop Failed", "停止失败")) { english = "Stop Failed"; chinese = "停止失败"; return true; }
                if (IsStatusKey(key, "Dashboard Opened", "Dashboard 已打开")) { english = "Dashboard Opened"; chinese = "Dashboard 已打开"; return true; }
                if (IsStatusKey(key, "Dashboard Error", "Dashboard 错误")) { english = "Dashboard Error"; chinese = "Dashboard 错误"; return true; }
                if (IsStatusKey(key, "Health Check Failed", "健康检查失败")) { english = "Health Check Failed"; chinese = "健康检查失败"; return true; }
                if (IsStatusKey(key, "Setup Completed", "初始化完成")) { english = "Setup Completed"; chinese = "初始化完成"; return true; }
                if (IsStatusKey(key, "Setup Failed", "初始化失败")) { english = "Setup Failed"; chinese = "初始化失败"; return true; }
                if (IsStatusKey(key, "Healthy", "健康")) { english = "Healthy"; chinese = "健康"; return true; }
                if (IsStatusKey(key, "HTTP Issue", "HTTP 异常")) { english = "HTTP Issue"; chinese = "HTTP 异常"; return true; }
                if (IsStatusKey(key, "Docker Down", "Docker 未运行")) { english = "Docker Down"; chinese = "Docker 未运行"; return true; }
                if (IsStatusKey(key, "Not Running", "未运行")) { english = "Not Running"; chinese = "未运行"; return true; }
                // Backward-compatible titles from previous panel versions.
                if (IsStatusKey(key, "Not checked", "未检查")) { english = "Idle"; chinese = "待检查"; return true; }
                if (IsStatusKey(key, "Started", "已启动")) { english = "OpenClaw Running"; chinese = "OpenClaw 运行中"; return true; }
                if (IsStatusKey(key, "Stopped", "已停止")) { english = "OpenClaw Stopped"; chinese = "OpenClaw 已停止"; return true; }
                if (IsStatusKey(key, "Running / Healthy", "运行中 / 健康")) { english = "Healthy"; chinese = "健康"; return true; }
                if (IsStatusKey(key, "Running / HTTP Issue", "运行中 / HTTP 异常")) { english = "HTTP Issue"; chinese = "HTTP 异常"; return true; }
                if (IsStatusKey(key, "Not Started", "未启动")) { english = "Not Running"; chinese = "未运行"; return true; }
                return false;
            }

            private static bool IsStatusKey(string input, string english, string chinese)
            {
                return string.Equals(input, english, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(input, chinese, StringComparison.Ordinal) ||
                       string.Equals(input, ConvertToTraditionalChinese(chinese), StringComparison.Ordinal);
            }

            private void ApplyThemeVisuals()
            {
                bool dark = _theme == UiTheme.Dark;
                if (dark)
                {
                    BackColor = Color.FromArgb(15, 22, 32);
                    _cardBackgroundColor = Color.FromArgb(35, 40, 49);
                    _cardBorderColor = Color.FromArgb(63, 71, 87);
                    _headerGradientStartColor = Color.FromArgb(8, 42, 76);
                    _headerGradientEndColor = Color.FromArgb(17, 100, 115);
                    _titleLabel.ForeColor = Color.FromArgb(244, 247, 251);
                    _subtitleLabel.ForeColor = Color.FromArgb(224, 229, 237);
                    _footerLabel.ForeColor = Color.FromArgb(228, 232, 238);
                    _modeTopLabel.ForeColor = Color.FromArgb(214, 224, 242);
                    _statusHint.ForeColor = Color.FromArgb(201, 212, 232);
                    _logTitleLabel.ForeColor = Color.FromArgb(235, 240, 247);
                    _logBox.BackColor = Color.FromArgb(6, 8, 12);
                    _logBox.ForeColor = Color.FromArgb(225, 230, 237);
                    _logTimestampColor = Color.FromArgb(240, 219, 108);
                    _logTextColor = Color.FromArgb(224, 228, 235);
                    _logSuccessColor = Color.FromArgb(135, 223, 146);
                    _logErrorColor = Color.FromArgb(247, 134, 134);
                    _logMutedColor = Color.FromArgb(176, 184, 197);
                    SetActionButtonFill(_btnSettings, Color.FromArgb(47, 56, 75));
                    _btnSettings.ForeColor = Color.FromArgb(228, 236, 248);
                    _heroCardPanel.BackColor = Color.FromArgb(46, 51, 62);
                }
                else
                {
                    BackColor = Color.FromArgb(218, 222, 230);
                    _cardBackgroundColor = Color.White;
                    _cardBorderColor = Color.FromArgb(198, 206, 220);
                    _headerGradientStartColor = Color.FromArgb(24, 52, 122);
                    _headerGradientEndColor = Color.FromArgb(14, 120, 155);
                    _titleLabel.ForeColor = Color.FromArgb(6, 8, 14);
                    _subtitleLabel.ForeColor = Color.FromArgb(23, 30, 39);
                    _footerLabel.ForeColor = Color.FromArgb(10, 12, 16);
                    _modeTopLabel.ForeColor = Color.FromArgb(45, 58, 84);
                    _statusHint.ForeColor = Color.FromArgb(50, 62, 82);
                    _logTitleLabel.ForeColor = Color.FromArgb(239, 242, 247);
                    _logBox.BackColor = Color.FromArgb(121, 126, 134);
                    _logBox.ForeColor = Color.FromArgb(241, 244, 248);
                    _logTimestampColor = Color.FromArgb(240, 219, 108);
                    _logTextColor = Color.FromArgb(241, 244, 248);
                    _logSuccessColor = Color.FromArgb(139, 229, 149);
                    _logErrorColor = Color.FromArgb(247, 139, 139);
                    _logMutedColor = Color.FromArgb(224, 229, 236);
                    SetActionButtonFill(_btnSettings, Color.FromArgb(245, 249, 255));
                    _btnSettings.ForeColor = Color.FromArgb(40, 52, 76);
                    _heroCardPanel.BackColor = Color.FromArgb(241, 244, 248);
                }

                _actionsPanel.BackColor = Color.Transparent;
                _logCardPanel.BackColor = Color.Transparent;

                ApplyActionButtonTheme(dark);
                RefreshStatusBadgeColors();

                _headerPanel.Invalidate();
                _actionsPanel.Invalidate();
                _logCardPanel.Invalidate();
                _btnSettings.Invalidate();
                Invalidate();
                ApplyNativeTitleBarTheme();
            }

            private void RefreshLocalizedStatusTexts()
            {
                string title = Tr(_statusTitleEnglish, _statusTitleChinese);
                if (_language == UiLanguage.English)
                {
                    title = title.ToUpperInvariant();
                }
                SetActionButtonText(_statusBadge, title);
                if (!_busy)
                {
                    _statusHint.Text = Tr(_statusDetailEnglish, _statusDetailChinese);
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                UpdateWindowRoundRegion();
                ApplyNativeTitleBarTheme();
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    if (!SystemInformation.TerminalServerSession)
                    {
                        cp.ExStyle |= WsExComposited;
                    }
                    return cp;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmNCHitTest && FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Normal)
                {
                    base.WndProc(ref m);
                    if ((int)m.Result == HtClient)
                    {
                        int lParam = m.LParam.ToInt32();
                        int x = (short)(lParam & 0xFFFF);
                        int y = (short)((lParam >> 16) & 0xFFFF);
                        Point clientPos = PointToClient(new Point(x, y));
                        bool onLeft = clientPos.X >= 0 && clientPos.X <= ResizeBorder;
                        bool onRight = clientPos.X <= ClientSize.Width && clientPos.X >= ClientSize.Width - ResizeBorder;
                        bool onTop = clientPos.Y >= 0 && clientPos.Y <= ResizeBorder;
                        bool onBottom = clientPos.Y <= ClientSize.Height && clientPos.Y >= ClientSize.Height - ResizeBorder;

                        if (onLeft && onTop) m.Result = (IntPtr)HtTopLeft;
                        else if (onRight && onTop) m.Result = (IntPtr)HtTopRight;
                        else if (onLeft && onBottom) m.Result = (IntPtr)HtBottomLeft;
                        else if (onRight && onBottom) m.Result = (IntPtr)HtBottomRight;
                        else if (onLeft) m.Result = (IntPtr)HtLeft;
                        else if (onRight) m.Result = (IntPtr)HtRight;
                        else if (onTop) m.Result = (IntPtr)HtTop;
                        else if (onBottom) m.Result = (IntPtr)HtBottom;
                    }
                    return;
                }

                base.WndProc(ref m);
            }

            private void ApplyNativeTitleBarTheme()
            {
                if (!IsHandleCreated || Environment.OSVersion.Version.Major < 10 || FormBorderStyle == FormBorderStyle.None)
                {
                    return;
                }

                try
                {
                    bool dark = _theme == UiTheme.Dark;
                    TryApplyUxThemeDarkMode(dark);

                    int useDark = _theme == UiTheme.Dark ? 1 : 0;
                    int size = Marshal.SizeOf(typeof(int));
                    DwmSetWindowAttribute(Handle, DwmaUseImmersiveDarkMode, ref useDark, size);
                    DwmSetWindowAttribute(Handle, DwmaUseImmersiveDarkModeBefore20H1, ref useDark, size);

                    int captionColor = _theme == UiTheme.Dark
                        ? ColorTranslator.ToWin32(Color.FromArgb(28, 34, 46))
                        : ColorTranslator.ToWin32(Color.FromArgb(242, 245, 250));
                    int textColor = _theme == UiTheme.Dark
                        ? ColorTranslator.ToWin32(Color.FromArgb(230, 236, 248))
                        : ColorTranslator.ToWin32(Color.FromArgb(43, 54, 73));
                    int borderColor = _theme == UiTheme.Dark
                        ? ColorTranslator.ToWin32(Color.FromArgb(58, 70, 92))
                        : ColorTranslator.ToWin32(Color.FromArgb(206, 214, 228));

                    DwmSetWindowAttribute(Handle, DwmaCaptionColor, ref captionColor, size);
                    DwmSetWindowAttribute(Handle, DwmaTextColor, ref textColor, size);
                    DwmSetWindowAttribute(Handle, DwmaBorderColor, ref borderColor, size);
                    SendMessage(Handle, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
                    SetWindowPos(
                        Handle,
                        IntPtr.Zero,
                        0,
                        0,
                        0,
                        0,
                        SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                }
                catch
                {
                }
            }

            private void TryApplyUxThemeDarkMode(bool dark)
            {
                try
                {
                    SetPreferredAppMode(dark ? PreferredAppMode.AllowDark : PreferredAppMode.Default);
                }
                catch
                {
                }

                try
                {
                    AllowDarkModeForWindow(Handle, dark);
                }
                catch
                {
                }

                try
                {
                    SetWindowTheme(Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                }
                catch
                {
                }
            }

            private void ApplyActionButtonTheme(bool dark)
            {
                ApplyActionButtonStyle(_btnStart, dark ? Color.FromArgb(157, 225, 177) : Color.FromArgb(150, 220, 170));
                ApplyActionButtonStyle(_btnStop, dark ? Color.FromArgb(244, 159, 161) : Color.FromArgb(243, 154, 156));
                ApplyActionButtonStyle(_btnOpenDashboard, dark ? Color.FromArgb(139, 193, 248) : Color.FromArgb(135, 191, 247));
                ApplyActionButtonStyle(_btnCheck, dark ? Color.FromArgb(240, 217, 136) : Color.FromArgb(246, 224, 145));
                _btnStart.ForeColor = Color.FromArgb(24, 36, 36);
                _btnStop.ForeColor = Color.FromArgb(34, 24, 24);
                _btnOpenDashboard.ForeColor = Color.FromArgb(21, 34, 55);
                _btnCheck.ForeColor = Color.FromArgb(64, 50, 21);
            }

            private static Button CreateWindowControlButton(string text)
            {
                var btn = new Button
                {
                    AutoSize = false,
                    Size = new Size(42, 30),
                    FlatStyle = FlatStyle.Flat,
                    Text = text,
                    Font = new Font("Segoe UI Symbol", 11.5F, FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleCenter,
                    UseVisualStyleBackColor = false,
                    TabStop = false,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0)
                };
                btn.FlatAppearance.BorderSize = 0;
                return btn;
            }

            private static void ApplyWindowControlButtonTheme(
                Button button,
                Color backColor,
                Color foreColor,
                Color hoverColor,
                Color downColor)
            {
                button.BackColor = backColor;
                button.ForeColor = foreColor;
                button.FlatAppearance.MouseOverBackColor = hoverColor;
                button.FlatAppearance.MouseDownBackColor = downColor;
            }

            private Panel BuildWindowTitleBar(
                out Label titleLabel,
                out Button minButton,
                out Button maxButton,
                out Button closeButton)
            {
                var panel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 10),
                    Padding = new Padding(12, 0, 0, 0),
                    BackColor = Color.FromArgb(248, 250, 255)
                };
                panel.Paint += delegate(object sender, PaintEventArgs e)
                {
                    var r = panel.ClientRectangle;
                    using (var pen = new Pen(_cardBorderColor))
                    {
                        e.Graphics.DrawLine(pen, 0, r.Height - 1, r.Width, r.Height - 1);
                    }
                };

                titleLabel = new Label
                {
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Text = "OpenClaw Windows Console",
                    ForeColor = Color.FromArgb(50, 62, 84),
                    Font = new Font("Segoe UI Semibold", 10.2F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 0, 0)
                };

                var right = new Panel
                {
                    Dock = DockStyle.Right,
                    Width = 126,
                    BackColor = Color.Transparent
                };

                var btnMin = CreateWindowControlButton("—");
                var btnMax = CreateWindowControlButton("□");
                var btnClose = CreateWindowControlButton("✕");

                btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
                btnMax.Click += delegate { ToggleWindowState(); };
                btnClose.Click += delegate { Close(); };

                right.Controls.Add(btnMin);
                right.Controls.Add(btnMax);
                right.Controls.Add(btnClose);

                right.Resize += delegate
                {
                    int top = Math.Max(0, (right.ClientSize.Height - btnClose.Height) / 2);
                    btnClose.Location = new Point(Math.Max(0, right.ClientSize.Width - btnClose.Width), top);
                    btnMax.Location = new Point(Math.Max(0, btnClose.Left - btnMax.Width), top);
                    btnMin.Location = new Point(Math.Max(0, btnMax.Left - btnMin.Width), top);
                };
                minButton = btnMin;
                maxButton = btnMax;
                closeButton = btnClose;

                panel.Controls.Add(titleLabel);
                panel.Controls.Add(right);

                panel.MouseDown += WindowTitleBarMouseDown;
                titleLabel.MouseDown += WindowTitleBarMouseDown;
                panel.DoubleClick += delegate { ToggleWindowState(); };
                titleLabel.DoubleClick += delegate { ToggleWindowState(); };

                return panel;
            }

            private void WindowTitleBarMouseDown(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized)
                {
                    return;
                }
                ReleaseCapture();
                SendMessage(Handle, WmNCLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
            }

            private void ToggleWindowState()
            {
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
            }

            private void MainFormPaint(object sender, PaintEventArgs e)
            {
                var r = ClientRectangle;
                if (r.Width <= 0 || r.Height <= 0)
                {
                    return;
                }
                using (var brush = new LinearGradientBrush(
                    r,
                    _theme == UiTheme.Dark ? Color.FromArgb(12, 18, 29) : Color.FromArgb(233, 235, 239),
                    _theme == UiTheme.Dark ? Color.FromArgb(26, 35, 47) : Color.FromArgb(206, 211, 222),
                    18F))
                {
                    e.Graphics.FillRectangle(brush, r);
                }
            }

            private void UpdateWindowRoundRegion()
            {
                if (FormBorderStyle != FormBorderStyle.None)
                {
                    Region = null;
                    return;
                }
                if (WindowState == FormWindowState.Maximized)
                {
                    Region = null;
                    return;
                }
                var rect = new Rectangle(0, 0, Width, Height);
                using (var path = CreateRoundedPath(rect, _windowCornerRadius))
                {
                    Region = new Region(path);
                }
            }

            private static void AttachRoundedCorners(Control control, int radius)
            {
                Action apply = delegate
                {
                    if (control.Width <= 0 || control.Height <= 0)
                    {
                        return;
                    }
                    var rect = new Rectangle(0, 0, control.Width, control.Height);
                    using (var path = CreateRoundedPath(rect, radius))
                    {
                        control.Region = new Region(path);
                    }
                };
                control.Resize += delegate { apply(); };
                apply();
            }

            private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
            {
                var path = new GraphicsPath();
                int d = Math.Max(2, radius * 2);
                var r = new Rectangle(bounds.X, bounds.Y, Math.Max(1, bounds.Width - 1), Math.Max(1, bounds.Height - 1));
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }

            private static void DrawRoundedBorder(Graphics g, Rectangle bounds, int radius, Color borderColor)
            {
                using (var path = CreateRoundedPath(bounds, radius))
                using (var pen = new Pen(borderColor))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawPath(pen, path);
                }
            }

            private Panel BuildHeader(out Label title, out Label subtitle, out Label modeLabel, out Button settingsButton, out Panel heroCardPanel)
            {
                var panel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 10),
                    BackColor = Color.Transparent
                };

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    BackColor = Color.Transparent
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                var topRow = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent
                };

                var badge = new Button
                {
                    AutoSize = false,
                    Size = new Size(190, 34),
                    FlatStyle = FlatStyle.Flat,
                    Text = string.Empty,
                    Font = new Font("Segoe UI", 10.3F, FontStyle.Regular),
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(56, 70, 94),
                    Cursor = Cursors.Default,
                    TabStop = false
                };
                badge.FlatAppearance.BorderSize = 0;
                badge.FlatAppearance.MouseOverBackColor = Color.Transparent;
                badge.FlatAppearance.MouseDownBackColor = Color.Transparent;
                badge.Tag = new ActionButtonVisual
                {
                    Text = "IDLE",
                    FillColor = Color.FromArgb(236, 240, 246),
                    ParseLeadingIcon = false,
                    EnableHoverEffects = false,
                    DrawShadow = false,
                    CornerRadius = 16,
                    IconSizeDelta = 0F,
                    TextStyle = FontStyle.Regular,
                    UseCenterTextLayout = true
                };
                badge.Paint += ActionButtonPaint;

                var gearButton = new Button
                {
                    AutoSize = false,
                    Size = new Size(34, 34),
                    FlatStyle = FlatStyle.Flat,
                    Text = string.Empty,
                    Font = new Font("Segoe UI Symbol", 13.6F, FontStyle.Regular),
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(56, 70, 94),
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                gearButton.FlatAppearance.BorderSize = 0;
                gearButton.FlatAppearance.MouseOverBackColor = Color.Transparent;
                gearButton.FlatAppearance.MouseDownBackColor = Color.Transparent;
                gearButton.Tag = new ActionButtonVisual
                {
                    Text = "\u2699",
                    FillColor = Color.FromArgb(236, 240, 246),
                    ParseLeadingIcon = false,
                    EnableHoverEffects = true,
                    DrawShadow = false,
                    CornerRadius = 11,
                    IconSizeDelta = 0.8F,
                    TextStyle = FontStyle.Regular,
                    UseCenterTextLayout = true,
                    CenterOffsetX = -0.55F,
                    CenterOffsetY = -0.9F
                };
                gearButton.Paint += ActionButtonPaint;

                var modeTextLabel = new Label
                {
                    AutoSize = false,
                    Size = new Size(240, 34),
                    Text = Tr("Mode: ", "模式：") + Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)),
                    Font = new Font("Segoe UI Semibold", 9.2F, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(56, 70, 94),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 0, 0),
                    AutoEllipsis = true
                };

                topRow.Controls.Add(modeTextLabel);
                topRow.Controls.Add(badge);
                topRow.Controls.Add(gearButton);
                topRow.Resize += delegate
                {
                    gearButton.Left = Math.Max(0, topRow.ClientSize.Width - gearButton.Width);
                    gearButton.Top = 2;
                    badge.Left = Math.Max(0, gearButton.Left - badge.Width - 8);
                    badge.Top = 2;
                    modeTextLabel.Left = 2;
                    modeTextLabel.Top = 2;
                    int available = Math.Max(80, badge.Left - modeTextLabel.Left - 10);
                    modeTextLabel.Width = Math.Min(440, available);
                };

                var heroCard = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 8, 0, 0),
                    BackColor = Color.FromArgb(236, 238, 242)
                };
                AttachRoundedCorners(heroCard, 18);
                heroCard.Paint += delegate(object sender, PaintEventArgs e)
                {
                    var r = heroCard.ClientRectangle;
                    Color fillStart;
                    Color fillEnd;
                    Color border;
                    if (_theme == UiTheme.Dark)
                    {
                        fillStart = Color.FromArgb(44, 49, 60);
                        fillEnd = Color.FromArgb(52, 57, 69);
                        border = Color.FromArgb(66, 73, 88);
                    }
                    else
                    {
                        fillStart = Color.FromArgb(247, 249, 252);
                        fillEnd = Color.FromArgb(236, 241, 247);
                        border = Color.FromArgb(196, 204, 219);
                    }
                    using (var brush = new LinearGradientBrush(
                        r,
                        fillStart,
                        fillEnd,
                        22F))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var path = CreateRoundedPath(r, 18))
                        {
                            e.Graphics.FillPath(brush, path);
                        }
                    }
                    DrawRoundedBorder(e.Graphics, r, 18, border);
                };

                var heroTextLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    BackColor = Color.Transparent,
                    Padding = new Padding(10, 8, 10, 8)
                };
                heroTextLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                heroTextLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 64F));
                heroTextLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 36F));

                var heroTitle = new Label
                {
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Text = "OpenClaw Control Console",
                    ForeColor = Color.Black,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI Semibold", 26F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                var heroSubtitle = new Label
                {
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Text = "Mode-aware launcher, dashboard, and health monitor.",
                    ForeColor = Color.FromArgb(41, 46, 57),
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 13F, FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                heroTextLayout.Controls.Add(heroTitle, 0, 0);
                heroTextLayout.Controls.Add(heroSubtitle, 0, 1);
                heroCard.Controls.Add(heroTextLayout);
                title = heroTitle;
                subtitle = heroSubtitle;

                layout.Controls.Add(topRow, 0, 0);
                layout.Controls.Add(heroCard, 0, 1);
                panel.Controls.Add(layout);
                panel.Tag = badge;
                modeLabel = modeTextLabel;
                settingsButton = gearButton;
                heroCardPanel = heroCard;
                return panel;
            }

            private Panel BuildActions(
                out Button btnStart,
                out Button btnStop,
                out Button btnOpen,
                out Button btnCheck,
                out ProgressBar progressBar)
            {
                var panel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent
                };
                panel.Margin = new Padding(0, 0, 0, 10);
                panel.Padding = new Padding(0, 0, 0, 0);

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor = Color.Transparent
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                var buttons = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 4,
                    RowCount = 1,
                    BackColor = Color.Transparent
                };
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

                btnStart = CreateActionButton("▶ Start\nOpenClaw", Color.FromArgb(157, 225, 177));
                btnStop = CreateActionButton("■ Stop\nOpenClaw", Color.FromArgb(244, 159, 161));
                btnOpen = CreateActionButton("⌘ Open\nDashboard", Color.FromArgb(139, 193, 248));
                btnCheck = CreateActionButton("✓ Check\nStatus/Health", Color.FromArgb(246, 224, 145));

                btnStart.Click += delegate { StartOpenClaw(); };
                btnStop.Click += delegate { StopOpenClaw(); };
                btnOpen.Click += delegate { OpenDashboard(); };
                btnCheck.Click += delegate { CheckHealth(); };

                buttons.Controls.Add(btnStart, 0, 0);
                buttons.Controls.Add(btnStop, 1, 0);
                buttons.Controls.Add(btnOpen, 2, 0);
                buttons.Controls.Add(btnCheck, 3, 0);

                for (int i = 0; i < buttons.Controls.Count; i++)
                {
                    buttons.Controls[i].Margin = new Padding(4, 4, 4, 4);
                }

                progressBar = new ProgressBar
                {
                    Dock = DockStyle.Fill,
                    Style = ProgressBarStyle.Continuous,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0,
                    MarqueeAnimationSpeed = 0
                };

                var hint = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(84, 99, 124),
                    Font = new Font("Segoe UI", 9.2F, FontStyle.Regular),
                    Text = "Ready"
                };

                layout.Controls.Add(buttons, 0, 0);
                layout.Controls.Add(progressBar, 0, 1);
                layout.Controls.Add(hint, 0, 2);

                panel.Controls.Add(layout);
                panel.Tag = hint;
                return panel;
            }

            private Panel BuildLogCard(out Label title)
            {
                var panel = CreateCardPanel(true);
                panel.Padding = new Padding(14, 12, 14, 12);
                panel.Paint += delegate(object sender, PaintEventArgs e)
                {
                    var r = panel.ClientRectangle;
                    using (var brush = new LinearGradientBrush(
                        r,
                        _theme == UiTheme.Dark ? Color.FromArgb(5, 7, 12) : Color.FromArgb(150, 153, 158),
                        _theme == UiTheme.Dark ? Color.FromArgb(8, 12, 18) : Color.FromArgb(128, 131, 137),
                        12F))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var path = CreateRoundedPath(r, 18))
                        {
                            e.Graphics.FillPath(brush, path);
                        }
                    }
                    DrawRoundedBorder(
                        e.Graphics,
                        r,
                        18,
                        _theme == UiTheme.Dark ? Color.FromArgb(24, 32, 45) : Color.FromArgb(181, 185, 192));
                };

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    BackColor = Color.Transparent
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                var headerPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent
                };

                title = new Label
                {
                    Text = "ACTIVITY LOG",
                    AutoSize = true,
                    Location = new Point(2, 4),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(236, 239, 244),
                    Font = new Font("Segoe UI Semibold", 11.2F, FontStyle.Bold)
                };
                var divider = new Panel
                {
                    Height = 1,
                    Dock = DockStyle.Bottom,
                    BackColor = Color.FromArgb(170, 177, 190)
                };
                headerPanel.Controls.Add(title);
                headerPanel.Controls.Add(divider);

                var logBox = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None,
                    ReadOnly = true,
                    BackColor = Color.FromArgb(8, 12, 18),
                    ForeColor = Color.FromArgb(223, 230, 238),
                    Font = new Font("Microsoft YaHei", 10.8F, FontStyle.Regular),
                    DetectUrls = false
                };

                layout.Controls.Add(headerPanel, 0, 0);
                layout.Controls.Add(logBox, 0, 1);

                panel.Controls.Add(layout);
                panel.Tag = logBox;
                return panel;
            }

            private Panel CreateCardPanel(bool drawBorder)
            {
                var panel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = _cardBackgroundColor
                };
                AttachRoundedCorners(panel, 18);

                if (drawBorder)
                {
                    panel.Paint += delegate(object sender, PaintEventArgs e)
                    {
                        Rectangle r = panel.ClientRectangle;
                        DrawRoundedBorder(e.Graphics, r, 18, _cardBorderColor);
                    };
                }

                return panel;
            }

            private static Button CreateActionButton(string text, Color backColor)
            {
                var visual = new ActionButtonVisual
                {
                    Text = text ?? string.Empty,
                    FillColor = backColor
                };
                var btn = new Button
                {
                    Text = string.Empty,
                    Tag = visual,
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(20, 26, 34),
                    Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Height = 68,
                    TextAlign = ContentAlignment.MiddleCenter,
                    UseVisualStyleBackColor = false
                };
                ApplyActionButtonStyle(btn, backColor);
                btn.Paint += ActionButtonPaint;
                return btn;
            }

            private static void ApplyActionButtonStyle(Button button, Color backColor)
            {
                var visual = button.Tag as ActionButtonVisual;
                if (visual != null)
                {
                    visual.FillColor = backColor;
                }
                button.BackColor = Color.Transparent;
                button.ForeColor = Color.FromArgb(20, 26, 34);
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = Color.Transparent;
                button.FlatAppearance.MouseDownBackColor = Color.Transparent;
            }

            private static void ActionButtonPaint(object sender, PaintEventArgs e)
            {
                var button = sender as Button;
                if (button == null)
                {
                    return;
                }

                var visual = button.Tag as ActionButtonVisual;
                if (visual == null)
                {
                    return;
                }

                string raw = visual.Text ?? string.Empty;
                if (raw.Length == 0)
                {
                    return;
                }

                string[] lines = raw.Split(new[] { '\n' }, 2);
                string firstLine = lines[0].Trim();
                string secondLine = lines.Length > 1 ? lines[1].Trim() : string.Empty;

                string icon = string.Empty;
                string firstText = firstLine;
                if (visual.ParseLeadingIcon)
                {
                    int splitIndex = firstLine.IndexOf(' ');
                    if (splitIndex > 0)
                    {
                        icon = firstLine.Substring(0, splitIndex).Trim();
                        firstText = firstLine.Substring(splitIndex + 1).Trim();
                    }
                    else if (firstLine.Length > 1)
                    {
                        icon = firstLine.Substring(0, 1);
                        firstText = firstLine.Substring(1).Trim();
                    }
                }

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

                Color baseFill = visual.FillColor;
                Point cursor = button.PointToClient(Control.MousePosition);
                bool hovered = visual.EnableHoverEffects && button.Enabled && button.ClientRectangle.Contains(cursor);
                bool pressed = hovered && (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;
                Color fillColor = baseFill;
                if (!button.Enabled)
                {
                    fillColor = Color.FromArgb(
                        (baseFill.R + 215) / 2,
                        (baseFill.G + 215) / 2,
                        (baseFill.B + 215) / 2);
                }
                else if (pressed)
                {
                    fillColor = LightenColor(baseFill, -12);
                }
                else if (hovered)
                {
                    fillColor = LightenColor(baseFill, 9);
                }

                var faceRect = new Rectangle(1, 1, Math.Max(1, button.ClientSize.Width - 3), Math.Max(1, button.ClientSize.Height - 4));
                if (visual.DrawShadow)
                {
                    DrawSoftShadow(e.Graphics, faceRect, visual.CornerRadius, hovered ? 58 : 46, 3, 1);
                }

                using (var facePath = CreateRoundedPath(faceRect, visual.CornerRadius))
                using (var faceBrush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(faceBrush, facePath);
                }

                Color borderColor = fillColor.GetBrightness() > 0.60
                    ? Color.FromArgb(90, 40, 52, 70)
                    : Color.FromArgb(112, 255, 255, 255);
                using (var borderPath = CreateRoundedPath(faceRect, visual.CornerRadius))
                using (var borderPen = new Pen(borderColor, 1.1F))
                {
                    e.Graphics.DrawPath(borderPen, borderPath);
                }

                using (var iconFont = new Font("Segoe UI Symbol", button.Font.Size + visual.IconSizeDelta, FontStyle.Regular))
                using (var textFont = new Font(button.Font.FontFamily, button.Font.Size, visual.TextStyle))
                using (var textBrush = new SolidBrush(button.Enabled ? button.ForeColor : SystemColors.GrayText))
                {
                    if (visual.UseCenterTextLayout && secondLine.Length == 0)
                    {
                        string centeredText = firstText.Length > 0 ? firstText : raw;
                        bool drewByPath = false;
                        try
                        {
                            using (var path = new GraphicsPath())
                            using (var sfTypographic = (StringFormat)StringFormat.GenericTypographic.Clone())
                            {
                                sfTypographic.FormatFlags |= StringFormatFlags.NoClip;
                                float emSize = e.Graphics.DpiY * textFont.SizeInPoints / 72F;
                                path.AddString(
                                    centeredText,
                                    textFont.FontFamily,
                                    (int)textFont.Style,
                                    emSize,
                                    new PointF(0F, 0F),
                                    sfTypographic);
                                RectangleF bounds = path.GetBounds();
                                float targetX = ((button.ClientSize.Width - 1F) / 2F) + visual.CenterOffsetX;
                                float targetY = ((button.ClientSize.Height - 1F) / 2F) + visual.CenterOffsetY;
                                float dx = targetX - (bounds.Left + (bounds.Width / 2F));
                                float dy = targetY - (bounds.Top + (bounds.Height / 2F));
                                using (var matrix = new Matrix())
                                {
                                    matrix.Translate(dx, dy);
                                    path.Transform(matrix);
                                }
                                e.Graphics.FillPath(textBrush, path);
                                drewByPath = true;
                            }
                        }
                        catch
                        {
                        }

                        if (!drewByPath)
                        {
                            using (var sf = new StringFormat
                            {
                                Alignment = StringAlignment.Center,
                                LineAlignment = StringAlignment.Center
                            })
                            {
                                var textRect = new RectangleF(
                                    visual.CenterOffsetX,
                                    visual.CenterOffsetY,
                                    Math.Max(1, button.ClientSize.Width - 1),
                                    Math.Max(1, button.ClientSize.Height - 1));
                                e.Graphics.DrawString(centeredText, textFont, textBrush, textRect, sf);
                            }
                        }
                        return;
                    }

                    SizeF iconSize = icon.Length > 0 ? e.Graphics.MeasureString(icon, iconFont) : SizeF.Empty;
                    SizeF firstSize = e.Graphics.MeasureString(firstText, textFont);
                    SizeF secondSize = secondLine.Length > 0 ? e.Graphics.MeasureString(secondLine, textFont) : SizeF.Empty;
                    float gapX = (iconSize.Width > 0 && firstText.Length > 0) ? 5F : 0F;
                    float blockHeight = firstSize.Height + (secondLine.Length > 0 ? secondSize.Height - 2F : 0F);
                    float y1 = Math.Max(6F, (button.ClientSize.Height - blockHeight) / 2F - 1F);

                    float firstLineWidth = firstSize.Width + iconSize.Width + gapX;
                    float x1 = Math.Max(8F, (button.ClientSize.Width - firstLineWidth) / 2F);

                    if (icon.Length > 0)
                    {
                        e.Graphics.DrawString(icon, iconFont, textBrush, x1, y1);
                    }

                    float textX = x1 + iconSize.Width + gapX;
                    e.Graphics.DrawString(firstText, textFont, textBrush, textX, y1);

                    if (secondLine.Length > 0)
                    {
                        float y2 = y1 + firstSize.Height - 2F;
                        float x2 = Math.Max(8F, (button.ClientSize.Width - secondSize.Width) / 2F);
                        e.Graphics.DrawString(secondLine, textFont, textBrush, x2, y2);
                    }
                }
            }

            private static void DrawSoftShadow(
                Graphics graphics,
                Rectangle bounds,
                int radius,
                int maxAlpha,
                int layers,
                int offsetY)
            {
                if (bounds.Width <= 0 || bounds.Height <= 0 || layers <= 0 || maxAlpha <= 0)
                {
                    return;
                }

                for (int i = layers; i >= 1; i--)
                {
                    int spread = i;
                    int alpha = Math.Max(4, (maxAlpha * (layers - i + 1)) / (layers * 2));
                    var shadowRect = new Rectangle(
                        bounds.X - spread,
                        bounds.Y - spread + offsetY,
                        bounds.Width + spread * 2,
                        bounds.Height + spread * 2);
                    using (var path = CreateRoundedPath(shadowRect, radius + spread))
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                    {
                        graphics.FillPath(brush, path);
                    }
                }
            }

            private static Color LightenColor(Color color, int delta)
            {
                int r = Math.Max(0, Math.Min(255, color.R + delta));
                int g = Math.Max(0, Math.Min(255, color.G + delta));
                int b = Math.Max(0, Math.Min(255, color.B + delta));
                return Color.FromArgb(r, g, b);
            }

            private void SetBusy(bool busy, string detail)
            {
                _busy = busy;
                _btnStart.Enabled = !busy;
                _btnStop.Enabled = !busy;
                _btnOpenDashboard.Enabled = !busy;
                _btnCheck.Enabled = !busy;
                UseWaitCursor = busy;
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    _busyText = detail;
                    _busyTick = 0;
                    _statusHint.Text = detail;
                }
                if (busy)
                {
                    _progressBar.Style = ProgressBarStyle.Marquee;
                    _progressBar.MarqueeAnimationSpeed = 25;
                    _busyTimer.Start();
                }
                else
                {
                    _busyTimer.Stop();
                    _progressBar.MarqueeAnimationSpeed = 0;
                    _progressBar.Style = ProgressBarStyle.Continuous;
                    _progressBar.Value = 0;
                }
                Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            }

            private void SetStatusNeutral(string titleEnglish, string titleChinese, string detailEnglish, string detailChinese)
            {
                _statusTone = StatusTone.Neutral;
                ApplyStatus(titleEnglish, titleChinese, detailEnglish, detailChinese);
            }

            private void SetStatusGood(string titleEnglish, string titleChinese, string detailEnglish, string detailChinese)
            {
                _statusTone = StatusTone.Good;
                ApplyStatus(titleEnglish, titleChinese, detailEnglish, detailChinese);
            }

            private void SetStatusWarn(string titleEnglish, string titleChinese, string detailEnglish, string detailChinese)
            {
                _statusTone = StatusTone.Warn;
                ApplyStatus(titleEnglish, titleChinese, detailEnglish, detailChinese);
            }

            private void SetStatusBad(string titleEnglish, string titleChinese, string detailEnglish, string detailChinese)
            {
                _statusTone = StatusTone.Bad;
                ApplyStatus(titleEnglish, titleChinese, detailEnglish, detailChinese);
            }

            private void RefreshStatusBadgeColors()
            {
                bool dark = _theme == UiTheme.Dark;
                switch (_statusTone)
                {
                    case StatusTone.Neutral:
                        SetActionButtonFill(_statusBadge, dark ? Color.FromArgb(57, 64, 78) : Color.FromArgb(194, 204, 220));
                        _statusBadge.ForeColor = dark ? Color.FromArgb(231, 238, 248) : Color.FromArgb(12, 22, 38);
                        break;
                    case StatusTone.Good:
                        SetActionButtonFill(_statusBadge, dark ? Color.FromArgb(57, 64, 78) : Color.FromArgb(214, 218, 225));
                        _statusBadge.ForeColor = dark ? Color.FromArgb(231, 238, 248) : Color.FromArgb(31, 39, 51);
                        break;
                    case StatusTone.Warn:
                        SetActionButtonFill(_statusBadge, dark ? Color.FromArgb(80, 73, 61) : Color.FromArgb(236, 224, 196));
                        _statusBadge.ForeColor = dark ? Color.FromArgb(255, 226, 168) : Color.FromArgb(94, 67, 12);
                        break;
                    case StatusTone.Bad:
                        SetActionButtonFill(_statusBadge, dark ? Color.FromArgb(86, 58, 66) : Color.FromArgb(236, 214, 218));
                        _statusBadge.ForeColor = dark ? Color.FromArgb(255, 218, 223) : Color.FromArgb(102, 44, 55);
                        break;
                    default:
                        SetActionButtonFill(_statusBadge, dark ? Color.FromArgb(57, 64, 78) : Color.FromArgb(214, 218, 225));
                        _statusBadge.ForeColor = dark ? Color.FromArgb(231, 238, 248) : Color.FromArgb(31, 39, 51);
                        break;
                }
            }

            private void ApplyStatus(string titleEnglish, string titleChinese, string detailEnglish, string detailChinese)
            {
                _statusTitleEnglish = titleEnglish;
                _statusTitleChinese = titleChinese;
                _statusDetailEnglish = detailEnglish;
                _statusDetailChinese = detailChinese;
                RefreshStatusBadgeColors();
                RefreshLocalizedStatusTexts();
            }

            private void RunWorker(string busyText, Func<CommandResult> workerFunc, Action<CommandResult> onSuccess)
            {
                if (_busy)
                {
                    AppendLog(Tr("An operation is already running.", "已有任务正在运行。"));
                    return;
                }

                SetBusy(true, busyText);
                var worker = new BackgroundWorker();
                worker.DoWork += delegate(object sender, DoWorkEventArgs e)
                {
                    e.Result = workerFunc();
                };
                worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
                {
                    SetBusy(false, Tr("Ready", "就绪"));
                    if (e.Error != null)
                    {
                        SetStatusBad("Operation Error", "操作错误", e.Error.Message, e.Error.Message);
                        AppendLog(Tr("Operation failed: ", "操作失败：") + e.Error.Message);
                        return;
                    }

                    var result = e.Result as CommandResult;
                    if (result == null)
                    {
                        SetStatusBad("Operation Error", "操作错误", "Unknown result", "未知结果");
                        AppendLog(Tr("Operation failed: unknown result.", "操作失败：未知结果。"));
                        return;
                    }

                    onSuccess(result);
                };
                worker.RunWorkerAsync();
            }

            private void RunInstallSetup()
            {
                ResolveEffectiveMode(true, true);
                AppendLog(
                    Tr("Running setup/install for mode: ", "正在执行安装/初始化，模式：") +
                    Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)));
                RunWorker(
                    Tr("Installing / Initializing OpenClaw...", "正在安装 / 初始化 OpenClaw..."),
                    delegate { return RunCommandCapture(BuildInstallCommandSpec()); },
                    delegate(CommandResult result)
                    {
                        EmitOutput(result.Output);
                        if (result.ExitCode == 0)
                        {
                            SetStatusGood("Setup Completed", "初始化完成", "Setup/install completed.", "安装/初始化已完成。");
                            AppendLog(Tr("Setup/install completed.", "安装/初始化已完成。"));
                        }
                        else
                        {
                            SetStatusBad("Setup Failed", "初始化失败", "Exit code: " + result.ExitCode, "退出码：" + result.ExitCode);
                            AppendLog(Tr("Setup/install failed. Exit code: ", "安装/初始化失败。退出码：") + result.ExitCode);
                        }
                    });
            }

            private void StartOpenClaw()
            {
                ResolveEffectiveMode(true, false);
                AppendLog(Tr("Starting OpenClaw...", "正在启动 OpenClaw..."));
                AppendLog(
                    Tr("Active mode: ", "当前模式：") +
                    Tr(ModeLabelEnglish(_effectiveMode), ModeLabelChinese(_effectiveMode)));
                RunWorker(
                    Tr("Starting OpenClaw (target < 1 minute)", "正在启动 OpenClaw（目标 1 分钟内）"),
                    delegate { return RunCommandCapture(BuildStartCommandSpec()); },
                    delegate(CommandResult result)
                    {
                        EmitOutput(result.Output);
                        if (result.ExitCode != 0)
                        {
                            if (result.ExitCode == 124)
                            {
                                SetStatusBad("Start Timeout", "启动超时", "Startup exceeded 75 seconds.", "启动超过 75 秒。");
                                AppendLog(Tr("Start timed out. Check runtime state and run Start again.", "启动超时。请检查运行时状态后重试。"));
                                return;
                            }
                            if (!string.IsNullOrWhiteSpace(result.Output) && (
                                result.Output.IndexOf("Docker daemon still unreachable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                result.Output.IndexOf("Docker daemon unreachable", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                SetStatusBad("Docker Unreachable", "Docker 不可达", "Docker unavailable. Retry Start.", "Docker 不可用，请重试启动。");
                            }
                            else
                            {
                                SetStatusBad("Start Failed", "启动失败", "Exit code: " + result.ExitCode, "退出码：" + result.ExitCode);
                            }
                            AppendLog(Tr("Start command exit code: ", "启动命令退出码：") + result.ExitCode);
                            return;
                        }

                        SetStatusGood("OpenClaw Running", "OpenClaw 运行中", "OpenClaw start completed.", "OpenClaw 启动完成。");
                        AppendLog(Tr("Start command completed.", "启动命令已完成。"));
                        AppendLog(Tr("Tip: Click 'Check Status/Health' for a full probe.", "提示：点击“检查状态/健康度”可进行完整探测。"));
                    });
            }

            private void StopOpenClaw()
            {
                ResolveEffectiveMode(true, false);
                AppendLog(Tr("Stopping OpenClaw...", "正在停止 OpenClaw..."));
                RunWorker(
                    Tr("Stopping OpenClaw...", "正在停止 OpenClaw..."),
                    delegate { return RunCommandCapture(BuildStopCommandSpec()); },
                    delegate(CommandResult result)
                    {
                        EmitOutput(result.Output);
                        if (result.ExitCode == 0)
                        {
                            SetStatusNeutral("OpenClaw Stopped", "OpenClaw 已停止", "Gateway container has been stopped.", "网关容器已停止。");
                            AppendLog(Tr("OpenClaw stopped.", "OpenClaw 已停止。"));
                        }
                        else
                        {
                            SetStatusBad("Stop Failed", "停止失败", "Exit code: " + result.ExitCode, "退出码：" + result.ExitCode);
                            AppendLog(Tr("Stop command exit code: ", "停止命令退出码：") + result.ExitCode);
                        }
                    });
            }

            private void OpenDashboard()
            {
                ResolveEffectiveMode(false, false);
                AppendLog(Tr("Opening dashboard...", "正在打开 Dashboard..."));
                RunWorker(
                    Tr("Opening dashboard", "正在打开 Dashboard"),
                    delegate { return RunCommandCapture(BuildOpenDashboardCommandSpec()); },
                    delegate(CommandResult result)
                    {
                        EmitOutput(result.Output);
                        if (result.ExitCode == 0)
                        {
                            string dashboardUrl = ExtractDashboardUrlFromOutput(result.Output);
                            if (!string.IsNullOrWhiteSpace(dashboardUrl))
                            {
                                if (TryOpenUrl(dashboardUrl))
                                {
                                    AppendLog(Tr("Dashboard URL opened: ", "已打开 Dashboard URL：") + dashboardUrl);
                                }
                                else
                                {
                                    AppendLog(Tr("Dashboard URL detected but browser launch failed: ", "检测到 Dashboard URL，但浏览器启动失败：") + dashboardUrl);
                                }
                            }
                            SetStatusGood("Dashboard Opened", "Dashboard 已打开", "Dashboard launch command completed.", "Dashboard 启动命令已完成。");
                            AppendLog(Tr("Dashboard open command completed.", "Dashboard 打开命令已完成。"));
                        }
                        else
                        {
                            SetStatusBad("Dashboard Error", "Dashboard 错误", "Exit code: " + result.ExitCode, "退出码：" + result.ExitCode);
                            AppendLog(Tr("Open dashboard failed. Exit code: ", "打开 Dashboard 失败。退出码：") + result.ExitCode);
                        }
                    });
            }

            private void CheckHealth()
            {
                ResolveEffectiveMode(true, false);
                AppendLog(Tr("Checking status and health...", "正在检查状态与健康度..."));
                RunWorker(
                    Tr("Checking status and health...", "正在检查状态与健康度..."),
                    delegate { return RunCommandCapture(BuildStatusCommandSpec()); },
                    delegate(CommandResult result)
                    {
                        EmitOutput(result.Output);

                        if (result.ExitCode != 0)
                        {
                            SetStatusBad("Health Check Failed", "健康检查失败", "Exit code: " + result.ExitCode, "退出码：" + result.ExitCode);
                            return;
                        }

                        var kv = ParseKeyValueLines(result.Output);
                        bool dockerUp = Get(kv, "docker") == "up";
                        string gatewayState = FirstNonEmpty(Get(kv, "gateway"), Get(kv, "gateway_container"));
                        bool gatewayRunning = string.Equals(gatewayState, "running", StringComparison.OrdinalIgnoreCase);
                        bool tokenOk = Get(kv, "token") == "ok";
                        string httpRoot = Get(kv, "http_root");
                        string httpHealth = Get(kv, "http_health");
                        bool httpRootOk = httpRoot == "200" || httpRoot == "301" || httpRoot == "302" || httpRoot == "401" || httpRoot == "403";
                        bool dockerStateKnownDown = Get(kv, "docker") == "down";
                        bool dockerStateKnownUp = dockerUp;

                        if (gatewayRunning && httpRootOk)
                        {
                            SetStatusGood("Healthy", "健康", "Gateway is reachable and healthy.", "网关可达且健康。");
                        }
                        else if (gatewayRunning)
                        {
                            SetStatusWarn("HTTP Issue", "HTTP 异常", "Container runs but HTTP health is abnormal.", "容器已运行，但 HTTP 健康异常。");
                        }
                        else if (dockerStateKnownDown)
                        {
                            SetStatusBad("Docker Down", "Docker 未运行", "Docker runtime is not reachable.", "Docker 运行时不可达。");
                        }
                        else if (dockerStateKnownUp && !gatewayRunning)
                        {
                            SetStatusNeutral("Not Running", "未运行", "Gateway container/process is not running.", "网关容器/进程未运行。");
                        }
                        else
                        {
                            SetStatusNeutral("Not Running", "未运行", "OpenClaw gateway is not running.", "OpenClaw 网关未运行。");
                        }

                        string modeValue = FirstNonEmpty(Get(kv, "mode"), ModeToConfigValue(_effectiveMode));
                        AppendLog(Tr("Summary: docker=", "摘要：docker=") + Get(kv, "docker") +
                                  "; gateway=" + gatewayState +
                                  "; token=" + Get(kv, "token") +
                                  "; http_root=" + httpRoot +
                                  "; http_health=" + httpHealth +
                                  "; mode=" + modeValue);

                        if (!tokenOk)
                        {
                            AppendLog(Tr("Hint: token missing. Click 'Start OPENCLAW' first.", "提示：token 缺失，请先点击“启动 OPENCLAW”。"));
                        }
                    });
            }

            private void AppendLog(string msg)
            {
                string ts = DateTime.Now.ToString("HH:mm:ss");
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionColor = _logTimestampColor;
                _logBox.AppendText(ts + " ");
                _logBox.SelectionColor = ResolveLogLineColor(msg);
                _logBox.AppendText(msg + Environment.NewLine);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();

                if (_logBox.TextLength > 180000)
                {
                    _logBox.Select(0, 60000);
                    _logBox.SelectedText = string.Empty;
                }
            }

            private Color ResolveLogLineColor(string message)
            {
                string text = (message ?? string.Empty).ToLowerInvariant();
                if (text.Contains("docker=up") || text.Contains("token=ok") || text.Contains("healthy") || text.Contains("completed"))
                {
                    return _logSuccessColor;
                }
                if (text.Contains("error") || text.Contains("failed") || text.Contains("missing") || text.Contains("timeout") || text.Contains("down"))
                {
                    return _logErrorColor;
                }
                if (text.Contains("switched") || text.Contains("切换") || text.StartsWith("summary:") || text.StartsWith("摘要："))
                {
                    return _logMutedColor;
                }
                return _logTextColor;
            }

            private void EmitOutput(string output)
            {
                if (string.IsNullOrWhiteSpace(output))
                {
                    AppendLog(Tr("No output.", "无输出。"));
                    return;
                }

                AppendLog(Tr("Output:", "输出："));
                foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (line.Length > 0)
                    {
                        AppendLog("  " + line);
                    }
                }
            }

            private static string Get(Dictionary<string, string> map, string key)
            {
                string val;
                return map.TryGetValue(key, out val) ? val : "";
            }

            private static Dictionary<string, string> ParseKeyValueLines(string text)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(text)) return map;

                string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (string raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    int idx = raw.IndexOf('=');
                    if (idx <= 0) continue;
                    string key = raw.Substring(0, idx).Trim();
                    string val = raw.Substring(idx + 1).Trim();
                    if (key.Length > 0)
                    {
                        map[key] = val;
                    }
                }
                return map;
            }

            private static string ExtractDashboardUrlFromOutput(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return string.Empty;
                }

                string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (string raw in lines)
                {
                    string line = (raw ?? string.Empty).Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    if (line.StartsWith("Dashboard URL:", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring("Dashboard URL:".Length).Trim();
                    }
                    if (line.StartsWith("dashboard_url=", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring("dashboard_url=".Length).Trim();
                    }
                    if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        return line;
                    }
                }

                return string.Empty;
            }

            private static bool TryOpenUrl(string url)
            {
                string target = (url ?? string.Empty).Trim();
                if (target.Length == 0)
                {
                    return false;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    return true;
                }
                catch
                {
                }

                try
                {
                    Process.Start(new ProcessStartInfo("rundll32.exe", "url.dll,FileProtocolHandler " + target) { UseShellExecute = true });
                    return true;
                }
                catch
                {
                }

                return false;
            }

            private CommandResult RunCommandCapture(CommandSpec spec)
            {
                if (spec == null)
                {
                    return new CommandResult { ExitCode = 2, Output = "No command spec." };
                }

                Program.TryWriteDiagnostic(
                    "command",
                    "start mode=" + ModeToConfigValue(spec.Mode) +
                    " action=" + (spec.ActionName ?? "-") +
                    " runtime=" + spec.Runtime +
                    " timeout=" + spec.TimeoutSeconds);

                string tempScriptPath = null;
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    if (spec.Runtime == CommandRuntime.WslBash)
                    {
                        psi.FileName = "wsl.exe";
                        psi.Arguments = "bash -lc \"" + EscapeForBashDoubleQuoted(spec.Command ?? string.Empty) + "\"";
                        string sudoPasswordPlain = GetStoredWslSudoPasswordPlain();
                        if (!string.IsNullOrEmpty(sudoPasswordPlain))
                        {
                            psi.EnvironmentVariables["OPENCLAW_PANEL_SUDO_PASS"] = sudoPasswordPlain;
                            psi.EnvironmentVariables["OPENCLAW_SUDO_PASSWORD"] = sudoPasswordPlain;
                        }
                    }
                    else
                    {
                        tempScriptPath = Path.Combine(
                            Path.GetTempPath(),
                            "openclaw-panel-" + Guid.NewGuid().ToString("N") + ".ps1");
                        File.WriteAllText(tempScriptPath, spec.Command ?? string.Empty, new UTF8Encoding(false));
                        psi.FileName = "powershell.exe";
                        psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + tempScriptPath + "\"";
                    }

                    CommandResult result = ExecuteProcessCapture(psi, Math.Max(1, spec.TimeoutSeconds));
                    Program.TryWriteDiagnostic(
                        "command",
                        "end mode=" + ModeToConfigValue(spec.Mode) +
                        " action=" + (spec.ActionName ?? "-") +
                        " exit=" + result.ExitCode);
                    return result;
                }
                catch (Exception ex)
                {
                    Program.TryWriteErrorLog("command-run", ex);
                    return new CommandResult
                    {
                        ExitCode = 127,
                        Output = ex.Message
                    };
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(tempScriptPath))
                    {
                        try { File.Delete(tempScriptPath); } catch { }
                    }
                }
            }

            private static CommandResult ExecuteProcessCapture(ProcessStartInfo psi, int timeoutSeconds)
            {
                using (var proc = new Process { StartInfo = psi })
                {
                    var outputBuilder = new StringBuilder();
                    var sync = new object();

                    proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (sync)
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (sync)
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    bool exited = proc.WaitForExit(timeoutSeconds * 1000);
                    if (!exited)
                    {
                        try { proc.Kill(); } catch { }
                        return new CommandResult
                        {
                            ExitCode = 124,
                            Output = "Command timed out after " + timeoutSeconds + " seconds."
                        };
                    }

                    proc.WaitForExit();
                    string output;
                    lock (sync)
                    {
                        output = outputBuilder.ToString().Trim();
                    }
                    return new CommandResult
                    {
                        ExitCode = proc.ExitCode,
                        Output = output
                    };
                }
            }

            private static string EscapeForBashDoubleQuoted(string value)
            {
                if (string.IsNullOrEmpty(value)) return "";
                return value
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("$", "\\$")
                    .Replace("`", "\\`");
            }
        }
    }
}
