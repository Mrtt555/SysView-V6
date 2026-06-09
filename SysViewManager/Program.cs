using System.Threading;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace SysViewManager;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // Une seule instance
        using var mutex = new Mutex(true, "Global\\SysViewManagerMutex", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "SysView Manager est déjà en cours d'exécution.\nVérifiez l'icône dans la barre système.",
                "SysView V6",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var ctx = new TrayApp();
        Application.Run(ctx);           // boucle message sans fenêtre principale
    }
}
