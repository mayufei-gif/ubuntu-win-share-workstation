using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace UbuntuWinShareClient
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (CommandLine.Has(args, "--self-test"))
                {
                    return SelfTest.Run(CommandLine.ValueAfter(args, "--result"));
                }

                if (CommandLine.Has(args, "--sync-test"))
                {
                    return SelfTest.RunSyncProbe(
                        CommandLine.ValueAfter(args, "--source"),
                        CommandLine.ValueAfter(args, "--target"),
                        CommandLine.ValueAfter(args, "--result"));
                }

                if (CommandLine.Has(args, "--queue-test"))
                {
                    return SelfTest.RunQueueProbe(
                        CommandLine.ValueAfter(args, "--share"),
                        CommandLine.ValueAfter(args, "--profile"),
                        CommandLine.ValueAfter(args, "--nas-host"),
                        CommandLine.ValueAfter(args, "--nas-user"),
                        CommandLine.ValueAfter(args, "--nas-password"),
                        CommandLine.ValueAfter(args, "--nas-remote"),
                        CommandLine.ValueAfter(args, "--result"));
                }

                if (CommandLine.Has(args, "--os-probe"))
                {
                    return SelfTest.RunOperatingSystemProbe(
                        CommandLine.ValueAfter(args, "--result"));
                }

                if (!WindowsVersion.IsWindows7Sp1())
                {
                    MessageBox.Show(
                        "此安装包仅支持 Windows 7 SP1。\r\n\r\n" +
                        "当前电脑不是 Windows 7 SP1，未执行安装或启动。",
                        "Ubuntu Win Share",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Stop);
                    return 3;
                }

                if (CommandLine.Has(args, "--sync-worker"))
                {
                    return SyncWorkerHost.Run(
                        CommandLine.ValueAfter(args, "--result"),
                        CommandLine.Has(args, "--force-reconnect"));
                }

                if (CommandLine.Has(args, "--uninstall"))
                {
                    return SelfInstaller.Uninstall();
                }

                if (!SelfInstaller.IsInstalledCopy())
                {
                    SelfInstaller.InstallAndLaunch();
                    return 0;
                }

                bool createdNew;
                using (Mutex mutex = new Mutex(
                    true,
                    AppSignals.ClientMutexName,
                    out createdNew))
                {
                    if (!createdNew)
                    {
                        if (CommandLine.Has(args, "--background"))
                        {
                            return 0;
                        }
                        MessageBox.Show(
                            "Ubuntu Win Share 已经在后台运行。",
                            "Ubuntu Win Share",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return 0;
                    }

                    bool exitEventCreated;
                    using (EventWaitHandle exitEvent = new EventWaitHandle(
                        false,
                        EventResetMode.ManualReset,
                        AppSignals.ExitEventName,
                        out exitEventCreated))
                    {
                        AppConfig config = ConfigStore.Load();
                        bool configure =
                            config == null ||
                            CommandLine.Has(args, "--configure");
                        if (configure)
                        {
                            using (SetupForm setup = new SetupForm(
                                config,
                                exitEvent))
                            {
                                if (setup.ShowDialog() != DialogResult.OK)
                                {
                                    return 0;
                                }
                                config = setup.SavedConfig;
                            }
                        }

                        SelfInstaller.RegisterApplication();
                        Application.Run(
                            new TrayApplicationContext(config, exitEvent));
                        GC.KeepAlive(exitEventCreated);
                    }
                    GC.KeepAlive(mutex);
                }
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("fatal", ex);
                MessageBox.Show(
                    "启动失败：" + ErrorText.Safe(ex),
                    "Ubuntu Win Share",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal static class WindowsVersion
    {
        private const byte WorkstationProductType = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RtlOsVersionInfoEx
        {
            public uint Size;
            public uint Major;
            public uint Minor;
            public uint Build;
            public uint Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string ServicePack;
            public ushort ServicePackMajor;
            public ushort ServicePackMinor;
            public ushort SuiteMask;
            public byte ProductType;
            public byte Reserved;
        }

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(
            ref RtlOsVersionInfoEx versionInfo);

        public static bool IsWindows7Sp1()
        {
            RtlOsVersionInfoEx info = new RtlOsVersionInfoEx();
            info.Size = (uint)Marshal.SizeOf(typeof(RtlOsVersionInfoEx));
            if (RtlGetVersion(ref info) != 0)
            {
                return false;
            }
            return IsWindows7Sp1(
                info.Major,
                info.Minor,
                info.Build,
                info.ProductType);
        }

        internal static bool IsWindows7Sp1(
            int major,
            int minor,
            int build)
        {
            return IsWindows7Sp1(
                (uint)major,
                (uint)minor,
                (uint)build,
                WorkstationProductType);
        }

        internal static bool IsWindows7Sp1(
            uint major,
            uint minor,
            uint build,
            byte productType)
        {
            return major == 6 &&
                   minor == 1 &&
                   build >= 7601 &&
                   productType == WorkstationProductType;
        }

        internal static string DescribeCurrent()
        {
            RtlOsVersionInfoEx info = new RtlOsVersionInfoEx();
            info.Size = (uint)Marshal.SizeOf(typeof(RtlOsVersionInfoEx));
            int status = RtlGetVersion(ref info);
            if (status != 0)
            {
                return "RtlGetVersion failed: 0x" +
                       status.ToString("X8");
            }
            return info.Major + "." +
                   info.Minor + "." +
                   info.Build +
                   " product_type=" +
                   info.ProductType +
                   " sp=" +
                   info.ServicePackMajor;
        }
    }

    internal static class CommandLine
    {
        public static bool Has(string[] args, string value)
        {
            int i;
            for (i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static string ValueAfter(string[] args, string key)
        {
            int i;
            for (i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }

    internal static class SelfInstaller
    {
        private const string EmbeddedUninstallerResource =
            "UbuntuWinShareClient.EmbeddedUninstaller";

        public static bool IsInstalledCopy()
        {
            string current = Path.GetFullPath(Application.ExecutablePath);
            string installed = Path.GetFullPath(AppPaths.InstalledExe);
            return string.Equals(current, installed, StringComparison.OrdinalIgnoreCase);
        }

        public static void InstallAndLaunch()
        {
            Directory.CreateDirectory(AppPaths.Root);
            string source = Path.GetFullPath(Application.ExecutablePath);
            string target = Path.GetFullPath(AppPaths.InstalledExe);

            if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                string temporary = target + ".new";
                File.Copy(source, temporary, true);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Move(temporary, target);
            }

            EnsureUninstallerInstalled();

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = target;
            start.Arguments = ConfigStore.Exists ? "--background" : "--configure";
            start.UseShellExecute = true;
            Process.Start(start);
        }

        public static void RegisterStartup()
        {
            Microsoft.Win32.RegistryKey key = null;
            try
            {
                key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Run");
                if (key != null)
                {
                    key.SetValue(
                        "UbuntuWinShare",
                        "\"" + AppPaths.InstalledExe + "\" --background",
                        Microsoft.Win32.RegistryValueKind.String);
                }
            }
            finally
            {
                if (key != null)
                {
                    key.Close();
                }
            }
        }

        public static void RegisterApplication()
        {
            EnsureUninstallerInstalled();
            RegisterStartup();
            Microsoft.Win32.RegistryKey key = null;
            try
            {
                key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\UbuntuWinShare");
                if (key != null)
                {
                    key.SetValue("DisplayName", "Ubuntu Win Share");
                    key.SetValue("DisplayVersion", Application.ProductVersion);
                    key.SetValue("Publisher", "Ubuntu Win Share Workstation");
                    key.SetValue("InstallLocation", AppPaths.Root);
                    key.SetValue("DisplayIcon", AppPaths.InstalledExe);
                    key.SetValue(
                        "UninstallString",
                        "\"" + AppPaths.UninstallerExe + "\"");
                    key.SetValue(
                        "EstimatedSize",
                        InstalledSizeKilobytes(),
                        Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue(
                        "NoModify",
                        1,
                        Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue(
                        "NoRepair",
                        1,
                        Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            finally
            {
                if (key != null)
                {
                    key.Close();
                }
            }
        }

        public static bool HasEmbeddedUninstaller()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().
                GetManifestResourceStream(EmbeddedUninstallerResource))
            {
                return stream != null && stream.Length > 0;
            }
        }

        public static void EnsureUninstallerInstalled()
        {
            Directory.CreateDirectory(AppPaths.Root);
            if (EmbeddedUninstallerMatchesInstalled())
            {
                return;
            }
            string temporary = AppPaths.UninstallerExe + ".new";

            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            using (Stream source = Assembly.GetExecutingAssembly().
                GetManifestResourceStream(EmbeddedUninstallerResource))
            {
                if (source == null)
                {
                    throw new InvalidOperationException(
                        "安装包中缺少独立卸载器。");
                }
                using (FileStream target = new FileStream(
                    temporary,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    byte[] buffer = new byte[32768];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        target.Write(buffer, 0, read);
                    }
                }
            }

            if (File.Exists(AppPaths.UninstallerExe))
            {
                File.Delete(AppPaths.UninstallerExe);
            }
            File.Move(temporary, AppPaths.UninstallerExe);
        }

        public static bool LaunchUninstaller()
        {
            try
            {
                EnsureUninstallerInstalled();
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = AppPaths.UninstallerExe;
                start.UseShellExecute = true;
                Process.Start(start);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("launch-uninstaller", ex);
                return false;
            }
        }

        public static int Uninstall()
        {
            return LaunchUninstaller() ? 0 : 1;
        }

        private static bool EmbeddedUninstallerMatchesInstalled()
        {
            if (!File.Exists(AppPaths.UninstallerExe))
            {
                return false;
            }

            try
            {
                using (Stream embedded = Assembly.GetExecutingAssembly().
                    GetManifestResourceStream(EmbeddedUninstallerResource))
                using (FileStream installed = File.OpenRead(
                    AppPaths.UninstallerExe))
                {
                    if (embedded == null ||
                        embedded.Length != installed.Length)
                    {
                        return false;
                    }

                    using (SHA256Managed sha = new SHA256Managed())
                    {
                        byte[] expected = sha.ComputeHash(embedded);
                        byte[] actual = sha.ComputeHash(installed);
                        if (expected.Length != actual.Length)
                        {
                            return false;
                        }
                        int i;
                        for (i = 0; i < expected.Length; i++)
                        {
                            if (expected[i] != actual[i])
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static int InstalledSizeKilobytes()
        {
            long bytes = 0;
            if (File.Exists(AppPaths.InstalledExe))
            {
                bytes += new FileInfo(AppPaths.InstalledExe).Length;
            }
            if (File.Exists(AppPaths.UninstallerExe))
            {
                bytes += new FileInfo(AppPaths.UninstallerExe).Length;
            }
            return (int)Math.Max(1, bytes / 1024);
        }

    }
}
