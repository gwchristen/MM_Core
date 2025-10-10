
using MMCore.Views;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace MMCore
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Call the base once
            base.OnStartup(e);

            // (Optional) make shutdown behavior explicit
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Create & show exactly one window
            var win = new MainWindow();
            this.MainWindow = win;
            win.Show();

            // Global exception hooks

            AppDomain.CurrentDomain.UnhandledException += (s, args2) =>
            {
                string message = args2.ExceptionObject is Exception ex
                    ? ex.ToString()
                    : $"Non-exception error: {args2.ExceptionObject?.ToString() ?? "Unknown"}";

                MessageBox.Show(
                    message,
                    "Unhandled Domain Exception",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };

            TaskScheduler.UnobservedTaskException += (s, args3) =>
            {
                MessageBox.Show(
                    args3.Exception.ToString(),
                    "Unobserved Task Exception",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args3.SetObserved();
            };


            // MaterialDesign theme (v5+)
            //var ph = new PaletteHelper();
            //var theme = ph.GetTheme();
            //theme.SetDarkTheme();                 // or theme.SetDarkTheme();
            //ph.SetTheme(theme);

            //new MainWindow().Show();
        }
    }
}