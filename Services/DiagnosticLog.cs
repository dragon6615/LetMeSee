using System.IO;
using System.Security;

namespace LetMeSee.Services;

/// <summary>
/// Append-only diagnostic log at %LOCALAPPDATA%\LetMeSee\letmesee.log. Logging must never
/// affect the app, so every failure here is swallowed.
/// </summary>
public static class DiagnosticLog
{
    private const long MaxLogBytes = 256 * 1024;

    private static readonly object WriteLock = new();

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LetMeSee");

    public static string FilePath { get; } = Path.Combine(Directory, "letmesee.log");

    public static void Write(string message)
    {
        try
        {
            lock (WriteLock)
            {
                System.IO.Directory.CreateDirectory(Directory);
                RollIfTooLarge();
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }

    public static void Write(string message, Exception error)
    {
        Write($"{message} :: {error.GetType().Name}: {error.Message}");
    }

    private static void RollIfTooLarge()
    {
        var logFile = new FileInfo(FilePath);
        if (logFile.Exists && logFile.Length > MaxLogBytes)
        {
            logFile.MoveTo(FilePath + ".old", overwrite: true);
        }
    }
}
