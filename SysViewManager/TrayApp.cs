using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace SysViewManager;

/// <summary>
/// Application context principale : icône tray + menu contextuel.
/// Gère SysViewHardware, Bridge (qui démarre Aether en sous-process) et surveille les 3 ports.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    // ─── Ports ────────────────────────────────────────────────────────────────
    private const int PORT_HW     = 8086;
    private const int PORT_BRIDGE = 5001;
    private const int PORT_AETHER = 8001;

    // ─── Chemins (relatifs au dossier de l'exe = racine SysView V6) ───────────
    private readonly string _base;
    private readonly string _hwExe;
    private readonly string _apiDir;
    private readonly string _bridgePyw;
    private readonly string _logDir;
    private string _pythonW = "";

    // ─── Processus suivis ─────────────────────────────────────────────────────
    private Process? _hwProc;
    private Process? _bridgeProc;

    // ─── État courant ─────────────────────────────────────────────────────────
    private bool _hwRunning;
    private bool _bridgeRunning;
    private bool _aetherRunning;

    // ─── UI ───────────────────────────────────────────────────────────────────
    private readonly NotifyIcon             _tray;
    private readonly ContextMenuStrip       _menu;
    private readonly ToolStripMenuItem      _miHW;
    private readonly ToolStripMenuItem      _miBridge;
    private readonly ToolStripMenuItem      _miStartAll;
    private readonly ToolStripMenuItem      _miStopAll;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly SynchronizationContext _syncCtx;

    // ─── Constructeur ─────────────────────────────────────────────────────────
    public TrayApp()
    {
        _base      = AppContext.BaseDirectory;
        _hwExe     = Path.Combine(_base,   "SysViewHardware", "SysViewHardware.exe");
        _apiDir    = Path.Combine(_base,   "API");
        _bridgePyw = Path.Combine(_apiDir, "SysViewBridge.pyw");
        _logDir    = Path.Combine(_base,   "logs");
        Directory.CreateDirectory(_logDir);

        FindPythonW();

        // Capturer le contexte de synchronisation UI (toujours disponible après ApplicationConfiguration.Initialize)
        _syncCtx = SynchronizationContext.Current ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();

        // ── Menu contextuel ──
        _menu = new ContextMenuStrip();

        var miTitle = new ToolStripMenuItem("SysView V6 Manager") { Enabled = false };
        miTitle.Font = new Font(SystemFonts.MenuFont ?? new Font("Segoe UI", 9f), FontStyle.Bold);
        _menu.Items.Add(miTitle);
        _menu.Items.Add(new ToolStripSeparator());

        _miHW = new ToolStripMenuItem();
        _miHW.Click += (_, _) => ToggleHW();
        _menu.Items.Add(_miHW);

        _miBridge = new ToolStripMenuItem();
        _miBridge.Click += (_, _) => ToggleBridge();
        _menu.Items.Add(_miBridge);

        _menu.Items.Add(new ToolStripSeparator());

        _miStartAll = new ToolStripMenuItem("▶  Tout démarrer");
        _miStartAll.Click += (_, _) => StartAll();
        _menu.Items.Add(_miStartAll);

        _miStopAll = new ToolStripMenuItem("■  Tout arrêter");
        _miStopAll.Click += (_, _) => StopAll();
        _menu.Items.Add(_miStopAll);

        _menu.Items.Add(new ToolStripSeparator());

        var miLogs = new ToolStripMenuItem("📁  Ouvrir les logs");
        miLogs.Click += (_, _) => OpenLogs();
        _menu.Items.Add(miLogs);

        _menu.Items.Add(new ToolStripSeparator());

        var miQuit = new ToolStripMenuItem("✕  Quitter");
        miQuit.Click += (_, _) => Quit();
        _menu.Items.Add(miQuit);

        // ── Icône tray ──
        _tray = new NotifyIcon
        {
            Text             = "SysView V6",
            Icon             = MakeTrayIcon(Color.Gray),
            Visible          = true,
            ContextMenuStrip = _menu,
        };
        _tray.DoubleClick += (_, _) => ShowStatusPopup();

        // ── Timer de polling (5 s) ──
        _pollTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _pollTimer.Tick += (_, _) => RefreshStatus();
        _pollTimer.Start();

        // Premier check immédiat
        RefreshStatus();
    }

    // ─── Rafraîchissement de l'état ───────────────────────────────────────────
    private void RefreshStatus()
    {
        _hwRunning     = IsPortListening(PORT_HW)     || IsAlive(_hwProc);
        _bridgeRunning = IsPortListening(PORT_BRIDGE) || IsAlive(_bridgeProc);
        _aetherRunning = IsPortListening(PORT_AETHER);

        // Libellés du menu
        _miHW.Text = _hwRunning
            ? "● SysViewHardware  [Arrêter]"
            : "○ SysViewHardware  [Démarrer]";

        _miBridge.Text = _bridgeRunning
            ? $"● Bridge + Aether {(_aetherRunning ? "●" : "○")}  [Arrêter]"
            : $"○ Bridge + Aether ○  [Démarrer]";

        _miStartAll.Enabled = !(_hwRunning && _bridgeRunning);
        _miStopAll.Enabled  = _hwRunning || _bridgeRunning;

        // Couleur de l'icône tray
        Color c = (_hwRunning && _bridgeRunning && _aetherRunning)
                      ? Color.FromArgb(0, 200, 100)     // tout vert
                  : (_hwRunning || _bridgeRunning)
                      ? Color.Orange                    // partiel
                      : Color.FromArgb(200, 50, 50);    // tout rouge

        var oldIcon = _tray.Icon;
        _tray.Icon = MakeTrayIcon(c);
        oldIcon?.Dispose();

        _tray.Text = $"SysView V6 — HW:{S(_hwRunning)}  Bridge:{S(_bridgeRunning)}  Aether:{S(_aetherRunning)}";
    }

    private static string S(bool on) => on ? "✓" : "✗";

    // ─── Actions ──────────────────────────────────────────────────────────────
    private void StartAll()
    {
        StartHW();
        StartBridge();
        // Rafraîchir après un court délai (le bridge met ~2 s à ouvrir son port)
        Task.Delay(3000).ContinueWith(_ => _syncCtx.Post(__ => RefreshStatus(), null));
    }

    private void StopAll()
    {
        StopBridge();
        StopHW();
        Task.Delay(1500).ContinueWith(_ => _syncCtx.Post(__ => RefreshStatus(), null));
    }

    private void ToggleHW()
    {
        if (_hwRunning) StopHW(); else StartHW();
        RefreshStatus();
    }

    private void ToggleBridge()
    {
        if (_bridgeRunning) StopBridge(); else StartBridge();
        Task.Delay(2000).ContinueWith(_ => _syncCtx.Post(__ => RefreshStatus(), null));
    }

    private void StartHW()
    {
        if (!File.Exists(_hwExe))
        {
            Notify("SysViewHardware.exe introuvable.\nVérifiez l'installation.", ToolTipIcon.Error);
            return;
        }
        KillPort(PORT_HW);
        _hwProc = Launch(_hwExe, "", _base);
        Notify("SysViewHardware démarré.", ToolTipIcon.Info);
    }

    private void StopHW()
    {
        KillProcess(_hwProc, "SysViewHardware");
        KillPort(PORT_HW);
        _hwProc = null;
        Notify("SysViewHardware arrêté.", ToolTipIcon.Info);
    }

    private void StartBridge()
    {
        if (_pythonW == "")
        {
            Notify("pythonw.exe introuvable.\nVérifiez que Python est installé.", ToolTipIcon.Error);
            return;
        }
        if (!File.Exists(_bridgePyw))
        {
            Notify($"SysViewBridge.pyw introuvable :\n{_bridgePyw}", ToolTipIcon.Error);
            return;
        }

        // Tuer l'ancienne instance via bridge.pid si présent
        var pidFile = Path.Combine(_apiDir, "bridge.pid");
        if (File.Exists(pidFile))
        {
            if (int.TryParse(File.ReadAllText(pidFile).Trim(), out var oldPid))
                try { Process.GetProcessById(oldPid).Kill(entireProcessTree: true); } catch { }
            File.Delete(pidFile);
        }

        KillPort(PORT_BRIDGE);
        KillPort(PORT_AETHER);

        _bridgeProc = Launch(_pythonW, $"\"{_bridgePyw}\"", _apiDir);
        Notify("Bridge (+ Aether) démarré.", ToolTipIcon.Info);
    }

    private void StopBridge()
    {
        // Priorité au PID enregistré (kill arborescence = tue aussi Aether)
        var pidFile = Path.Combine(_apiDir, "bridge.pid");
        if (File.Exists(pidFile))
        {
            if (int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid))
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            File.Delete(pidFile);
        }

        KillProcess(_bridgeProc, null);
        KillPort(PORT_BRIDGE);
        KillPort(PORT_AETHER);
        _bridgeProc = null;
        Notify("Bridge + Aether arrêtés.", ToolTipIcon.Info);
    }

    // ─── Helpers processus ────────────────────────────────────────────────────
    private static bool IsAlive(Process? p)
    {
        if (p == null) return false;
        try { return !p.HasExited; }
        catch { return false; }
    }

    private static Process? Launch(string exe, string args, string workDir)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = workDir,
                UseShellExecute  = false,
                CreateNoWindow   = true,
            };
            return Process.Start(psi);
        }
        catch { return null; }
    }

    private static void KillProcess(Process? p, string? byName)
    {
        if (IsAlive(p))
            try { p!.Kill(entireProcessTree: true); } catch { }

        if (byName != null)
            foreach (var pr in Process.GetProcessesByName(byName))
                try { pr.Kill(entireProcessTree: true); } catch { }
    }

    // ─── Helpers réseau ───────────────────────────────────────────────────────
    private static bool IsPortListening(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                                     .GetActiveTcpListeners()
                                     .Any(ep => ep.Port == port);
        }
        catch { return false; }
    }

    private static void KillPort(int port)
    {
        // On tue les processus qui écoutent sur ce port via netstat
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("cmd", $"/c netstat -ano | findstr LISTENING | findstr \":{port} \"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                }
            };
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // Format: Proto  LocalAddr  ForeignAddr  State  PID
                if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid) && pid > 4)
                    try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }
    }

    // ─── Recherche de pythonw.exe ─────────────────────────────────────────────
    private void FindPythonW()
    {
        // 1. PATH
        foreach (var name in new[] { "pythonw", "python" })
        {
            var found = WhereExe(name);
            if (found != null)
            {
                // Préférer pythonw.exe dans le même dossier
                var pw = Path.Combine(Path.GetDirectoryName(found)!, "pythonw.exe");
                _pythonW = File.Exists(pw) ? pw : found;
                return;
            }
        }

        // 2. %LOCALAPPDATA%\Programs\Python\Python3xx\
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pyRoot   = Path.Combine(localApp, "Programs", "Python");
        if (Directory.Exists(pyRoot))
        {
            foreach (var dir in Directory.GetDirectories(pyRoot).OrderByDescending(d => d))
            {
                var pw = Path.Combine(dir, "pythonw.exe");
                if (File.Exists(pw)) { _pythonW = pw; return; }
            }
        }

        // 3. Chemin écrit par setup dans runtime_config.json
        var cfg = Path.Combine(_base, "runtime_config.json");
        if (File.Exists(cfg))
        {
            foreach (var line in File.ReadAllLines(cfg))
            {
                // Ligne attendue :  "pythonw": "C:\\...\\pythonw.exe"
                if (line.Contains("\"pythonw\""))
                {
                    var start = line.IndexOf('"', line.IndexOf(':') + 1) + 1;
                    var end   = line.LastIndexOf('"');
                    if (end > start)
                    {
                        var path = line[start..end].Replace("\\\\", "\\");
                        if (File.Exists(path)) { _pythonW = path; return; }
                    }
                }
            }
        }
    }

    private static string? WhereExe(string name)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("cmd", $"/c where {name}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                }
            };
            p.Start();
            var first = p.StandardOutput.ReadLine()?.Trim();
            p.WaitForExit(3000);
            return (first != null && File.Exists(first)) ? first : null;
        }
        catch { return null; }
    }

    // ─── UI helpers ───────────────────────────────────────────────────────────
    private void ShowStatusPopup()
    {
        var lines = new[]
        {
            $"SysViewHardware : {(_hwRunning     ? "✓ En marche" : "✗ Arrêté")}  (port {PORT_HW})",
            $"Bridge          : {(_bridgeRunning ? "✓ En marche" : "✗ Arrêté")}  (port {PORT_BRIDGE})",
            $"Aether          : {(_aetherRunning ? "✓ En marche" : "✗ Arrêté")}  (port {PORT_AETHER})",
        };
        MessageBox.Show(string.Join("\n", lines), "SysView V6 — Statut",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Notify(string msg, ToolTipIcon icon)
    {
        _tray.ShowBalloonTip(3000, "SysView V6", msg, icon);
    }

    private void OpenLogs()
    {
        if (Directory.Exists(_logDir))
            Process.Start(new ProcessStartInfo { FileName = _logDir, UseShellExecute = true });
    }

    private void Quit()
    {
        _pollTimer.Stop();
        _tray.Visible = false;
        Application.Exit();
    }

    // ─── Icône tray générée dynamiquement (cercle coloré 16×16) ──────────────
    private static Icon MakeTrayIcon(Color fill)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(fill);
            g.FillEllipse(brush, 1, 1, 13, 13);
            using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1f);
            g.DrawEllipse(pen, 1, 1, 13, 13);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ─── Nettoyage ────────────────────────────────────────────────────────────
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _tray.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
