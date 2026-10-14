using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tweakora.App.Services;

public static class AppIconExtractor
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource? ExtractIcon(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        string cleanPath = filePath.Trim('"', ' ');
        if (_cache.TryGetValue(cleanPath, out var cached))
        {
            return cached;
        }

        try
        {
            if (!File.Exists(cleanPath))
            {
                _cache[cleanPath] = null;
                return null;
            }

            var shinfo = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(cleanPath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
            if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    bitmap.Freeze();
                    _cache[cleanPath] = bitmap;
                    return bitmap;
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch
        {
            // Graceful fallback
        }

        _cache[cleanPath] = null;
        return null;
    }
}
