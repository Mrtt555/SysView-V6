// =============================================================
// TrayApp — icône tray WinForms
// Statuts : Hardware (LHM) / Bridge / Météo (in-process)
// Toggle démarrage automatique via tâche planifiée (admin).
// =============================================================
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SysViewManager;

public sealed class TrayApp : ApplicationContext
{
    // ─── Services ────────────────────────────────────────────────────────────
    private readonly HardwareService        _hw;
    private readonly WeatherService         _weather;
    private readonly string                 _dataDir;   // %AppData%\SysViewManager\
    private readonly SynchronizationContext _syncCtx;

    // ─── UI ───────────────────────────────────────────────────────────────────
    private readonly NotifyIcon        _tray;
    private readonly ContextMenuStrip  _menu;
    private readonly ToolStripMenuItem _miHw;
    private readonly ToolStripMenuItem _miBridge;
    private readonly ToolStripMenuItem _miWeather;
    private readonly ToolStripMenuItem _miAutoStart;
    private readonly System.Windows.Forms.Timer _timer;

    public TrayApp(HardwareService hw, WeatherService weather, string dataDir)
    {
        _hw      = hw;
        _weather = weather;
        _dataDir = dataDir;
        _syncCtx = SynchronizationContext.Current
                   ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();

        // ── Menu contextuel ───────────────────────────────────────────────────
        _menu = new ContextMenuStrip();

        var miTitle = new ToolStripMenuItem("SysView V6") { Enabled = false };
        miTitle.Font = new Font(SystemFonts.MenuFont ?? new Font("Segoe UI", 9f), FontStyle.Bold);
        _menu.Items.Add(miTitle);
        _menu.Items.Add(new ToolStripSeparator());

        // Statuts (non-cliquables)
        _miHw = new ToolStripMenuItem("○ Hardware  [init...]") { Enabled = false };
        _menu.Items.Add(_miHw);

        _miBridge = new ToolStripMenuItem("● Bridge  [Port 5001]") { Enabled = false };
        _menu.Items.Add(_miBridge);

        _miWeather = new ToolStripMenuItem("○ Météo  [en attente…]") { Enabled = false };
        _menu.Items.Add(_miWeather);

        _menu.Items.Add(new ToolStripSeparator());

        // Actualiser la météo
        var miRefreshWeather = new ToolStripMenuItem("↺  Actualiser la météo");
        miRefreshWeather.Click += (_, _) => {
            _weather.TriggerRefresh();
            miRefreshWeather.Enabled = false;
            Task.Delay(5000).ContinueWith(_ => _syncCtx.Post(__ => {
                miRefreshWeather.Enabled = true;
                RefreshStatus();
            }, null));
        };
        _menu.Items.Add(miRefreshWeather);

        // Démarrage automatique (toggle)
        _miAutoStart = new ToolStripMenuItem("⚡  Démarrage auto  [vérif…]");
        _miAutoStart.Click += (_, _) => ToggleAutoStart();
        _menu.Items.Add(_miAutoStart);

        _menu.Items.Add(new ToolStripSeparator());

        var miDataDir = new ToolStripMenuItem("📁  Dossier données (%AppData%)");
        miDataDir.Click += (_, _) => {
            if (Directory.Exists(_dataDir))
                Process.Start(new ProcessStartInfo { FileName = _dataDir, UseShellExecute = true });
        };
        _menu.Items.Add(miDataDir);

        var miDocs = new ToolStripMenuItem("🌐  Bridge API /docs");
        miDocs.Click += (_, _) => Process.Start(new ProcessStartInfo
            { FileName = "http://127.0.0.1:5001/docs", UseShellExecute = true });
        _menu.Items.Add(miDocs);

        _menu.Items.Add(new ToolStripSeparator());

        var miQuit = new ToolStripMenuItem("✕  Quitter");
        miQuit.Click += (_, _) => Quit();
        _menu.Items.Add(miQuit);

        // Rafraîchir le statut auto-start au moment où le menu s'ouvre
        _menu.Opening += (_, _) => RefreshAutoStartLabel();

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
        var snap       = _hw.GetSnapshot();
        var weather    = _weather.GetData();
        bool weatherOk = weather.Temp.HasValue;

        // ── Labels ──
        _miHw.Text = snap.LhmOnline
            ? $"● Hardware  [CPU {snap.CpuUsage?.ToString("F0") ?? "—"}%  " +
              $"{(snap.CpuTemp.HasValue ? snap.CpuTemp.Value.ToString("F0") + "°C" : "—")}]"
            : "○ Hardware  [dégradé — admin requis ?]";

        _miBridge.Text = "● Bridge  [Port 5001]";

        _miWeather.Text = weatherOk
            ? $"● Météo  [{weather.Temp!.Value:F1}°C  {weather.Wind?.ToString("F0") ?? "—"} km/h" +
              $"  {weather.AetherModel ?? "—"}]"
            : "○ Météo  [en attente…]";

        // ── Couleur icône ──
        Color c = (snap.LhmOnline && weatherOk) ? Color.FromArgb(0, 200, 100)
                : (snap.LhmOnline || weatherOk) ? Color.Orange
                :                                  Color.FromArgb(200, 50, 50);

        var old = _tray.Icon;
        _tray.Icon = MakeTrayIcon(c);
        old?.Dispose();

        string cpuStr   = snap.CpuUsage.HasValue ? $"{snap.CpuUsage.Value:F0}%" : "—";
        string ramStr   = snap.RamUsage.HasValue  ? $"{snap.RamUsage.Value:F0}%" : "—";
        string meteoStr = weatherOk ? $"{weather.Temp!.Value:F1}°C" : "⏳";
        _tray.Text = $"SysView V6 — CPU:{cpuStr}  RAM:{ramStr}  Météo:{meteoStr}";
    }

    private void RefreshAutoStartLabel()
    {
        bool on = Program.IsAutoStartRegistered();
        _miAutoStart.Text = on
            ? "⚡  Démarrage auto  [✓ Actif]"
            : "⚡  Démarrage auto  [✗ Désactivé]";
    }

    // ─── Toggle démarrage automatique ────────────────────────────────────────

    private void ToggleAutoStart()
    {
        bool current = Program.IsAutoStartRegistered();
        if (current)
            Program.UnregisterAutoStart();
        else
            Program.RegisterAutoStart();

        // Rafraîchir le label après la commande
        Task.Delay(1500).ContinueWith(_ =>
            _syncCtx.Post(__ => RefreshAutoStartLabel(), null));
    }

    // ─── Popup double-clic ────────────────────────────────────────────────────

    private void ShowStatusPopup()
    {
        var snap = _hw.GetSnapshot();
        var w    = _weather.GetData();
        bool autoOn = Program.IsAutoStartRegistered();
        MessageBox.Show(
            $"Hardware (LHM) : {(snap.LhmOnline ? "✓ OK" : "✗ Dégradé (admin requis ?)")}\n" +
            $"Bridge         : ✓ Port 5001\n" +
            $"Météo          : {(w.Temp.HasValue ? $"✓ {w.Temp.Value:F1}°C" : "⏳ En attente…")}\n" +
            $"Modèle météo   : {w.AetherModel ?? "—"}\n" +
            $"Démarrage auto : {(autoOn ? "✓ Actif (tâche planifiée)" : "✗ Désactivé")}\n\n" +
            $"CPU   : {snap.CpuUsage?.ToString("F1") ?? "—"}%  " +
                    $"{(snap.CpuTemp.HasValue ? snap.CpuTemp.Value.ToString("F1") + "°C" : "—")}\n" +
            $"GPU   : {snap.GpuUsage?.ToString("F1") ?? "—"}%  " +
                    $"{(snap.GpuTemp.HasValue ? snap.GpuTemp.Value.ToString("F1") + "°C" : "—")}\n" +
            $"RAM   : {snap.RamUsage?.ToString("F1") ?? "—"}%  " +
                    $"({snap.RamUsedMb} Mo / {snap.RamTotalMb} Mo)\n" +
            $"Vent  : {w.Wind?.ToString("F0") ?? "—"} km/h  Rafales : {w.WindGusts?.ToString("F0") ?? "—"} km/h\n" +
            $"IQA   : {w.Aqi?.ToString() ?? "—"}  ({w.AqiLabel})  Pollen : {w.Pollen?.ToString() ?? "—"} ({w.PollenLabel})\n\n" +
            $"Données : {_dataDir}",
            "SysView V6 — Statut", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Quit()
    {
        _timer.Stop();
        _tray.Visible = false;
        Application.Exit();
    }

    // ─── Icône tray (cercle coloré généré en mémoire) ─────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

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
        // GetHicon() alloue un HICON GDI que Icon.FromHandle() ne possède pas.
        // Clone() crée un Icon autonome ; DestroyIcon libère le handle d'origine.
        IntPtr hicon = bmp.GetHicon();
        try   { return (Icon)Icon.FromHandle(hicon).Clone(); }
        finally { DestroyIcon(hicon); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _tray.Dispose(); _menu.Dispose(); }
        base.Dispose(disposing);
    }
}
