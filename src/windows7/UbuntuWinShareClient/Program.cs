using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace UbuntuWinShareClient
{
    internal static class Program
    {
        private const string MutexName = "Local\\UbuntuWinShareClient.Singleton";

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
                using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
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

                    AppConfig config = ConfigStore.Load();
                    bool configure = config == null || CommandLine.Has(args, "--configure");
                    if (configure)
                    {
                        using (SetupForm setup = new SetupForm(config))
                        {
                            if (setup.ShowDialog() != DialogResult.OK)
                            {
                                return 0;
                            }
                            config = setup.SavedConfig;
                        }
                    }

                    SelfInstaller.RegisterApplication();
                    Application.Run(new TrayApplicationContext(config));
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
                        "\"" + AppPaths.InstalledExe + "\" --uninstall");
                    key.SetValue(
                        "EstimatedSize",
                        (int)Math.Max(
                            1,
                            new FileInfo(AppPaths.InstalledExe).Length / 1024),
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

        public static int Uninstall()
        {
            try
            {
                Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        "Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                        true);
                if (key != null)
                {
                    key.DeleteValue("UbuntuWinShare", false);
                    key.Close();
                }
                Microsoft.Win32.RegistryKey uninstallRoot =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                        true);
                if (uninstallRoot != null)
                {
                    try
                    {
                        if (uninstallRoot.OpenSubKey("UbuntuWinShare") != null)
                        {
                            uninstallRoot.DeleteSubKeyTree("UbuntuWinShare");
                        }
                    }
                    finally
                    {
                        uninstallRoot.Close();
                    }
                }

                if (Directory.Exists(AppPaths.Root))
                {
                    string cleanup = Path.Combine(Path.GetTempPath(), "ubuntu-win-share-uninstall.cmd");
                    File.WriteAllText(
                        cleanup,
                        "@echo off\r\n" +
                        "ping 127.0.0.1 -n 3 >nul\r\n" +
                        "rmdir /s /q \"" + AppPaths.Root + "\"\r\n" +
                        "del /q \"%~f0\"\r\n");
                    ProcessStartInfo start = new ProcessStartInfo();
                    start.FileName = Environment.GetEnvironmentVariable("COMSPEC");
                    start.Arguments = "/c \"" + cleanup + "\"";
                    start.CreateNoWindow = true;
                    start.UseShellExecute = false;
                    start.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(start);
                }
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("uninstall", ex);
                return 1;
            }
        }
    }
}
