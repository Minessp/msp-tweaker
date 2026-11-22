using System.Windows;

namespace MspTweaker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, a) =>
        {
            MessageBox.Show(a.Exception.GetBaseException().Message, "Msp Tweaker", MessageBoxButton.OK, MessageBoxImage.Error);
            a.Handled = true;
            if (MainWindow is not { IsVisible: true }) Shutdown(1); // no window means no way to quit
        };

        if (e.Args.Contains("--selftest"))
        {
            var failures = SelfTest.Run();
            MessageBox.Show(failures.Count == 0 ? "SelfTest: all good." : "SelfTest failed:\n" + string.Join("\n", failures), "Msp Tweaker");
            Shutdown(failures.Count);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
