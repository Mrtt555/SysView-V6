// =============================================================
// AetherProcess — gestion du sous-processus Aether (Python)
// Aether est un projet séparé → on le lance via pythonw / uvicorn.
// =============================================================
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace SysViewManager;

public sealed class AetherProcess : IDisposable
{
    private readonly string _dir;      // chemin vers Aether/
    private readonly string _pythonW;  // chemin vers pythonw.exe
    private Process? _proc;
    private readonly object _mu = new();

    public AetherProcess(string aetherDir, string pythonW)
    {
        _dir    = aetherDir;
        _pythonW = pythonW;
    }

    // ─── État ─────────────────────────────────────────────────────────────────

    public bool IsRunning
    {
        get
        {
            lock (_mu)
            {
                try { if (_proc != null && !_proc.HasExited) return true; } catch { }
                return IsPortListening(8001);
            }
        }
    }

    // ─── Contrôle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        lock (_mu)
        {
            if (!Directory.Exists(_dir))                              return;
            if (!File.Exists(Path.Combine(_dir, "main.py")))          return;
            if (string.IsNullOrEmpty(_pythonW))                        return;

            KillPort(8001);
            try
            {
                _proc = Process.Start(new ProcessStartInfo(
                    _pythonW,
                    "-m uvicorn main:app --host 127.0.0.1 --port 8001 --log-level error")
                {
                    WorkingDirectory = _dir,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                });
            }
            catch { _proc = null; }
        }
    }

    public void Stop()
    {
        lock (_mu)
        {
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch { }
            _proc = null;
        }
        KillPort(8001);
    }

    public void Restart() { Stop(); Thread.Sleep(500); Start(); }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsPortListening(int port)
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port); }
        catch { return false; }
    }

    private static void KillPort(int port)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("cmd",
                    $"/c netstat -ano | findstr LISTENING | findstr \":{port} \"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                }
            };
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid) && pid > 4)
                    try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }
    }

    public void Dispose() => Stop();
}
