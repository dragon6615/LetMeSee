using System.IO;
using System.Windows;

namespace LetMeSee;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            WriteStartupLog("Starting LetMeSee.");

            var app = new App
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };

            var imagePath = args.Length > 0 ? args[0] : null;
            var window = new MainWindow(imagePath);
            app.MainWindow = window;

            WriteStartupLog("Showing main window.");
            return app.Run(window);
        }
        catch (Exception ex)
        {
            WriteStartupLog(ex.ToString());
            MessageBox.Show(
                ex.Message,
                "LetMeSee startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
    }

    private static void WriteStartupLog(string message)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LetMeSee");
        Directory.CreateDirectory(directory);

        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        File.AppendAllText(Path.Combine(directory, "startup.log"), line);
    }
}
