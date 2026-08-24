using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LetMeSee.Services;

public static class FileAssociationRegistrar
{
    private const string AppName = "LetMeSee";
    private const string ProgId = "LetMeSee.Image";
    private const string AppExeName = "LetMeSee.exe";
    private const string ContextMenuKeyPath = @"Software\Classes\SystemFileAssociations\image\shell\LetMeSee";
    private const string RegisteredApplicationsKeyPath = @"Software\RegisteredApplications";

    /// <summary>
    /// Extensions currently listed under the LetMeSee ProgID in the user's Open With metadata.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredExtensions()
    {
        var registered = SupportedImageFormats.Extensions.Where(IsExtensionRegistered).ToArray();
        DiagnosticLog.Write($"讀取檔案關聯：已註冊 {registered.Length} 種 [{string.Join(" ", registered)}]，" +
            $"右鍵選單={IsImageContextMenuRegistered()}，指向={GetRegisteredExecutablePath() ?? "(無)"}");
        return registered;
    }

    public static bool IsExtensionRegistered(string extension)
    {
        using var openWithProgIdsKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids");
        return openWithProgIdsKey?.GetValue(ProgId) is not null;
    }

    /// <summary>
    /// 目前用來開啟這個副檔名的 ProgID，沒有設定過則為 null。這個值由 Windows 維護
    /// （`UserChoice`，帶簽章保護），程式只能讀，不能寫。
    /// </summary>
    public static string? GetDefaultHandlerProgId(string extension)
    {
        using var userChoiceKey = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice");
        return userChoiceKey?.GetValue("ProgId") as string;
    }

    public static bool IsDefaultHandler(string extension)
    {
        return IsOurProgId(GetDefaultHandlerProgId(extension));
    }

    /// <summary>
    /// 我們註冊的 ProgID 是 <c>LetMeSee.Image</c>，但使用者若是透過「開啟方式 &gt; 瀏覽到執行檔」
    /// 指定的，Windows 會記成 <c>Applications\LetMeSee.exe</c>。兩者都算是 LetMeSee。
    /// </summary>
    private static bool IsOurProgId(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return false;
        }

        if (string.Equals(progId, ProgId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string applicationsPrefix = @"Applications\";
        return progId.StartsWith(applicationsPrefix, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(progId[applicationsPrefix.Length..], AppExeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 目前預設開啟程式的顯示名稱，沒有設定過則為 null。
    /// </summary>
    public static string? DescribeDefaultHandler(string extension)
    {
        var defaultProgId = GetDefaultHandlerProgId(extension);
        if (string.IsNullOrWhiteSpace(defaultProgId))
        {
            return null;
        }

        if (IsOurProgId(defaultProgId))
        {
            return AppName;
        }

        using var classKey = Registry.ClassesRoot.OpenSubKey(defaultProgId);
        var description = classKey?.GetValue("") as string;

        // 描述字串常是 "@C:\path\app.exe,-101" 這種間接資源字串，直接顯示會很醜，
        // 這種情況退回 ProgID 本身，至少看得出是哪套軟體。
        return string.IsNullOrWhiteSpace(description) || description.StartsWith('@')
            ? defaultProgId
            : description;
    }

    public static bool IsImageContextMenuRegistered()
    {
        using var contextMenuKey = Registry.CurrentUser.OpenSubKey($@"{ContextMenuKeyPath}\command");
        return contextMenuKey?.GetValue("") is string command && !string.IsNullOrWhiteSpace(command);
    }

    /// <summary>
    /// Executable the current registration points at, or null when nothing is registered. Lets the
    /// caller notice that an older copy of LetMeSee owns the association.
    /// </summary>
    public static string? GetRegisteredExecutablePath()
    {
        using var openCommandKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return openCommandKey?.GetValue("") is string command
            ? ExtractExecutablePath(command)
            : null;
    }

    public static string GetCurrentExecutablePath()
    {
        var exePath = Environment.ProcessPath ??
            Process.GetCurrentProcess().MainModule?.FileName ??
            throw new InvalidOperationException("Cannot resolve LetMeSee.exe path.");

        return Path.GetFullPath(exePath);
    }

    /// <summary>
    /// Makes the registry match the requested selection: selected extensions are registered against
    /// the running executable, everything else is removed. An empty selection with no context menu
    /// removes the registration entirely.
    /// </summary>
    public static void Apply(IReadOnlyCollection<string> extensions, bool addImageContextMenu)
    {
        var exePath = GetCurrentExecutablePath();
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("LetMeSee.exe does not exist.", exePath);
        }

        DiagnosticLog.Write($"套用檔案關聯：勾選 {extensions.Count} 種 [{string.Join(" ", extensions)}]，" +
            $"右鍵選單={addImageContextMenu}，執行檔={exePath}");

        if (extensions.Count == 0 && !addImageContextMenu)
        {
            Unregister();
            return;
        }

        Apply(exePath, extensions, addImageContextMenu);
        DiagnosticLog.Write($"套用完成：實際註冊 [{string.Join(" ", SupportedImageFormats.Extensions.Where(IsExtensionRegistered))}]");
    }

    public static void Unregister()
    {
        var exePath = Environment.ProcessPath ??
            Process.GetCurrentProcess().MainModule?.FileName ??
            throw new InvalidOperationException("Cannot resolve LetMeSee.exe path.");

        var appExeName = Path.GetFileName(exePath);
        var applicationKeyPath = $@"Software\Classes\Applications\{appExeName}";

        foreach (var extension in SupportedImageFormats.Extensions)
        {
            RemoveExtensionFromOpenWith(extension);
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(applicationKeyPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKeyPath, throwOnMissingSubKey: false);

        using (var registeredApplicationsKey = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKeyPath, true))
        {
            registeredApplicationsKey?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        NotifyShellAssociationsChanged();
        DiagnosticLog.Write("已移除所有檔案關聯。");
    }

    private static void Apply(string exePath, IReadOnlyCollection<string> extensions, bool addImageContextMenu)
    {
        var appExeName = Path.GetFileName(exePath);
        var command = $"\"{exePath}\" \"%1\"";
        var applicationKeyPath = $@"Software\Classes\Applications\{appExeName}";
        var capabilitiesKeyPath = $@"{applicationKeyPath}\Capabilities";
        var selectedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

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

        // Rebuild the format lists from scratch so extensions the user just cleared disappear.
        Registry.CurrentUser.DeleteSubKeyTree($@"{applicationKeyPath}\SupportedTypes", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{capabilitiesKeyPath}\FileAssociations", throwOnMissingSubKey: false);

        using (var supportedTypesKey = Registry.CurrentUser.CreateSubKey($@"{applicationKeyPath}\SupportedTypes", true))
        using (var fileAssociationsKey = Registry.CurrentUser.CreateSubKey($@"{capabilitiesKeyPath}\FileAssociations", true))
        {
            foreach (var extension in SupportedImageFormats.Extensions)
            {
                if (!selectedExtensions.Contains(extension))
                {
                    if (IsExtensionRegistered(extension))
                    {
                        DiagnosticLog.Write($"  移除 {extension}");
                    }

                    RemoveExtensionFromOpenWith(extension);
                    continue;
                }

                if (!IsExtensionRegistered(extension))
                {
                    DiagnosticLog.Write($"  新增 {extension}");
                }

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

        if (addImageContextMenu)
        {
            using var contextMenuKey = Registry.CurrentUser.CreateSubKey(ContextMenuKeyPath, true);
            contextMenuKey.SetValue("MUIVerb", "用 LetMeSee 開啟");
            contextMenuKey.SetValue("Icon", exePath);

            using var contextMenuCommandKey = Registry.CurrentUser.CreateSubKey($@"{ContextMenuKeyPath}\command", true);
            contextMenuCommandKey.SetValue("", command);
        }
        else
        {
            Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKeyPath, throwOnMissingSubKey: false);
        }

        NotifyShellAssociationsChanged();
    }

    private static void RemoveExtensionFromOpenWith(string extension)
    {
        using var openWithProgIdsKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", true);
        openWithProgIdsKey?.DeleteValue(ProgId, throwOnMissingValue: false);
    }

    private static string? ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (command.StartsWith('"'))
        {
            var closingQuoteIndex = command.IndexOf('"', 1);
            return closingQuoteIndex > 1 ? command[1..closingQuoteIndex] : null;
        }

        var separatorIndex = command.IndexOf(' ');
        return separatorIndex > 0 ? command[..separatorIndex] : command;
    }

    private static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
