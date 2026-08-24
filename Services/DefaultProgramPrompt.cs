using System.IO;
using System.Runtime.InteropServices;

namespace LetMeSee.Services;

/// <summary>
/// Windows 保護預設開啟程式（`UserChoice` 帶簽章的 `Hash`），程式不能自己寫。
/// 但可以請 Windows 跳出標準的「你要如何開啟這個檔案」對話框，由使用者按下「一律」，
/// 這是被支援的做法。
/// </summary>
public static class DefaultProgramPrompt
{
    private const int OaifAllowRegistration = 0x00000001;
    private const int OaifRegisterExtension = 0x00000002;
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    /// <summary>
    /// 對指定檔案跳出「開啟方式」對話框。使用者取消時回傳 false。
    /// </summary>
    public static bool Show(IntPtr ownerWindow, string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Image file does not exist.", filePath);
        }

        var info = new OpenAsInfo
        {
            FileName = filePath,
            FileClass = null,
            InFlags = OaifAllowRegistration | OaifRegisterExtension
        };

        var result = SHOpenWithDialog(ownerWindow, ref info);
        if (result == ErrorCancelled)
        {
            return false;
        }

        Marshal.ThrowExceptionForHR(result);
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string FileName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? FileClass;

        public int InFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr parentWindow, ref OpenAsInfo openAsInfo);
}
