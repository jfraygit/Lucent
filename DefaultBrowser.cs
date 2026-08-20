using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Lucent;

public static class DefaultBrowser
{
    public const string AppName = "Lucent";

    private const string ProgId = "LucentHTML";

    private const string ClientKey = @"Software\Clients\StartMenuInternet\" + AppName;

    private const string CapabilitiesKey = ClientKey + @"\Capabilities";
    private const string ClassKey = @"Software\Classes\" + ProgId;
    private const string CommandKey = ClassKey + @"\shell\open\command";
    private const string RegisteredApplications = @"Software\RegisteredApplications";

    private const string PreferenceKey = @"Software\" + AppName;
    private const string DismissedValue = "DefaultPromptDismissed";

    public static bool IsDefault => Handles("http") && Handles("https");

    private static bool Handles(string scheme)
    {
        if (Executable.Length == 0) return false;

        string handler = Handler(scheme);
        if (handler.Length == 0) return false;

        return string.Equals(handler, Executable, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFileName(handler), Path.GetFileName(Executable),
                             StringComparison.OrdinalIgnoreCase);
    }

    private static string Handler(string scheme)
    {
        try
        {
            var found = new StringBuilder(1024);
            int length = found.Capacity;

            return AssocQueryString(AssocIsProtocol, AssocExecutable, scheme, null, found, ref length) == 0
                ? found.ToString()
                : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public static bool IsRegistered
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(CommandKey);

            return key?.GetValue(null) as string == OpenCommand;
        }
    }

    public static bool Dismissed
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PreferenceKey);

            return key?.GetValue(DismissedValue) is int flag && flag != 0;
        }
    }

    public static void Dismiss()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(PreferenceKey);
            key.SetValue(DismissedValue, 1, RegistryValueKind.DWord);
        }
        catch (Exception)
        {
        }
    }

    private static string Executable => Environment.ProcessPath ?? string.Empty;

    private static string OpenCommand => $"\"{Executable}\" \"%1\"";

    public static bool Register()
    {
        if (Executable.Length == 0) return false;

        try
        {
            string icon = $"\"{Executable}\",0";

            using (RegistryKey client = Registry.CurrentUser.CreateSubKey(ClientKey))
            {
                client.SetValue(null, AppName);

                using (RegistryKey defaultIcon = client.CreateSubKey("DefaultIcon"))
                    defaultIcon.SetValue(null, icon);

                using (RegistryKey command = client.CreateSubKey(@"shell\open\command"))
                    command.SetValue(null, $"\"{Executable}\"");

                using RegistryKey capabilities = client.CreateSubKey("Capabilities");
                capabilities.SetValue("ApplicationName", AppName);
                capabilities.SetValue("ApplicationIcon", icon);
                capabilities.SetValue("ApplicationDescription",
                    "A small browser that blocks ads and keeps out of the way.");

                using (RegistryKey startMenu = capabilities.CreateSubKey("StartMenu"))
                    startMenu.SetValue("StartMenuInternet", AppName);

                using RegistryKey urls = capabilities.CreateSubKey("URLAssociations");
                urls.SetValue("http", ProgId);
                urls.SetValue("https", ProgId);
            }

            using (RegistryKey document = Registry.CurrentUser.CreateSubKey(ClassKey))
            {
                document.SetValue(null, "Lucent HTML Document");

                using RegistryKey defaultIcon = document.CreateSubKey("DefaultIcon");
                defaultIcon.SetValue(null, icon);
            }

            using (RegistryKey command = Registry.CurrentUser.CreateSubKey(CommandKey))
                command.SetValue(null, OpenCommand);

            using (RegistryKey registered = Registry.CurrentUser.CreateSubKey(RegisteredApplications))
                registered.SetValue(AppName, CapabilitiesKey);

            PlaceStartMenuShortcut();

            SHChangeNotify(AssociationChanged, IdList, IntPtr.Zero, IntPtr.Zero);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void PlaceStartMenuShortcut()
    {
        try
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (folder.Length == 0) return;

            Directory.CreateDirectory(folder);

            var link = (IShellLink)new ShellLink();
            link.SetPath(Executable);
            link.SetWorkingDirectory(Path.GetDirectoryName(Executable) ?? string.Empty);
            link.SetIconLocation(Executable, 0);
            link.SetDescription("Lucent");

            ((IPersistFile)link).Save(Path.Combine(folder, AppName + ".lnk"), true);
        }
        catch (Exception)
        {
        }
    }

    public static void OpenSettings()
    {
        string page = Environment.OSVersion.Version.Build >= 22000
            ? $"ms-settings:defaultapps?registeredAppUser={AppName}"
            : "ms-settings:defaultapps";

        try
        {
            Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    public static void RefreshIfMoved()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(CommandKey);
        if (key is null) return;

        if (key.GetValue(null) is not string command) return;
        if (command == OpenCommand) return;

        if (File.Exists(Registered(command))) return;

        Register();
    }

    private static string Registered(string command)
    {
        int opening = command.IndexOf('"');
        if (opening < 0) return string.Empty;

        int closing = command.IndexOf('"', opening + 1);

        return closing < 0 ? string.Empty : command[(opening + 1)..closing];
    }

    private const int AssociationChanged = 0x08000000;
    private const uint IdList = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private const int AssocIsProtocol = 0x1000;
    private const int AssocExecutable = 2;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(
        int flags, int wanted, string association, string? extra, StringBuilder result, ref int length);


    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath(IntPtr file, int maxPath, IntPtr findData, int flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription(IntPtr name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(IntPtr arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation(IntPtr icon, int maxPath, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(IntPtr owner, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, int mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile(IntPtr file);
    }
}
