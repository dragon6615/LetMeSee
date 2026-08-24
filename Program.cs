using LetMeSee.Services;
using System.Windows;

namespace LetMeSee;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            DiagnosticLog.Write($"=== 啟動 LetMeSee，參數：{(args.Length > 0 ? string.Join(" ", args) : "(無)")}");

            var app = new App
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };

            var imagePath = args.Length > 0 ? args[0] : null;
            var window = new MainWindow(imagePath);
            app.MainWindow = window;

            var exitCode = app.Run(window);
            DiagnosticLog.Write($"=== 結束，exit code {exitCode}");
            return exitCode;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("啟動失敗", ex);
            MessageBox.Show(
                ex.Message,
                "LetMeSee 啟動錯誤",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
    }
}
