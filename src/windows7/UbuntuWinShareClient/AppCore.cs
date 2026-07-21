using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace UbuntuWinShareClient
{
    internal static class AppSignals
    {
        public const string ClientMutexName =
            "Local\\UbuntuWinShareClient.Singleton";
        public const string ExitEventName =
            "Local\\UbuntuWinShareClient.Exit";
    }

    [Serializable]
    public sealed class AppConfig
    {
        public int Version = 1;
        public string ClientId = Guid.NewGuid().ToString("N");
        public string ProfileName = "default";
        public string SourceFolder = "";
        public string ShareUnc = "";
        public string DriveLetter = "I:";
        public string ShareUsername = "";
        public string SharePassword = "";
        public string NasHost = "";
        public int NasPort = 22;
        public string NasUsername = "";
        public string NasPassword = "";
        public string NasRemoteRoot = "Win7Uploads";
        public bool MirrorDeletes = false;
        public int SyncIntervalSeconds = 60;
        public bool InitialSyncCompleted = false;
        public bool UploadPromptShown = false;
        public bool AutoUploadEnabled = false;
        public bool UploadPending = false;
        public string UploadPublicKeyFingerprint = "";
        public string LastQueuedJobId = "";
        public string LastQueuedUtc = "";

        public AppConfig Clone()
        {
            return new AppConfig {
                Version = Version,
                ClientId = ClientId,
                ProfileName = ProfileName,
                SourceFolder = SourceFolder,
                ShareUnc = ShareUnc,
                DriveLetter = DriveLetter,
                ShareUsername = ShareUsername,
                SharePassword = SharePassword,
                NasHost = NasHost,
                NasPort = NasPort,
                NasUsername = NasUsername,
                NasPassword = NasPassword,
                NasRemoteRoot = NasRemoteRoot,
                MirrorDeletes = MirrorDeletes,
                SyncIntervalSeconds = SyncIntervalSeconds,
                InitialSyncCompleted = InitialSyncCompleted,
                UploadPromptShown = UploadPromptShown,
                AutoUploadEnabled = AutoUploadEnabled,
                UploadPending = UploadPending,
                UploadPublicKeyFingerprint = UploadPublicKeyFingerprint,
                LastQueuedJobId = LastQueuedJobId,
                LastQueuedUtc = LastQueuedUtc
            };
        }
    }

    internal static class AppPaths
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UbuntuWinShare");
        public static readonly string InstalledExe = Path.Combine(Root, "UbuntuWinShare.exe");
        public static readonly string UninstallerExe = Path.Combine(Root, "UbuntuWinShareUninstaller.exe");
        public static readonly string ConfigFile = Path.Combine(Root, "config.dat");
        public static readonly string ConfigBackupFile = Path.Combine(Root, "config.dat.bak");
        public static readonly string LogFile = Path.Combine(Root, "client.log");
        public static readonly string DiagnosticsFile = Path.Combine(Root, "diagnostics.txt");
    }

    internal static class SecureXml
    {
        public static XmlReader CreateReader(Stream stream)
        {
            return XmlReader.Create(stream, CreateSettings());
        }

        public static XmlDocument LoadDocument(string path)
        {
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            using (XmlReader reader = XmlReader.Create(path, CreateSettings()))
            {
                document.Load(reader);
            }
            return document;
        }

        public static string LoadRsaPublicKeyXml(string path)
        {
            XmlDocument document = LoadDocument(path);
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "RSAKeyValue")
            {
                throw new InvalidOperationException(
                    "Ubuntu upload agent public key is invalid.");
            }
            return root.OuterXml;
        }

        private static XmlReaderSettings CreateSettings()
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.ProhibitDtd = true;
            settings.XmlResolver = null;
            settings.CloseInput = false;
            return settings;
        }
    }

    internal static class ConfigStore
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("UbuntuWinShare.Config.v1");

        public static bool Exists
        {
            get
            {
                return File.Exists(AppPaths.ConfigFile) ||
                       File.Exists(AppPaths.ConfigBackupFile);
            }
        }

        public static AppConfig Load()
        {
            return LoadFromFiles(
                AppPaths.ConfigFile,
                AppPaths.ConfigBackupFile,
                true);
        }

        internal static AppConfig LoadFromFiles(
            string configPath,
            string backupPath,
            bool writeLog)
        {
            AppConfig config = TryLoad(configPath, writeLog);
            if (config != null)
            {
                return config;
            }

            config = TryLoad(backupPath, writeLog);
            if (config != null)
            {
                try
                {
                    RestoreBackup(configPath, backupPath);
                    if (writeLog)
                    {
                        Log.Write(
                            "config-load",
                            "主配置损坏，已从 .bak 恢复。");
                    }
                }
                catch (Exception ex)
                {
                    if (writeLog)
                    {
                        Log.Write("config-restore", ex);
                    }
                }
            }
            return config;
        }

        private static AppConfig TryLoad(string path, bool writeLog)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                byte[] encrypted = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(
                    encrypted,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
                    using (MemoryStream stream = new MemoryStream(plain))
                    using (XmlReader reader = SecureXml.CreateReader(stream))
                    {
                        AppConfig config = (AppConfig)serializer.Deserialize(reader);
                        Normalize(config);
                        return config;
                    }
                }
                finally
                {
                    Array.Clear(plain, 0, plain.Length);
                }
            }
            catch (Exception ex)
            {
                if (writeLog)
                {
                    Log.Write(
                        "config-load",
                        path + ": " + ErrorText.Safe(ex));
                }
                return null;
            }
        }

        public static void Save(AppConfig config)
        {
            SaveToFiles(
                config,
                AppPaths.ConfigFile,
                AppPaths.ConfigBackupFile);
        }

        public static bool EnsureNasPasswordRemoved(AppConfig config)
        {
            bool rewrite =
                NeedsNasPasswordSanitization(AppPaths.ConfigFile) ||
                NeedsNasPasswordSanitization(AppPaths.ConfigBackupFile);
            if (!rewrite)
            {
                return false;
            }
            SaveAfterNasPasswordRemovalToFiles(
                config,
                AppPaths.ConfigFile,
                AppPaths.ConfigBackupFile);
            return true;
        }

        internal static void SaveToFiles(
            AppConfig config,
            string configPath,
            string backupPath)
        {
            SaveToFilesCore(
                config,
                configPath,
                backupPath,
                false);
        }

        internal static void SaveAfterNasPasswordRemovalToFiles(
            AppConfig config,
            string configPath,
            string backupPath)
        {
            SaveToFilesCore(
                config,
                configPath,
                backupPath,
                true);
        }

        internal static bool NeedsNasPasswordSanitization(
            string path)
        {
            AppConfig stored = TryLoad(path, false);
            return stored == null ||
                   !Text.IsBlank(stored.NasPassword);
        }

        private static void SaveToFilesCore(
            AppConfig config,
            string configPath,
            string backupPath,
            bool synchronizeBackupWithCurrent)
        {
            Normalize(config);
            string directory = Path.GetDirectoryName(configPath);
            if (Text.IsBlank(directory))
            {
                throw new InvalidOperationException(
                    "Configuration directory is missing.");
            }
            Directory.CreateDirectory(directory);

            byte[] plain;
            XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, config);
                plain = stream.ToArray();
            }

            try
            {
                byte[] encrypted = ProtectedData.Protect(
                    plain,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                try
                {
                    if (synchronizeBackupWithCurrent)
                    {
                        ReplaceFileWithBytes(
                            backupPath,
                            encrypted);
                        ReplaceFileWithBytes(
                            configPath,
                            encrypted);
                        return;
                    }

                    string temporary = configPath + "." +
                                       Guid.NewGuid().ToString("N") +
                                       ".tmp";
                    try
                    {
                        WriteBytesDurably(temporary, encrypted);
                        if (File.Exists(configPath))
                        {
                            File.Replace(
                                temporary,
                                configPath,
                                backupPath,
                                true);
                        }
                        else
                        {
                            ReplaceFileWithBytes(
                                backupPath,
                                encrypted);
                            File.Move(temporary, configPath);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(temporary))
                            {
                                File.Delete(temporary);
                            }
                        }
                        catch
                        {
                        }
                    }

                }
                finally
                {
                    Array.Clear(encrypted, 0, encrypted.Length);
                }
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        private static void ReplaceFileWithBytes(
            string path,
            byte[] bytes)
        {
            string temporary = path + "." +
                               Guid.NewGuid().ToString("N") +
                               ".tmp";
            try
            {
                WriteBytesDurably(temporary, bytes);
                if (File.Exists(path))
                {
                    File.Replace(
                        temporary,
                        path,
                        null,
                        true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch
                {
                }
            }
        }

        private static void WriteBytesDurably(string path, byte[] bytes)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }

        private static void RestoreBackup(
            string configPath,
            string backupPath)
        {
            if (!File.Exists(backupPath))
            {
                return;
            }

            string temporary = configPath + "." +
                               Guid.NewGuid().ToString("N") +
                               ".restore";
            try
            {
                File.Copy(backupPath, temporary, true);
                if (File.Exists(configPath))
                {
                    File.Replace(
                        temporary,
                        configPath,
                        null,
                        true);
                }
                else
                {
                    File.Move(temporary, configPath);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch
                {
                }
            }
        }

        private static void Normalize(AppConfig config)
        {
            if (config.Version <= 0) config.Version = 1;
            if (Text.IsBlank(config.ClientId)) config.ClientId = Guid.NewGuid().ToString("N");
            if (Text.IsBlank(config.ProfileName)) config.ProfileName = "default";
            if (Text.IsBlank(config.DriveLetter)) config.DriveLetter = "I:";
            config.DriveLetter = Text.NormalizeDrive(config.DriveLetter);
            if (config.SyncIntervalSeconds < 15) config.SyncIntervalSeconds = 15;
            if (config.SyncIntervalSeconds > 3600) config.SyncIntervalSeconds = 3600;
            if (config.NasPort <= 0 || config.NasPort > 65535) config.NasPort = 22;
            if (config.SharePassword == null) config.SharePassword = "";
            if (config.NasPassword == null) config.NasPassword = "";
            if (config.UploadPublicKeyFingerprint == null)
                config.UploadPublicKeyFingerprint = "";
            if (config.LastQueuedJobId == null) config.LastQueuedJobId = "";
            if (config.LastQueuedUtc == null) config.LastQueuedUtc = "";
        }
    }

    internal static class Text
    {
        public static bool IsBlank(string value)
        {
            return value == null || value.Trim().Length == 0;
        }

        public static string NormalizeDrive(string value)
        {
            if (Text.IsBlank(value))
            {
                return "I:";
            }
            string trimmed = value.Trim().ToUpperInvariant();
            if (trimmed.Length == 1)
            {
                trimmed += ":";
            }
            return trimmed;
        }

        public static string SafeSegment(string value)
        {
            if (Text.IsBlank(value))
            {
                return "default";
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder();
            int i;
            for (i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                bool bad = false;
                int j;
                for (j = 0; j < invalid.Length; j++)
                {
                    if (ch == invalid[j])
                    {
                        bad = true;
                        break;
                    }
                }
                result.Append(bad ? '_' : ch);
            }
            string safe = result.ToString().Trim().Trim('.');
            return safe.Length == 0 ? "default" : safe;
        }

        public static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public static string ToForwardSlashes(string value)
        {
            return value.Replace('\\', '/');
        }

        public static string CombineRemote(string first, string second, string third)
        {
            string result = first == null ? "" : first.Trim().Replace('\\', '/').Trim('/');
            string[] parts = new string[] {
                SafeRemoteSegment(second),
                SafeRemoteSegment(third)
            };
            int i;
            for (i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? "" : parts[i].Trim().Replace('\\', '/').Trim('/');
                if (part.Length > 0)
                {
                    if (result.Length > 0) result += "/";
                    result += part;
                }
            }
            return result;
        }

        public static string SafeRemoteSegment(string value)
        {
            if (Text.IsBlank(value))
            {
                return "item";
            }

            StringBuilder safe = new StringBuilder();
            bool changed = false;
            bool previousUnderscore = false;
            int i;
            for (i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                bool allowed =
                    ch <= 127 &&
                    ((ch >= 'A' && ch <= 'Z') ||
                     (ch >= 'a' && ch <= 'z') ||
                     (ch >= '0' && ch <= '9') ||
                     ch == '.' ||
                     ch == '_' ||
                     ch == '-');
                if (allowed)
                {
                    safe.Append(ch);
                    previousUnderscore = false;
                }
                else
                {
                    changed = true;
                    if (!previousUnderscore)
                    {
                        safe.Append('_');
                        previousUnderscore = true;
                    }
                }
            }

            string result = safe.ToString().Trim('.', '_', '-');
            if (result.Length == 0)
            {
                result = "item";
                changed = true;
            }
            if (!string.Equals(result, value, StringComparison.Ordinal))
            {
                changed = true;
            }
            if (changed)
            {
                using (SHA256Managed sha = new SHA256Managed())
                {
                    byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                    result += "-" + HexPrefix(digest, 4);
                }
            }
            return result;
        }

        private static string HexPrefix(byte[] bytes, int count)
        {
            StringBuilder text = new StringBuilder(count * 2);
            int i;
            for (i = 0; i < count && i < bytes.Length; i++)
            {
                text.Append(bytes[i].ToString("x2"));
            }
            return text.ToString();
        }
    }

    internal static class ErrorText
    {
        public static string Safe(Exception ex)
        {
            if (ex == null) return "未知错误";
            string value = ex.Message;
            if (Text.IsBlank(value)) value = ex.GetType().Name;
            return value.Replace("\r", " ").Replace("\n", " ");
        }
    }

    internal static class Log
    {
        private static readonly object Sync = new object();

        public static void Write(string category, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(AppPaths.Root);
                    Rotate();
                    File.AppendAllText(
                        AppPaths.LogFile,
                        DateTime.UtcNow.ToString("s") + "Z [" + category + "] " +
                        message.Replace("\r", " ").Replace("\n", " ") +
                        Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public static void Write(string category, Exception ex)
        {
            Write(category, ErrorText.Safe(ex));
        }

        private static void Rotate()
        {
            if (!File.Exists(AppPaths.LogFile)) return;
            FileInfo info = new FileInfo(AppPaths.LogFile);
            if (info.Length < 1024 * 1024) return;
            string old = AppPaths.LogFile + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(AppPaths.LogFile, old);
        }
    }

    internal sealed class ShareMapResult
    {
        public bool Success;
        public string Message;
    }

    internal static class NetworkShare
    {
        private const int ResourceTypeDisk = 1;
        private const int ConnectUpdateProfile = 1;
        private const int ErrorAlreadyAssigned = 85;
        private const int ErrorNotConnected = 2250;

        [StructLayout(LayoutKind.Sequential)]
        private struct NetResource
        {
            public int Scope;
            public int Type;
            public int DisplayType;
            public int Usage;
            public string LocalName;
            public string RemoteName;
            public string Comment;
            public string Provider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(
            ref NetResource netResource,
            string password,
            string username,
            int flags);

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

        public static ShareMapResult EnsureMapped(AppConfig config)
        {
            return EnsureMapped(config, false);
        }

        public static ShareMapResult EnsureMapped(
            AppConfig config,
            bool forceReconnect)
        {
            ShareMapResult result = new ShareMapResult();
            string drive = Text.NormalizeDrive(config.DriveLetter);
            string existing = GetRemoteName(drive);
            if (!Text.IsBlank(existing))
            {
                if (!string.Equals(
                        existing.TrimEnd('\\'),
                        config.ShareUnc.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Success = false;
                    result.Message = drive + " 已被其它网络位置占用：" + existing;
                    return result;
                }
                if (!forceReconnect)
                {
                    result.Success = true;
                    result.Message = drive + " -> " + config.ShareUnc;
                    return result;
                }

                int cancelCode = WNetCancelConnection2(
                    drive,
                    ConnectUpdateProfile,
                    true);
                if (cancelCode != 0 && cancelCode != ErrorNotConnected)
                {
                    result.Success = false;
                    result.Message = "无法重建失效映射，Windows 错误 " +
                                     cancelCode + "：" +
                                     new Win32Exception(cancelCode).Message;
                    return result;
                }
            }

            NetResource resource = new NetResource();
            resource.Type = ResourceTypeDisk;
            resource.LocalName = drive;
            resource.RemoteName = config.ShareUnc;

            int code = WNetAddConnection2(
                ref resource,
                Text.IsBlank(config.SharePassword) ? null : config.SharePassword,
                Text.IsBlank(config.ShareUsername) ? null : config.ShareUsername,
                ConnectUpdateProfile);

            if (code == 0)
            {
                result.Success = true;
                result.Message = drive + " -> " + config.ShareUnc;
                return result;
            }

            if (code == ErrorAlreadyAssigned)
            {
                string assigned = GetRemoteName(drive);
                if (!Text.IsBlank(assigned) &&
                    string.Equals(
                        assigned.TrimEnd('\\'),
                        config.ShareUnc.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Success = true;
                    result.Message = drive + " -> " + config.ShareUnc;
                    return result;
                }
                result.Success = false;
                result.Message = drive + " 已被本地磁盘或其它设备占用。";
                return result;
            }

            result.Success = false;
            result.Message = "映射失败，Windows 错误 " + code + "：" +
                             new Win32Exception(code).Message;
            return result;
        }

        public static string GetRemoteName(string drive)
        {
            StringBuilder value = new StringBuilder(1024);
            int length = value.Capacity;
            int code = WNetGetConnection(Text.NormalizeDrive(drive), value, ref length);
            return code == 0 ? value.ToString() : "";
        }
    }

    internal sealed class SyncResult
    {
        public bool Success;
        public bool Changed;
        public int ExitCode;
        public string Destination;
        public string Message;
    }

    internal sealed class ProcessWaitResult
    {
        public bool Exited;
        public bool Stopped;
        public bool TimedOut;
        public int ExitCode;
    }

    internal static class ProcessSupervisor
    {
        public static ProcessWaitResult Wait(
            Process process,
            WaitHandle stopSignal,
            int timeoutMilliseconds)
        {
            ProcessWaitResult result = new ProcessWaitResult();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(
                timeoutMilliseconds);
            while (!process.WaitForExit(250))
            {
                if (stopSignal != null &&
                    stopSignal.WaitOne(0, false))
                {
                    result.Stopped = true;
                    break;
                }
                if (DateTime.UtcNow >= deadline)
                {
                    result.TimedOut = true;
                    break;
                }
            }

            if (result.Stopped || result.TimedOut)
            {
                TryKillProcessTree(process);
                result.Exited = process.WaitForExit(10000);
            }
            else
            {
                result.Exited = true;
            }

            if (result.Exited)
            {
                process.WaitForExit();
                result.ExitCode = process.ExitCode;
            }
            else
            {
                result.ExitCode = -3;
            }
            return result;
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (process == null || process.HasExited)
                {
                    return;
                }
                string taskkill = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.System),
                    "taskkill.exe");
                if (File.Exists(taskkill))
                {
                    ProcessStartInfo kill = new ProcessStartInfo();
                    kill.FileName = taskkill;
                    kill.Arguments = "/PID " + process.Id + " /T /F";
                    kill.CreateNoWindow = true;
                    kill.UseShellExecute = false;
                    using (Process killer = Process.Start(kill))
                    {
                        killer.WaitForExit(10000);
                    }
                }
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                Log.Write("process-kill", ex);
            }
        }
    }

    internal static class SyncWorkerProtocol
    {
        public static void Write(string path, SyncResult result)
        {
            string temporary = path + "." +
                               Guid.NewGuid().ToString("N") +
                               ".tmp";
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            settings.Indent = true;
            try
            {
                using (XmlWriter writer = XmlWriter.Create(
                    temporary,
                    settings))
                {
                    writer.WriteStartElement("SyncResult");
                    writer.WriteAttributeString("version", "1");
                    writer.WriteElementString(
                        "Success",
                        result.Success ? "true" : "false");
                    writer.WriteElementString(
                        "Changed",
                        result.Changed ? "true" : "false");
                    writer.WriteElementString(
                        "ExitCode",
                        result.ExitCode.ToString(
                            System.Globalization.CultureInfo.InvariantCulture));
                    writer.WriteElementString(
                        "Destination",
                        result.Destination ?? "");
                    writer.WriteElementString(
                        "Message",
                        result.Message ?? "");
                    writer.WriteEndElement();
                }
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch
                {
                }
            }
        }

        public static SyncResult Read(string path)
        {
            XmlDocument document = SecureXml.LoadDocument(path);
            XmlElement root = document.DocumentElement;
            if (root == null ||
                root.Name != "SyncResult" ||
                root.GetAttribute("version") != "1")
            {
                throw new InvalidOperationException(
                    "Unsupported sync worker result.");
            }

            SyncResult result = new SyncResult();
            result.Success = string.Equals(
                NodeText(root, "Success"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            result.Changed = string.Equals(
                NodeText(root, "Changed"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            int exitCode;
            if (!int.TryParse(
                    NodeText(root, "ExitCode"),
                    out exitCode))
            {
                throw new InvalidOperationException(
                    "Invalid sync worker exit code.");
            }
            result.ExitCode = exitCode;
            result.Destination = NodeText(root, "Destination");
            result.Message = NodeText(root, "Message");
            return result;
        }

        private static string NodeText(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            return node == null ? "" : node.InnerText;
        }
    }

    internal static class SyncWorkerHost
    {
        public static int Run(
            string resultPath,
            bool forceReconnect)
        {
            SyncResult result = new SyncResult();
            try
            {
                if (Text.IsBlank(resultPath))
                {
                    throw new InvalidOperationException(
                        "Sync worker result path is missing.");
                }
                AppConfig config = ConfigStore.Load();
                if (config == null)
                {
                    throw new InvalidOperationException(
                        "Sync worker configuration is unavailable.");
                }
                result = SyncRunner.RunWorker(
                    config,
                    forceReconnect);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ExitCode = -4;
                result.Message =
                    "同步 worker 异常：" + ErrorText.Safe(ex);
            }

            try
            {
                SyncWorkerProtocol.Write(resultPath, result);
            }
            catch (Exception ex)
            {
                Log.Write("sync-worker-result", ex);
                return 2;
            }
            return result.Success ? 0 : 1;
        }
    }

    internal static class SyncSchedule
    {
        public static bool ShouldStart(
            DateTime now,
            DateTime retryNotBefore,
            bool requested,
            bool sourceQuiet,
            DateTime periodicAt)
        {
            if (now < retryNotBefore)
            {
                return false;
            }
            return (requested && sourceQuiet) || now >= periodicAt;
        }

        public static DateTime OfflineRetryAt(DateTime now, int intervalSeconds)
        {
            return now.AddSeconds(Math.Max(30, intervalSeconds));
        }
    }

    internal static class RobocopyExitCode
    {
        public static bool HasCopiedFiles(int exitCode)
        {
            return exitCode >= 0 && (exitCode & 1) == 1;
        }
    }

    internal static class SyncRunner
    {
        private const int DefaultTimeoutSeconds = 300;
        private const int RetryCount = 1;

        public static string Destination(AppConfig config)
        {
            string machine = Text.SafeSegment(Environment.MachineName);
            string profile = Text.SafeSegment(config.ProfileName);
            return Path.Combine(
                Path.Combine(
                    Path.Combine(config.ShareUnc, "Win7Sync"),
                    machine),
                profile);
        }

        public static string SourceRelative(AppConfig config)
        {
            return Text.ToForwardSlashes(
                Path.Combine(
                    Path.Combine(
                        Path.Combine("Win7Sync", Text.SafeSegment(Environment.MachineName)),
                        Text.SafeSegment(config.ProfileName)),
                    ""));
        }

        public static SyncResult Run(AppConfig config)
        {
            return Run(config, null);
        }

        public static SyncResult Run(
            AppConfig config,
            WaitHandle stopSignal)
        {
            SyncResult result = null;
            int attempt;
            for (attempt = 0; attempt <= RetryCount; attempt++)
            {
                bool forceReconnect = attempt > 0;
                result = RunSupervised(
                    config,
                    stopSignal,
                    forceReconnect);
                if (result.Success ||
                    result.ExitCode == -1 ||
                    attempt == RetryCount)
                {
                    return result;
                }
                Log.Write(
                    "sync-retry",
                    "第一次同步失败，正在重建匹配映射后重试：" +
                    result.Message);
            }
            return result;
        }

        internal static SyncResult RunWorker(
            AppConfig config,
            bool forceReconnect)
        {
            SyncResult result = new SyncResult();
            result.Destination = Destination(config);

            if (!Directory.Exists(config.SourceFolder))
            {
                result.Success = false;
                result.Message = "本地同步目录不存在。";
                return result;
            }

            ShareMapResult mapped = NetworkShare.EnsureMapped(
                config,
                forceReconnect);
            if (!mapped.Success)
            {
                result.Success = false;
                result.Message = mapped.Message;
                return result;
            }

            try
            {
                Directory.CreateDirectory(result.Destination);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "无法创建共享目标目录：" + ErrorText.Safe(ex);
                return result;
            }

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "robocopy.exe");
            start.Arguments =
                QuoteArgument(config.SourceFolder) + " " +
                QuoteArgument(result.Destination) + " " +
                (config.MirrorDeletes ? "/MIR " : "/E ") +
                "/COPY:DAT /DCOPY:T /FFT /Z /XJ /R:2 /W:3 " +
                "/NP /NFL /NDL /NJH /NJS";
            start.CreateNoWindow = true;
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;

            Process process = null;
            try
            {
                process = Process.Start(start);
                using (process)
                {
                    StringBuilder output = new StringBuilder();
                    StringBuilder error = new StringBuilder();
                    object outputLock = new object();
                    object errorLock = new object();
                    process.OutputDataReceived +=
                        delegate(object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data == null) return;
                            lock (outputLock)
                            {
                                output.AppendLine(e.Data);
                            }
                        };
                    process.ErrorDataReceived +=
                        delegate(object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data == null) return;
                            lock (errorLock)
                            {
                                error.AppendLine(e.Data);
                            }
                        };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    result.ExitCode = process.ExitCode;
                    result.Success = process.ExitCode < 8;
                    result.Changed =
                        result.Success &&
                        RobocopyExitCode.HasCopiedFiles(process.ExitCode);
                    result.Message = result.Success
                        ? "同步完成，robocopy=" + process.ExitCode
                        : "同步失败，robocopy=" + process.ExitCode + " " +
                          (error.Length == 0
                              ? output.ToString()
                              : error.ToString());
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "无法运行 robocopy：" + ErrorText.Safe(ex);
            }

            if (result.Success)
            {
                WriteStatus(config, result);
            }
            return result;
        }

        private static SyncResult RunSupervised(
            AppConfig config,
            WaitHandle stopSignal,
            bool forceReconnect)
        {
            SyncResult result = new SyncResult();
            result.Destination = Destination(config);
            string resultPath = Path.Combine(
                Path.GetTempPath(),
                "ubuntu-win-share-sync-" +
                Guid.NewGuid().ToString("N") +
                ".xml");
            try
            {
                string executable = AppPaths.InstalledExe;
                if (!File.Exists(executable))
                {
                    executable =
                        Process.GetCurrentProcess().MainModule.FileName;
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = executable;
                start.Arguments =
                    "--sync-worker --result " +
                    QuoteArgument(resultPath) +
                    (forceReconnect
                        ? " --force-reconnect"
                        : "");
                start.CreateNoWindow = true;
                start.UseShellExecute = false;
                start.WindowStyle = ProcessWindowStyle.Hidden;

                using (Process worker = Process.Start(start))
                {
                    ProcessWaitResult waited = ProcessSupervisor.Wait(
                        worker,
                        stopSignal,
                        DefaultTimeoutSeconds * 1000);
                    if (waited.Stopped)
                    {
                        result.ExitCode = -1;
                        result.Message =
                            "同步已因客户端退出而停止。";
                        return result;
                    }
                    if (waited.TimedOut)
                    {
                        result.ExitCode = -2;
                        result.Message =
                            "同步超过 " +
                            DefaultTimeoutSeconds +
                            " 秒，已终止 worker 和 robocopy。";
                        return result;
                    }
                    if (!waited.Exited)
                    {
                        result.ExitCode = -3;
                        result.Message =
                            "无法确认超时同步进程已经退出。";
                        return result;
                    }
                }

                if (!File.Exists(resultPath))
                {
                    result.ExitCode = -4;
                    result.Message =
                        "同步 worker 未生成结果文件。";
                    return result;
                }
                return SyncWorkerProtocol.Read(resultPath);
            }
            catch (Exception ex)
            {
                result.ExitCode = -4;
                result.Message =
                    "无法运行同步 worker：" + ErrorText.Safe(ex);
                return result;
            }
            finally
            {
                try
                {
                    if (File.Exists(resultPath))
                    {
                        File.Delete(resultPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static string QuoteArgument(string value)
        {
            if (value.IndexOf('"') >= 0)
            {
                throw new InvalidOperationException("路径中不能包含双引号。");
            }
            return "\"" + value + "\"";
        }

        private static void WriteStatus(AppConfig config, SyncResult result)
        {
            try
            {
                string path = Path.Combine(result.Destination, ".ubuntu-win-sync-status.xml");
                string temporary = path + ".tmp";
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = new UTF8Encoding(false);
                settings.Indent = true;
                using (XmlWriter writer = XmlWriter.Create(temporary, settings))
                {
                    writer.WriteStartElement("UbuntuWinSyncStatus");
                    writer.WriteAttributeString("version", "1");
                    writer.WriteElementString("ClientId", config.ClientId);
                    writer.WriteElementString("Computer", Environment.MachineName);
                    writer.WriteElementString("Profile", config.ProfileName);
                    writer.WriteElementString("UpdatedUtc", DateTime.UtcNow.ToString("o"));
                    writer.WriteEndElement();
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            catch (Exception ex)
            {
                Log.Write("status-write", ex);
            }
        }
    }

    internal sealed class QueueResult
    {
        public bool Success;
        public string JobId;
        public string Message;
    }

    internal sealed class UploadResultStatus
    {
        public string JobId;
        public string Status;
        public string UpdatedUtc;
        public string Message;
        public int UploadedFiles;

        public string Signature
        {
            get
            {
                return (JobId ?? "") + "|" +
                       (Status ?? "") + "|" +
                       (UpdatedUtc ?? "");
            }
        }
    }

    internal static class CredentialLifecycle
    {
        public static bool ClearNasPasswordAfterSuccessfulUpload(AppConfig config)
        {
            if (config == null || Text.IsBlank(config.NasPassword))
            {
                return false;
            }
            config.NasPassword = "";
            return true;
        }
    }

    internal static class ConfigLifecycle
    {
        public static void ApplySettingsChange(
            AppConfig previous,
            AppConfig current)
        {
            if (previous == null || current == null)
            {
                return;
            }

            bool shareChanged = !SameIgnoreCase(
                previous.ShareUnc,
                current.ShareUnc);
            bool syncIdentityChanged =
                shareChanged ||
                !SameIgnoreCase(
                    previous.SourceFolder,
                    current.SourceFolder) ||
                !SameOrdinal(
                    previous.ProfileName,
                    current.ProfileName);
            bool uploadTargetChanged =
                syncIdentityChanged ||
                previous.NasPort != current.NasPort ||
                !SameIgnoreCase(
                    previous.NasHost,
                    current.NasHost) ||
                !SameOrdinal(
                    previous.NasUsername,
                    current.NasUsername) ||
                !SameOrdinal(
                    previous.NasRemoteRoot,
                    current.NasRemoteRoot) ||
                !SameOrdinal(
                    previous.NasPassword,
                    current.NasPassword);

            if (syncIdentityChanged)
            {
                current.InitialSyncCompleted = false;
            }
            if (uploadTargetChanged)
            {
                current.UploadPromptShown = false;
                current.AutoUploadEnabled = false;
                current.UploadPending = false;
                current.LastQueuedJobId = "";
                current.LastQueuedUtc = "";
            }
            if (shareChanged)
            {
                current.UploadPublicKeyFingerprint = "";
            }
        }

        private static bool SameIgnoreCase(
            string left,
            string right)
        {
            return string.Equals(
                Normalize(left),
                Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameOrdinal(
            string left,
            string right)
        {
            return string.Equals(
                Normalize(left),
                Normalize(right),
                StringComparison.Ordinal);
        }

        private static string Normalize(string value)
        {
            return value == null ? "" : value.Trim();
        }
    }

    internal static class UploadQueue
    {
        private const string ControlDirectory = ".ubuntu-win-share";
        private const string PublicKeyFile = "nas-upload-public.xml";

        public static string ControlRoot(AppConfig config)
        {
            return Path.Combine(config.ShareUnc, ControlDirectory);
        }

        public static string PublicKeyPath(AppConfig config)
        {
            return Path.Combine(
                Path.Combine(ControlRoot(config), "keys"),
                PublicKeyFile);
        }

        public static string PublicKeyFingerprint(AppConfig config)
        {
            byte[] data = File.ReadAllBytes(PublicKeyPath(config));
            using (SHA256Managed sha = new SHA256Managed())
            {
                return Hex(sha.ComputeHash(data));
            }
        }

        public static UploadResultStatus ReadResult(AppConfig config)
        {
            if (Text.IsBlank(config.LastQueuedJobId))
            {
                return null;
            }
            string path = Path.Combine(
                Path.Combine(
                    Path.Combine(ControlRoot(config), "jobs"),
                    "results"),
                ResultFileName(config, config.LastQueuedJobId));
            if (!File.Exists(path))
            {
                return null;
            }

            XmlDocument document = SecureXml.LoadDocument(path);
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "NasUploadResult")
            {
                return null;
            }

            UploadResultStatus result = new UploadResultStatus();
            result.JobId = NodeText(root, "JobId");
            result.Status = NodeText(root, "Status");
            result.UpdatedUtc = NodeText(root, "UpdatedUtc");
            result.Message = NodeText(root, "Message");
            int uploaded;
            if (int.TryParse(NodeText(root, "UploadedFiles"), out uploaded))
            {
                result.UploadedFiles = uploaded;
            }
            return result;
        }

        private static string NodeText(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            return node == null ? "" : node.InnerText;
        }

        public static QueueResult Queue(AppConfig config)
        {
            return Queue(config, true);
        }

        public static QueueResult Queue(AppConfig config, bool persistConfig)
        {
            QueueResult result = new QueueResult();
            string source = SyncRunner.Destination(config);
            if (!Directory.Exists(source))
            {
                result.Success = false;
                result.Message = "共享同步目录尚不存在。";
                return result;
            }

            string keyPath = PublicKeyPath(config);
            if (!File.Exists(keyPath))
            {
                result.Success = false;
                result.Message = "Ubuntu 上传代理公钥不存在：" + keyPath;
                return result;
            }

            string fingerprint = PublicKeyFingerprint(config);
            if (!Text.IsBlank(config.UploadPublicKeyFingerprint) &&
                !string.Equals(
                    config.UploadPublicKeyFingerprint,
                    fingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = "Ubuntu 上传代理公钥已变化，已拒绝提交 NAS 凭据。";
                return result;
            }

            if (Text.IsBlank(config.UploadPublicKeyFingerprint))
            {
                config.UploadPublicKeyFingerprint = fingerprint;
                if (persistConfig)
                {
                    ConfigStore.Save(config);
                }
            }

            string encryptedPassword = "";
            string authMode = "ssh-key";
            if (!Text.IsBlank(config.NasPassword))
            {
                authMode = "password";
                byte[] plain = Encoding.UTF8.GetBytes(config.NasPassword);
                try
                {
                    RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
                    try
                    {
                        rsa.FromXmlString(
                            SecureXml.LoadRsaPublicKeyXml(keyPath));
                        byte[] encrypted = rsa.Encrypt(plain, true);
                        encryptedPassword = Convert.ToBase64String(encrypted);
                    }
                    finally
                    {
                        rsa.Clear();
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = "无法加密 NAS 密码：" + ErrorText.Safe(ex);
                    return result;
                }
                finally
                {
                    Array.Clear(plain, 0, plain.Length);
                }
            }

            string jobId = Guid.NewGuid().ToString("N");
            string machine = Text.SafeSegment(Environment.MachineName);
            string profile = Text.SafeSegment(config.ProfileName);
            string queueRoot = Path.Combine(ControlRoot(config), "jobs");
            string incoming = Path.Combine(queueRoot, "incoming");
            string results = Path.Combine(queueRoot, "results");
            Directory.CreateDirectory(incoming);
            Directory.CreateDirectory(results);

            string jobName = JobFileName(config, jobId);
            string finalPath = Path.Combine(incoming, jobName);
            string temporary = finalPath + "." + jobId + ".tmp";

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            settings.Indent = true;
            using (XmlWriter writer = XmlWriter.Create(temporary, settings))
            {
                writer.WriteStartElement("NasUploadJob");
                writer.WriteAttributeString("version", "1");
                writer.WriteElementString("JobId", jobId);
                writer.WriteElementString("ClientId", config.ClientId);
                writer.WriteElementString("CreatedUtc", DateTime.UtcNow.ToString("o"));
                writer.WriteElementString("Computer", Environment.MachineName);
                writer.WriteElementString("Profile", config.ProfileName);
                writer.WriteElementString(
                    "SourceRelative",
                    Text.ToForwardSlashes(
                        Path.Combine(
                            Path.Combine("Win7Sync", machine),
                            profile)));
                writer.WriteElementString("NasHost", config.NasHost);
                writer.WriteElementString(
                    "NasPort",
                    config.NasPort.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteElementString("NasUsername", config.NasUsername);
                writer.WriteElementString("NasAuthMode", authMode);
                writer.WriteElementString(
                    "NasRemotePath",
                    Text.CombineRemote(config.NasRemoteRoot, machine, profile));
                writer.WriteElementString(
                    "NasPasswordCipher",
                    encryptedPassword);
                writer.WriteElementString("CipherAlgorithm", "RSA-OAEP-SHA1");
                writer.WriteElementString("PublicKeySha256", fingerprint);
                writer.WriteElementString("DeleteRemoteFiles", "false");
                writer.WriteEndElement();
            }

            PublishJob(temporary, finalPath);

            config.LastQueuedJobId = jobId;
            config.LastQueuedUtc = DateTime.UtcNow.ToString("o");
            if (persistConfig)
            {
                ConfigStore.Save(config);
            }

            result.Success = true;
            result.JobId = jobId;
            result.Message = "已提交 Ubuntu NAS 上传任务。";
            return result;
        }

        internal static string JobFileName(AppConfig config, string jobId)
        {
            return Text.SafeSegment(config.ClientId) + "-" +
                   Text.SafeSegment(config.ProfileName) + "-" +
                   Text.SafeSegment(jobId) + ".job.xml";
        }

        internal static string ResultFileName(AppConfig config, string jobId)
        {
            return Text.SafeSegment(config.ClientId) + "-" +
                   Text.SafeSegment(config.ProfileName) + "-" +
                   Text.SafeSegment(jobId) + ".result.xml";
        }

        internal static void PublishJob(string temporary, string finalPath)
        {
            Exception lastError = null;
            int attempt;
            for (attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Move(temporary, finalPath);
                    return;
                }
                catch (FileNotFoundException)
                {
                    if (!File.Exists(temporary))
                    {
                        throw;
                    }
                    lastError = null;
                }
                catch (IOException ex)
                {
                    if (!File.Exists(temporary))
                    {
                        throw;
                    }
                    lastError = ex;
                }
                Thread.Sleep(100);
            }

            throw new IOException(
                "上传队列正忙，无法在两秒内发布任务，请稍后重试。",
                lastError);
        }

        private static string Hex(byte[] bytes)
        {
            StringBuilder text = new StringBuilder(bytes.Length * 2);
            int i;
            for (i = 0; i < bytes.Length; i++)
            {
                text.Append(bytes[i].ToString("x2"));
            }
            return text.ToString();
        }
    }
}
