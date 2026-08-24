using LetMeSee.Services;
using System.Windows;
using System.Windows.Threading;

namespace LetMeSee;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Image decoding can fail in ways WPF surfaces on the dispatcher, including
        // OutOfMemoryException on very large files. Report it instead of killing the viewer.
        DiagnosticLog.Write($"未處理的例外：{e.Exception}");

        MessageBox.Show(
            $"發生未預期的錯誤：{Environment.NewLine}{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"詳細內容已寫入：{Environment.NewLine}{DiagnosticLog.FilePath}",
            "LetMeSee",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
