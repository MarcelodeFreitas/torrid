namespace Torrid.App.Notifications;

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

/// <summary>
/// Keeps a Start Menu shortcut that carries Torrid's AppUserModelID. Windows silently drops an unpackaged app's toasts
/// unless such a shortcut exists (proven in tools/ToastProbe/FINDINGS.md); the registry registration done by the toast
/// toolkit is not enough. Interop ported from tools/ToastProbe/ShortcutHelper.cs.
/// </summary>
public static class StartMenuShortcut
{
    public enum Outcome { Created, Unchanged, Recreated, Failed }

    // PKEY_AppUserModel_ID (System.AppUserModel.ID).
    private static readonly Guid AppUserModelFormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint AppUserModelIdPropertyId = 5;
    private const ushort VtLpwstr = 31;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs",
        AppIdentity.ShortcutName);

    /// <summary>
    /// Makes sure <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\{AppIdentity.ShortcutName}</c> points at this exe and
    /// carries <see cref="AppIdentity.Aumid"/>. Idempotent and never throws.
    /// </summary>
    public static Outcome EnsureForToasts()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            Debug.WriteLine("StartMenuShortcut: no process path; toasts may not be delivered.");
            return Outcome.Failed;
        }

        return Ensure(DefaultPath, exe, AppIdentity.Aumid);
    }

    /// <summary>
    /// Creates the shortcut at <paramref name="shortcutPath"/> if missing. An existing shortcut is read back (target via
    /// IShellLinkW.GetPath, id via IPropertyStore.GetValue) and left untouched when both match: rewriting it on every
    /// start would make Windows re-index it, and toasts sent while a shortcut is being (re)indexed can be dropped.
    /// It is recreated when the exe moved or the id differs (e.g. a Debug shortcut name reused by another build).
    /// </summary>
    public static Outcome Ensure(string shortcutPath, string targetExe, string appUserModelId)
    {
        try
        {
            string? currentTarget = null;
            string? currentId = null;
            var existed = File.Exists(shortcutPath);
            if (existed &&
                TryRead(shortcutPath, out currentTarget, out currentId) &&
                string.Equals(Normalize(currentTarget), Normalize(targetExe), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(currentId, appUserModelId, StringComparison.Ordinal))
            {
                return Outcome.Unchanged;
            }

            // No delete first: IPersistFile.Save overwrites, so a failed Create never removes a working shortcut.
            Create(shortcutPath, targetExe, appUserModelId);
            return existed ? Outcome.Recreated : Outcome.Created;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"StartMenuShortcut: could not ensure '{shortcutPath}': {ex}");
            return Outcome.Failed;
        }
    }

    /// <summary>Reads a shortcut's target path and AppUserModelID (null when it carries none). Never throws.</summary>
    public static bool TryRead(string shortcutPath, out string? targetPath, out string? appUserModelId)
    {
        targetPath = null;
        appUserModelId = null;
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistFile)link).Load(shortcutPath, 0 /* STGM_READ */);

            var buffer = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            targetPath = buffer.ToString();

            var key = new PropertyKey(AppUserModelFormatId, AppUserModelIdPropertyId);
            ((IPropertyStore)link).GetValue(ref key, out var value);
            try
            {
                if (value.VarType == VtLpwstr && value.PointerValue != IntPtr.Zero)
                {
                    appUserModelId = Marshal.PtrToStringUni(value.PointerValue);
                }
            }
            finally
            {
                PropVariantClear(ref value);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"StartMenuShortcut: could not read '{shortcutPath}': {ex.Message}");
            return false;
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
        }
    }

    private static void Create(string shortcutPath, string targetExe, string appUserModelId)
    {
        var link = new ShellLinkCoClass();
        try
        {
            var shellLink = (IShellLinkW)link;
            shellLink.SetPath(targetExe);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(targetExe) ?? "");

            var store = (IPropertyStore)link;
            var key = new PropertyKey(AppUserModelFormatId, AppUserModelIdPropertyId);
            var value = PropVariant.FromString(appUserModelId);
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                PropVariantClear(ref value);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pv);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey
    {
        public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
        public readonly Guid FormatId;
        public readonly uint PropertyId;
    }

    // Size = 24: a native PROPVARIANT is 24 bytes on x64. GetValue writes the whole struct, so a smaller managed
    // buffer would be overrun. Only the type tag and the pointer member are used.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr PointerValue;

        public static PropVariant FromString(string value) =>
            new() { VarType = VtLpwstr, PointerValue = Marshal.StringToCoTaskMemUni(value) };
    }
}
