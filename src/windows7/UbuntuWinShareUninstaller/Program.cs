using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace UbuntuWinShareUninstaller
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (Arguments.Has(args, "--self-test"))
            {
                return SelfTest.Run(Arguments.ValueAfter(args, "--result"));
            }

            if (Arguments.Has(args, "--execute"))
            {
                int parentProcessId;
                int.TryParse(
                    Arguments.ValueAfter(args, "--parent"),
                    out parentProcessId);
                return UninstallEngine.Execute(parentProcessId);
            }

            DialogResult answer = MessageBox.Show(
                "将卸载这台 Windows 7 电脑上的 Ubuntu Win Share。\r\n\r\n" +
                "会删除：本机客户端、配置、开机启动项和旧恢复任务。\r\n" +
                "不会删除：Windows 原始文件、Ubuntu 共享文件或 NAS 文件。",
                "卸载 Ubuntu Win Share",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.OK)
            {
                return 0;
            }

            try
            {
                string worker = Path.Combine(
                    Path.GetTempPath(),
                    "UbuntuWinShareUninstaller-" +
                    Guid.NewGuid().ToString("N") +
                    ".exe");
                File.Copy(Application.ExecutablePath, worker, true);

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = worker;
                start.Arguments =
                    "--execute --parent " +
                    Process.GetCurrentProcess().Id;
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(start);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "无法启动卸载程序：" + ErrorText.Safe(ex),
                    "Ubuntu Win Share",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal static class Arguments
    {
        public static bool Has(string[] args, string value)
        {
            int i;
            for (i = 0; i < args.Length; i++)
            {
                if (string.Equals(
                        args[i],
                        value,
                        StringComparison.OrdinalIgnoreCase))
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
                if (string.Equals(
                        args[i],
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }

    internal static class InstallContract
    {
        public const string ClientMutexName =
            "Local\\UbuntuWinShareClient.Singleton";
        public const string ExitEventName =
            "Local\\UbuntuWinShareClient.Exit";
        public const string StartupValueName = "UbuntuWinShare";
        public const string UninstallKeyName = "UbuntuWinShare";
        public const string LogonTaskName =
            "Ubuntu Win Share Win7 Logon";
        public const string SelfHealTaskName =
            "Ubuntu Win Share Win7 Self Heal";

        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "UbuntuWinShare");
        public static readonly string InstalledExe =
            Path.Combine(Root, "UbuntuWinShare.exe");
        public static readonly string ConfigFile =
            Path.Combine(Root, "config.dat");
        public static readonly string ConfigBackupFile =
            Path.Combine(Root, "config.dat.bak");
    }

    [Serializable]
    [XmlRoot("AppConfig")]
    public sealed class AppConfig
    {
        public string ShareUnc = "";
        public string DriveLetter = "";
    }

    internal static class ConfigReader
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("UbuntuWinShare.Config.v1");

        public static AppConfig TryLoad()
        {
            return TryLoadFromFiles(
                InstallContract.ConfigFile,
                InstallContract.ConfigBackupFile);
        }

        internal static AppConfig TryLoadFromFiles(
            string configPath,
            string backupPath)
        {
            AppConfig config = TryLoadFile(configPath);
            if (config != null)
            {
                return config;
            }
            return TryLoadFile(backupPath);
        }

        private static AppConfig TryLoadFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            byte[] plain = null;
            try
            {
                byte[] encrypted =
                    File.ReadAllBytes(path);
                plain = ProtectedData.Unprotect(
                    encrypted,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                XmlSerializer serializer =
                    new XmlSerializer(typeof(AppConfig));
                using (MemoryStream stream = new MemoryStream(plain))
                {
                    return (AppConfig)serializer.Deserialize(stream);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (plain != null)
                {
                    Array.Clear(plain, 0, plain.Length);
                }
            }
        }
    }

    internal static class UninstallEngine
    {
        private const int DeleteDelayUntilReboot = 4;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(
            string existingFileName,
            string newFileName,
            int flags);

        public static int Execute(int parentProcessId)
        {
            List<string> warnings = new List<string>();
            try
            {
                WaitForParent(parentProcessId);
                AppConfig config = ConfigReader.TryLoad();

                using (EventWaitHandle exitEvent = CreateExitSignal())
                {
                    exitEvent.Set();
                    if (!WaitForClientExit(15000) &&
                        !ForceStopInstalledClient())
                    {
                        warnings.Add(
                            "后台客户端未能退出，部分本机文件可能需要重启后清理。");
                    }

                    RemoveRegistry(warnings);
                    DisconnectConfiguredMapping(config, warnings);
                    RemoveLegacyTask(InstallContract.LogonTaskName, warnings);
                    RemoveLegacyTask(InstallContract.SelfHealTaskName, warnings);
                    DeleteInstallDirectory(warnings);
                }
                ScheduleOwnDeletion();

                if (warnings.Count == 0)
                {
                    MessageBox.Show(
                        "Ubuntu Win Share 已从这台电脑卸载。\r\n\r\n" +
                        "Windows 原始文件、Ubuntu 共享文件和 NAS 文件均未删除。",
                        "卸载完成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return 0;
                }

                MessageBox.Show(
                    "主体卸载已完成，但有以下项目需要检查：\r\n\r\n- " +
                    string.Join("\r\n- ", warnings.ToArray()) +
                    "\r\n\r\nWindows、Ubuntu 和 NAS 中的业务文件均未删除。",
                    "卸载完成并有提示",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 2;
            }
            catch (Exception ex)
            {
                ScheduleOwnDeletion();
                MessageBox.Show(
                    "卸载未完成：" + ErrorText.Safe(ex) +
                    "\r\n\r\n未删除 Windows 原始文件、Ubuntu 共享文件或 NAS 文件。",
                    "Ubuntu Win Share",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        public static bool IsSafeInstallRoot(string path)
        {
            if (Text.IsBlank(path))
            {
                return false;
            }
            try
            {
                string expected = Path.GetFullPath(
                    InstallContract.Root).TrimEnd('\\');
                string actual = Path.GetFullPath(path).TrimEnd('\\');
                return expected.Length > 3 &&
                       string.Equals(
                           expected,
                           actual,
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool ShouldDisconnectMapping(
            string drive,
            string expectedUnc,
            string actualUnc)
        {
            string normalizedDrive = NetworkShare.NormalizeDrive(drive);
            if (normalizedDrive.Length != 2 ||
                normalizedDrive[1] != ':' ||
                normalizedDrive[0] < 'A' ||
                normalizedDrive[0] > 'Z' ||
                Text.IsBlank(expectedUnc) ||
                Text.IsBlank(actualUnc))
            {
                return false;
            }

            return string.Equals(
                expectedUnc.Trim().TrimEnd('\\'),
                actualUnc.Trim().TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void WaitForParent(int processId)
        {
            if (processId <= 0)
            {
                Thread.Sleep(500);
                return;
            }
            try
            {
                using (Process parent = Process.GetProcessById(processId))
                {
                    parent.WaitForExit(10000);
                }
            }
            catch
            {
            }
        }

        private static EventWaitHandle CreateExitSignal()
        {
            bool created;
            EventWaitHandle exitEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                InstallContract.ExitEventName,
                out created);
            GC.KeepAlive(created);
            return exitEvent;
        }

        private static bool ForceStopInstalledClient()
        {
            bool stopped = true;
            string processName = Path.GetFileNameWithoutExtension(
                InstallContract.InstalledExe);
            Process[] processes = Process.GetProcessesByName(processName);
            int i;
            for (i = 0; i < processes.Length; i++)
            {
                using (Process process = processes[i])
                {
                    try
                    {
                        string executable = Path.GetFullPath(
                            process.MainModule.FileName);
                        if (!string.Equals(
                                executable,
                                Path.GetFullPath(
                                    InstallContract.InstalledExe),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string taskkill = Path.Combine(
                            Environment.GetFolderPath(
                                Environment.SpecialFolder.System),
                            "taskkill.exe");
                        if (File.Exists(taskkill))
                        {
                            RunHidden(
                                taskkill,
                                "/PID " +
                                process.Id +
                                " /T /F",
                                10000);
                        }
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                        if (!process.WaitForExit(5000))
                        {
                            stopped = false;
                        }
                    }
                    catch
                    {
                        stopped = false;
                    }
                }
            }
            return stopped && WaitForClientExit(5000);
        }

        private static bool WaitForClientExit(int timeoutMilliseconds)
        {
            try
            {
                using (Mutex mutex = new Mutex(
                    false,
                    InstallContract.ClientMutexName))
                {
                    try
                    {
                        if (!mutex.WaitOne(timeoutMilliseconds, false))
                        {
                            return false;
                        }
                    }
                    catch (AbandonedMutexException)
                    {
                    }
                    mutex.ReleaseMutex();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveRegistry(List<string> warnings)
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                    true))
                {
                    if (run != null)
                    {
                        run.DeleteValue(
                            InstallContract.StartupValueName,
                            false);
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add(
                    "无法删除当前用户开机启动项：" +
                    ErrorText.Safe(ex));
            }

            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                    true))
                {
                    if (root != null)
                    {
                        using (RegistryKey existing =
                            root.OpenSubKey(
                                InstallContract.UninstallKeyName))
                        {
                            if (existing != null)
                            {
                                existing.Close();
                                root.DeleteSubKeyTree(
                                    InstallContract.UninstallKeyName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add(
                    "无法删除控制面板卸载项：" +
                    ErrorText.Safe(ex));
            }
        }

        private static void DisconnectConfiguredMapping(
            AppConfig config,
            List<string> warnings)
        {
            if (config == null)
            {
                return;
            }

            string actual =
                NetworkShare.GetRemoteName(config.DriveLetter);
            if (!ShouldDisconnectMapping(
                    config.DriveLetter,
                    config.ShareUnc,
                    actual))
            {
                return;
            }

            int code = NetworkShare.Disconnect(config.DriveLetter);
            if (code != 0 && code != 2250)
            {
                warnings.Add(
                    "无法断开 " +
                    NetworkShare.NormalizeDrive(config.DriveLetter) +
                    " 映射，Windows 错误 " +
                    code +
                    "：" +
                    new Win32Exception(code).Message);
            }
        }

        private static void RemoveLegacyTask(
            string taskName,
            List<string> warnings)
        {
            string schtasks = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.System),
                "schtasks.exe");
            if (!File.Exists(schtasks))
            {
                return;
            }

            ProcessResult query = RunHidden(
                schtasks,
                "/Query /TN " + Quote(taskName),
                10000);
            if (query.ExitCode != 0)
            {
                return;
            }

            ProcessResult delete = RunHidden(
                schtasks,
                "/Delete /F /TN " + Quote(taskName),
                10000);
            if (delete.ExitCode != 0)
            {
                warnings.Add(
                    "无法删除旧计划任务“" +
                    taskName +
                    "”，请以管理员身份再次运行卸载包。");
            }
        }

        private static ProcessResult RunHidden(
            string fileName,
            string arguments,
            int timeoutMilliseconds)
        {
            ProcessResult result = new ProcessResult();
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = fileName;
            start.Arguments = arguments;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;

            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                    result.ExitCode = -1;
                    result.Output = "timeout";
                    return result;
                }
                result.ExitCode = process.ExitCode;
                result.Output = output + error;
                return result;
            }
        }

        private static void DeleteInstallDirectory(
            List<string> warnings)
        {
            if (!Directory.Exists(InstallContract.Root))
            {
                return;
            }
            if (!IsSafeInstallRoot(InstallContract.Root))
            {
                warnings.Add("安装目录安全校验失败，未删除本机客户端目录。");
                return;
            }

            Exception last = null;
            int attempt;
            for (attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    DeleteTree(InstallContract.Root);
                    if (!Directory.Exists(InstallContract.Root))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                Thread.Sleep(500);
            }

            ScheduleTreeDeletion(InstallContract.Root);
            warnings.Add(
                "本机客户端目录将在下次重启时继续清理" +
                (last == null ? "。" : "：" + ErrorText.Safe(last)));
        }

        private static void DeleteTree(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(path, false);
                return;
            }

            string[] files = Directory.GetFiles(path);
            int i;
            for (i = 0; i < files.Length; i++)
            {
                File.SetAttributes(files[i], FileAttributes.Normal);
                File.Delete(files[i]);
            }

            string[] directories = Directory.GetDirectories(path);
            for (i = 0; i < directories.Length; i++)
            {
                DeleteTree(directories[i]);
            }
            Directory.Delete(path, false);
        }

        private static void ScheduleTreeDeletion(string path)
        {
            try
            {
                List<string> files = new List<string>();
                List<string> directories = new List<string>();
                CollectDeletionTargets(path, files, directories);
                int i;
                for (i = 0; i < files.Count; i++)
                {
                    MoveFileEx(
                        files[i],
                        null,
                        DeleteDelayUntilReboot);
                }

                directories.Sort(
                    delegate(string left, string right)
                    {
                        return right.Length.CompareTo(left.Length);
                    });
                for (i = 0; i < directories.Count; i++)
                {
                    MoveFileEx(
                        directories[i],
                        null,
                        DeleteDelayUntilReboot);
                }
                MoveFileEx(path, null, DeleteDelayUntilReboot);
            }
            catch
            {
            }
        }

        private static void CollectDeletionTargets(
            string path,
            List<string> files,
            List<string> directories)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                directories.Add(path);
                return;
            }

            string[] childFiles = Directory.GetFiles(path);
            int i;
            for (i = 0; i < childFiles.Length; i++)
            {
                files.Add(childFiles[i]);
            }

            string[] childDirectories = Directory.GetDirectories(path);
            for (i = 0; i < childDirectories.Length; i++)
            {
                CollectDeletionTargets(
                    childDirectories[i],
                    files,
                    directories);
            }
            directories.Add(path);
        }

        private static void ScheduleOwnDeletion()
        {
            try
            {
                string executable =
                    Path.GetFullPath(Application.ExecutablePath);
                string tempRoot =
                    Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') +
                    "\\";
                if (executable.StartsWith(
                        tempRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    MoveFileEx(
                        executable,
                        null,
                        DeleteDelayUntilReboot);
                }
            }
            catch
            {
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class ProcessResult
    {
        public int ExitCode;
        public string Output;
    }

    internal static class NetworkShare
    {
        private const int ConnectUpdateProfile = 1;

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(
            string localName,
            StringBuilder remoteName,
            ref int length);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(
            string name,
            int flags,
            bool force);

        public static string NormalizeDrive(string value)
        {
            if (Text.IsBlank(value))
            {
                return "";
            }
            string drive = value.Trim().ToUpperInvariant();
            if (drive.Length == 1)
            {
                drive += ":";
            }
            return drive;
        }

        public static string GetRemoteName(string drive)
        {
            string normalized = NormalizeDrive(drive);
            if (normalized.Length != 2)
            {
                return "";
            }

            StringBuilder remote = new StringBuilder(1024);
            int length = remote.Capacity;
            int code = WNetGetConnection(
                normalized,
                remote,
                ref length);
            return code == 0 ? remote.ToString() : "";
        }

        public static int Disconnect(string drive)
        {
            return WNetCancelConnection2(
                NormalizeDrive(drive),
                ConnectUpdateProfile,
                false);
        }
    }

    internal static class SelfTest
    {
        public static int Run(string resultPath)
        {
            try
            {
                if (!UninstallEngine.IsSafeInstallRoot(
                        InstallContract.Root))
                {
                    throw new InvalidOperationException(
                        "Expected install root was rejected.");
                }
                if (UninstallEngine.IsSafeInstallRoot(
                        Path.GetDirectoryName(InstallContract.Root)))
                {
                    throw new InvalidOperationException(
                        "Parent directory passed the delete safety gate.");
                }
                if (!UninstallEngine.ShouldDisconnectMapping(
                        "i",
                        "\\\\server\\ubuntu-win\\",
                        "\\\\SERVER\\ubuntu-win"))
                {
                    throw new InvalidOperationException(
                        "Matching Ubuntu mapping was not recognized.");
                }
                if (UninstallEngine.ShouldDisconnectMapping(
                        "I:",
                        "\\\\server\\ubuntu-win",
                        "\\\\server\\other-share"))
                {
                    throw new InvalidOperationException(
                        "Unrelated mapping passed the disconnect safety gate.");
                }
                if (InstallContract.ClientMutexName !=
                        "Local\\UbuntuWinShareClient.Singleton" ||
                    InstallContract.ExitEventName !=
                        "Local\\UbuntuWinShareClient.Exit")
                {
                    throw new InvalidOperationException(
                        "Client exit contract does not match.");
                }
                TestConfigShapeCompatibility();
                TestConfigBackupFallback();

                WriteResult(
                    resultPath,
                    "WIN7_UNINSTALLER_SELF_TEST_OK");
                return 0;
            }
            catch (Exception ex)
            {
                WriteResult(
                    resultPath,
                    "WIN7_UNINSTALLER_SELF_TEST_FAILED " +
                    ErrorText.Safe(ex));
                return 1;
            }
        }

        private static void TestConfigShapeCompatibility()
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<AppConfig>" +
                "<Version>1</Version>" +
                "<ShareUnc>\\\\server\\ubuntu-win</ShareUnc>" +
                "<DriveLetter>I:</DriveLetter>" +
                "<NasPassword>ignored</NasPassword>" +
                "</AppConfig>";
            XmlSerializer serializer =
                new XmlSerializer(typeof(AppConfig));
            AppConfig config;
            using (MemoryStream stream = new MemoryStream(
                Encoding.UTF8.GetBytes(xml)))
            {
                config = (AppConfig)serializer.Deserialize(stream);
            }
            if (config.ShareUnc != "\\\\server\\ubuntu-win" ||
                config.DriveLetter != "I:")
            {
                throw new InvalidOperationException(
                    "Installed configuration shape is not compatible.");
            }
        }

        private static void TestConfigBackupFallback()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "UbuntuWinShareUninstaller-" +
                Guid.NewGuid().ToString("N"));
            string configPath = Path.Combine(root, "config.dat");
            string backupPath = configPath + ".bak";
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(
                    configPath,
                    "corrupt",
                    Encoding.ASCII);
                AppConfig expected = new AppConfig();
                expected.ShareUnc = "\\\\server\\ubuntu-win";
                expected.DriveLetter = "I:";
                WriteProtectedConfiguration(backupPath, expected);

                AppConfig actual = ConfigReader.TryLoadFromFiles(
                    configPath,
                    backupPath);
                if (actual == null ||
                    actual.ShareUnc != expected.ShareUnc ||
                    actual.DriveLetter != expected.DriveLetter)
                {
                    throw new InvalidOperationException(
                        "Uninstaller could not read the backup configuration.");
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch
                {
                }
            }
        }

        private static void WriteProtectedConfiguration(
            string path,
            AppConfig config)
        {
            byte[] plain;
            XmlSerializer serializer =
                new XmlSerializer(typeof(AppConfig));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, config);
                plain = stream.ToArray();
            }

            byte[] entropy =
                Encoding.UTF8.GetBytes("UbuntuWinShare.Config.v1");
            try
            {
                byte[] encrypted = ProtectedData.Protect(
                    plain,
                    entropy,
                    DataProtectionScope.CurrentUser);
                try
                {
                    File.WriteAllBytes(path, encrypted);
                }
                finally
                {
                    Array.Clear(encrypted, 0, encrypted.Length);
                }
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                Array.Clear(entropy, 0, entropy.Length);
            }
        }

        private static void WriteResult(string path, string value)
        {
            if (!Text.IsBlank(path))
            {
                File.WriteAllText(
                    path,
                    value + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
    }

    internal static class Text
    {
        public static bool IsBlank(string value)
        {
            return value == null || value.Trim().Length == 0;
        }
    }

    internal static class ErrorText
    {
        public static string Safe(Exception ex)
        {
            if (ex == null)
            {
                return "未知错误";
            }
            string value = ex.Message;
            if (Text.IsBlank(value))
            {
                value = ex.GetType().Name;
            }
            return value.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
