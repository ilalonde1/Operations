#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Email;

internal sealed class FolderPickerService
{
    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;

    private const int BFFM_INITIALIZED = 1;
    private const uint BFFM_SETSELECTIONW = 0x0467;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    private delegate int BrowseCallbackProc(IntPtr hwnd, uint uMsg, IntPtr lParam, IntPtr lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO bi);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    private static string? _initialPath;
    private static BrowseCallbackProc? _callback;

    private readonly ILogger<FolderPickerService> _logger;

    public FolderPickerService(ILogger<FolderPickerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string? PickFolder(string promptTitle, string? initialFolder)
    {
        _logger.LogDebug("Opening folder picker with initial folder {InitialFolder}.", initialFolder);

        _initialPath = !string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder)
            ? initialFolder
            : null;
        _callback = new BrowseCallbackProc(BrowseCallback);

        IntPtr displayNamePtr = IntPtr.Zero;
        IntPtr pidl = IntPtr.Zero;

        try
        {
            displayNamePtr = Marshal.AllocHGlobal(260 * Marshal.SystemDefaultCharSize);

            var bi = new BROWSEINFO
            {
                hwndOwner = IntPtr.Zero,
                pidlRoot = IntPtr.Zero,
                pszDisplayName = displayNamePtr,
                lpszTitle = promptTitle,
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
                lpfn = Marshal.GetFunctionPointerForDelegate(_callback),
                lParam = IntPtr.Zero,
                iImage = 0
            };

            pidl = SHBrowseForFolder(ref bi);
            if (pidl == IntPtr.Zero)
                return null;

            var sb = new StringBuilder(260);
            bool ok = SHGetPathFromIDList(pidl, sb);
            if (!ok)
                return null;

            string path = sb.ToString();
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return path;
        }
        finally
        {
            if (pidl != IntPtr.Zero)
                CoTaskMemFree(pidl);

            if (displayNamePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(displayNamePtr);

            _callback = null;
            _initialPath = null;
        }
    }

    private static int BrowseCallback(IntPtr hwnd, uint uMsg, IntPtr lParam, IntPtr lpData)
    {
        if (uMsg == BFFM_INITIALIZED && !string.IsNullOrEmpty(_initialPath))
        {
            SendMessage(hwnd, BFFM_SETSELECTIONW, new IntPtr(1), _initialPath);
        }

        return 0;
    }
}
