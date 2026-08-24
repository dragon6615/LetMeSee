using System.IO;

namespace LetMeSee.Services;

public sealed record ImageFormatGroup(string DisplayName, string Description, IReadOnlyList<string> Extensions);

/// <summary>
/// The one place that lists the image formats LetMeSee handles. Browsing, the open dialog,
/// the About box, and file association registration all read from here.
/// </summary>
public static class SupportedImageFormats
{
    public static IReadOnlyList<ImageFormatGroup> Groups { get; } =
    [
        new("JPEG", "最常見的相片格式，Windows 內建支援。", [".jpg", ".jpeg"]),
        new("PNG", "無失真壓縮，支援透明背景，Windows 內建支援。", [".png"]),
        new("BMP", "Windows 點陣圖，未壓縮、檔案較大。", [".bmp"]),
        new("GIF", "支援動畫，LetMeSee 會依檔案內的循環次數播放。", [".gif"]),
        new("WebP", "網頁常用的壓縮格式，Windows 10 之後內建支援。", [".webp"]),
        new("TIFF", "印刷與掃描常用，可包含多個影格。", [".tif", ".tiff"]),
        new("RAW", "相機原始檔，需要從 Microsoft Store 安裝「Windows 原始影像擴充功能」才能顯示。", [".cr2", ".cr3", ".nef", ".arw", ".raf", ".orf", ".rw2", ".dng"]),
        new("HEIF / HEIC", "iPhone 預設的相片格式，需要安裝「HEVC 影像擴充功能」才能顯示。", [".heic", ".heif"]),
    ];

    public static IReadOnlyList<string> Extensions { get; } = Groups.SelectMany(group => group.Extensions).ToArray();

    public static string OpenFileDialogFilter { get; } =
        $"圖片|{string.Join(";", Extensions.Select(extension => $"*{extension}"))}|所有檔案|*.*";

    private static readonly HashSet<string> ExtensionLookup = new(Extensions, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupportedFile(string path)
    {
        return ExtensionLookup.Contains(Path.GetExtension(path));
    }
}
