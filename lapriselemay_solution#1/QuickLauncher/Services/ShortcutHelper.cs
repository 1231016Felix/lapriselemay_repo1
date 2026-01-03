using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace QuickLauncher.Services;

public class ShortcutInfo
{
    public string TargetPath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
}

public static class ShortcutHelper
{
    public static ShortcutInfo? ResolveShortcut(string shortcutPath)
    {
        if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;
        
        try
        {
            var link = (IShellLink)new ShellLink();
            var file = (IPersistFile)link;
            
            file.Load(shortcutPath, 0);
            link.Resolve(IntPtr.Zero, SLR_FLAGS.SLR_NO_UI | SLR_FLAGS.SLR_ANY_MATCH);
            
            var targetPath = new StringBuilder(260);
            var data = new WIN32_FIND_DATAW();
            link.GetPath(targetPath, targetPath.Capacity, ref data, SLGP_FLAGS.SLGP_RAWPATH);
            
            var description = new StringBuilder(1024);
            link.GetDescription(description, description.Capacity);
            
            var arguments = new StringBuilder(1024);
            link.GetArguments(arguments, arguments.Capacity);
            
            var workingDir = new StringBuilder(260);
            link.GetWorkingDirectory(workingDir, workingDir.Capacity);
            
            return new ShortcutInfo
            {
                TargetPath = targetPath.ToString(),
                Description = description.ToString(),
                Arguments = arguments.ToString(),
                WorkingDirectory = workingDir.ToString()
            };
        }
        catch
        {
            return null;
        }
    }
    
    #region COM Interop
    
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }
    
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, ref WIN32_FIND_DATAW pfd, SLGP_FLAGS fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, SLR_FLAGS fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
    
    [Flags]
    private enum SLR_FLAGS
    {
        SLR_NO_UI = 0x0001,
        SLR_ANY_MATCH = 0x0002,
        SLR_UPDATE = 0x0004,
        SLR_NOUPDATE = 0x0008,
        SLR_NOSEARCH = 0x0010,
        SLR_NOTRACK = 0x0020,
        SLR_NOLINKINFO = 0x0040,
        SLR_INVOKE_MSI = 0x0080
    }
    
    [Flags]
    private enum SLGP_FLAGS
    {
        SLGP_SHORTPATH = 0x0001,
        SLGP_UNCPRIORITY = 0x0002,
        SLGP_RAWPATH = 0x0004
    }
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }
    
    #endregion
}
