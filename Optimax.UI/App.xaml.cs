using System.Windows;
using System.Windows.Threading;

namespace Optimax.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!IsAdministrator())
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "Optimax.UI.exe",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    System.Diagnostics.Process.Start(psi);
                    Shutdown();
                    return;
                }
                catch
                {
                    // User declined UAC elevation prompt
                }
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            System.AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private static bool IsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception.InnerException ?? e.Exception;
            MessageBox.Show($"Optimax UI Warning: {ex.Message}\n\nType: {ex.GetType().Name}", "Optimax UI", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is System.Exception ex)
            {
                MessageBox.Show($"Optimax UI Critical: {ex.Message}", "Optimax UI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
