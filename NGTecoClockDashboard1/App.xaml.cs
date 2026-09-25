using NGTecoClockDashboard;
using System.Windows;

namespace NGTecoClockDashboard1
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            LoginWindow login = new LoginWindow();
            bool? result = login.ShowDialog();

            if (result == true)
            {
                MainWindow main = new MainWindow();
                Application.Current.MainWindow = main;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                main.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}