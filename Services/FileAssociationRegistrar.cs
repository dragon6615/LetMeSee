using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LetMeSee.Services;

public static class FileAssociationRegistrar
{
    private const string AppName = "LetMeSee";
    private const string ProgId = "LetMeSee.Image";
    private const string ContextMenuKeyPath = @"Software\Classes\SystemFileAssociations\image\shell\LetMeSee";
    private const string RegisteredApplicationsKeyPath = @"Software\RegisteredApplications";

    private static readonly string[] Extensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".gif",
        ".webp",
        ".tif",
        ".tiff"
    ];

    public static bool IsRegistered()
    {
        using var openCommandKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        using var registeredApplicationsKey = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKeyPath);
        using var contextMenuKey = Registry.CurrentUser.OpenSubKey(ContextMenuKeyPath);

        return openCommandKey?.GetValue("") is string openCommand && !string.IsNullOrWhiteSpace(openCommand) ||
            registeredApplicationsKey?.GetValue(AppName) is string registeredApplication && !string.IsNullOrWhiteSpace(registeredApplication) ||
            contextMenuKey is not null;
    }

    public static void Register()
    {
        var exePath = Environment.ProcessPath ??
            Process.GetCurrentProcess().MainModule?.FileName ??
            throw new InvalidOperationException("Cannot resolve LetMeSee.exe path.");

        exePath = Path.GetFullPath(exePath);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("LetMeSee.exe does not exist.", exePath);
        }

        Register(exePath);
    }

    public static void Register(string exePath)
    {
        var appExeName = Path.GetFileName(exePath);
        var command = $"\"{exePath}\" \"%1\"";
        var applicationKeyPath = $@"Software\Classes\Applications\{appExeName}";
        var capabilitiesKeyPath = $@"{applicationKeyPath}\Capabilities";

        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}", true))
        {
            progIdKey.SetValue("", "LetMeSee Image");
            progIdKey.SetValue("FriendlyTypeName", "LetMeSee Image");
        }

        using (var defaultIconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon", true))
        {
            defaultIconKey.SetValue("", exePath);
        }

        using (var openCommandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command", true))
        {
            openCommandKey.SetValue("", command);
        }

        using (var applicationKey = Registry.CurrentUser.CreateSubKey(applicationKeyPath, true))
        {
            applicationKey.SetValue("FriendlyAppName", AppName);
        }

        using (var applicationOpenCommandKey = Registry.CurrentUser.CreateSubKey($@"{applicationKeyPath}\shell\open\command", true))
        {
            applicationOpenCommandKey.SetValue("", command);
        }

        using (var supportedTypesKey = Registry.CurrentUser.CreateSubKey($@"{applicationKeyPath}\SupportedTypes", true))
        using (var fileAssociationsKey = Registry.CurrentUser.CreateSubKey($@"{capabilitiesKeyPath}\FileAssociations", true))
        {
            foreach (var extension in Extensions)
            {
                supportedTypesKey.SetValue(extension, "");
                fileAssociationsKey.SetValue(extension, ProgId);

                using var openWithProgIdsKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids", true);
                openWithProgIdsKey.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.Binary);
            }
        }

        using (var capabilitiesKey = Registry.CurrentUser.CreateSubKey(capabilitiesKeyPath, true))
        {
            capabilitiesKey.SetValue("ApplicationName", AppName);
            capabilitiesKey.SetValue("ApplicationDescription", "Lightweight image viewer");
        }

        using (var registeredApplicationsKey = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKeyPath, true))
        {
            registeredApplicationsKey.SetValue(AppName, $@"Software\Classes\Applications\{appExeName}\Capabilities");
        }

        using (var contextMenuKey = Registry.CurrentUser.CreateSubKey(ContextMenuKeyPath, true))
        {
            contextMenuKey.SetValue("MUIVerb", "Open with LetMeSee");
            contextMenuKey.SetValue("Icon", exePath);
        }

        using (var contextMenuCommandKey = Registry.CurrentUser.CreateSubKey($@"{ContextMenuKeyPath}\command", true))
        {
            contextMenuCommandKey.SetValue("", command);
        }

        NotifyShellAssociationsChanged();
    }

    public static void Unregister()
    {
        var exePath = Environment.ProcessPath ??
            Process.GetCurrentProcess().MainModule?.FileName ??
            throw new InvalidOperationException("Cannot resolve LetMeSee.exe path.");

        var appExeName = Path.GetFileName(exePath);
        var applicationKeyPath = $@"Software\Classes\Applications\{appExeName}";

        foreach (var extension in Extensions)
        {
            using var openWithProgIdsKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", true);
            openWithProgIdsKey?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(applicationKeyPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKeyPath, throwOnMissingSubKey: false);

        using (var registeredApplicationsKey = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKeyPath, true))
        {
            registeredApplicationsKey?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        NotifyShellAssociationsChanged();
    }

    private static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
