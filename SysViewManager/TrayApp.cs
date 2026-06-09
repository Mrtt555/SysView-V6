// =============================================================
// TrayApp — icône tray WinForms
// Affiche le statut LHM / Bridge (in-process) / Aether (subprocess).
// =============================================================
using System.Diagnostics;
using System.Windows.Forms;

namespace SysViewManager;

public sealed class TrayApp : ApplicationContext
{
    // ─── Services passés par référence ────────────────────────────────────────
    private readonly HardwareService _hw;
    private readonly WeatherService  _weather;
    private readonly AetherProcess   _aether;
    private readonly string          _logDir;
    private readonly SynchronizationContext _syncCtx;

    // ─── UI ───────────────────────────────────────────────────────────────────
    private readonly NotifyIcon         _tray;
    private readonly ContextMenuStrip   _menu;
    private readonly ToolStripMenuItem  _miHw;
    private readonly ToolStripMenuItem  _miBridge;
    private readonly ToolStripMenuItem  _miAether;
    private readonly ToolStripMenuItem  _miRestartAether;
    private readonly System.Windows.Forms.Timer _timer;

    public TrayApp(HardwareService hw, WeatherService weather,
                   AetherProcess aether, string logDir)
    {
        _hw      = hw;
        _weather = weather;
        _aether  = aether;
        _logDir  = logDir;
        _syncCtx = SynchronizationContext.Current
                   ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();

        // ── Menu contextuel ───────────────────────────────────────────────────
        _menu = new ContextMenuStrip();

        var miTitle = new ToolStripMenuItem("SysView V6") { Enabled = false };
        miTitle.Font = new Font(SystemFonts.MenuFont ?? new Font("Segoe UI", 9f), FontStyle.Bold);
        _menu.Items.Add(miTitle);
        _menu.Items.Add(new ToolStripSeparator());

        _miHw = new ToolStripMenuItem("○ Hardware  [init...]") { Enabled = false };
        _menu.Items.Add(_miHw);

        _miBridge = new ToolStripMenuItem("● Bridge  [Port 5001]") { Enabled = false };
        _menu.Items.Add(_miBridge);

        _miAether = new ToolStripMenuItem("○ Aether  [Démarrer]");
        _miAether.Click += (_, _) => ToggleAether();
        _menu.Items.Add(_miAether);

        _menu.Items.Add(new ToolStripSeparator());

        _miRestartAether = new ToolStripMenuItem("↺  Redémarrer Aether");
        _miRestartAether.Click += (_, _) => Task.Run(_aether.Restart)
            .ContinueWith(_ => _syncCtx.Post(__ => RefreshStatus(), null));
        _menu.Items.Add(_miRestartAether);

        _menu.Items.Add(new ToolStripSeparator());

        var miLogs = new ToolStripMenuItem("📁  Ouvrir les logs");
        miLogs.Click += (_, _) => OpenLogs();
        _menu.Items.Add(miLogs);

        var miDocs = new ToolStripMenuItem("🌐  Bridge API /docs");
        miDocs.Click += (_, _) => Process.Start(new ProcessStartInfo
            { FileName = "http://127.0.0.1:5001/docs", UseShellExecute = true });
        _menu.Items.Add(miDocs);

        _menu.Items.Add(new ToolStripSeparator());

        var miQuit = new ToolStripMenuItem("✕  Quitter");
        miQuit.Click += (_, _) => Quit();
        _menu.Items.Add(miQuit);

        // ── Icône tray ────────────────────────────────────────────────────────
        _tray = new NotifyIcon
        {
            Text             = "SysView V6",
            Icon             = MakeTrayIcon(Color.Gray),
            Visible          = true,
            ContextMenuStrip = _menu,
        };
        _tray.DoubleClick += (_, _) => ShowStatusPopup();

        // ── Timer de polling (5 s) ────────────────────────────────────────────
        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();

        RefreshStatus();
    }

    // ─── Rafraîchissement ─────────────────────────────────────────────────────

    private void RefreshStatus()
    {
        var snap      = _hw.GetSnapshot();
        var weather   = _weather.GetData();
        bool aetherOk = _aether.IsRunning;

        // ── Labels ──
        _miHw.Text = snap.LhmOnline
            ? $"● Hardware  [CPU {snap.CpuUsage?.ToString("F0") ?? "—"}%  " +
              $"{(snap.CpuTemp.HasValue ? snap.CpuTemp.Value.ToString("F0") + "°C" : "—")}]"
            : "○ Hardware  [dégradé — admin requis ?]";

        _miBridge.Text = "● Bridge  [Port 5001]";

        _miAether.Text = aetherOk
            ? $"● Aether{(weather.Temp.HasValue ? "  " + weather.Temp.Value.ToString("F1") + "°C" : "")}  [Arrêter]"
            : "○ Aether  [Démarrer]";

        _miRestartAether.Enabled = aetherOk;

        // ── Couleur icône ──
        Color c = (snap.LhmOnline && aetherOk)  ? Color.FromArgb(0, 200, 100)   // tout vert
                : (snap.LhmOnline || aetherOk)  ? Color.Orange                  // partiel
                :                                  Color.FromArgb(200, 50, 50);  // rouge

        var old = _tray.Icon;
        _tray.Icon = MakeTrayIcon(c);
        old?.Dispose();

        string cpuStr = snap.CpuUsage.HasValue ? $"{snap.CpuUsage.Value:F0}%" : "—";
        string ramStr = snap.RamUsage.HasValue ? $"{snap.RamUsage.Value:F0}%" : "—";
        _tray.Text = $"SysView V6 — CPU:{cpuStr}  RAM:{ramStr}  Aether:{(aetherOk ? "✓" : "✗")}";
    }

    // ─── Actions ──────────────────────────────────────────────────────────────

    private void ToggleAether()
    {
        if (_aether.IsRunning) Task.Run(_aether.Stop);
        else                   Task.Run(_aether.Start);
        Task.Delay(2000).ContinueWith(_ => _syncCtx.Post(__ => RefreshStatus(), null));
    }

    private void ShowStatusPopup()
    {
        var snap = _hw.GetSnapshot();
        var w    = _weather.GetData();
        MessageBox.Show(
            $"Hardware (LHM) : {(snap.LhmOnline ? "✓ OK" : "✗ Dégradé (lancer en admin ?)")}\n" +
            $"Bridge         : ✓ Port 5001\n" +
            $"Aether         : {(_aether.IsRunning ? "✓ Port 8001" : "✗ Arrêté")}\n\n" +
            $"CPU   : {snap.CpuUsage?.ToString("F1") ?? "—"}%  " +
                    $"{(snap.CpuTemp.HasValue ? snap.CpuTemp.Value.ToString("F1") + "°C" : "—")}\n" +
            $"GPU   : {snap.GpuUsage?.ToString("F1") ?? "—"}%  " +
                    $"{(snap.GpuTemp.HasValue ? snap.GpuTemp.Value.ToString("F1") + "°C" : "—")}\n" +
            $"RAM   : {snap.RamUsage?.ToString("F1") ?? "—"}%  " +
                    $"({snap.RamUsedMb} Mo / {snap.RamTotalMb} Mo)\n" +
            $"Météo : {(w.Temp.HasValue ? w.Temp.Value.ToString("F1") + "°C" : "—")}",
            "SysView V6 — Statut", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenLogs()
    {
        if (Directory.Exists(_logDir))
            Process.Start(new ProcessStartInfo { FileName = _logDir, UseShellExecute = true });
    }

    private void Quit()
    {
        _timer.Stop();
        _tray.Visible = false;
        Application.Exit();
    }

    // ─── Icône tray (cercle coloré généré en mémoire) ─────────────────────────

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

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _tray.Dispose(); _menu.Dispose(); }
        base.Dispose(disposing);
    }
}
