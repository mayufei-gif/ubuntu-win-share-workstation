using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace UbuntuWinShareClient
{
    internal static class SelfTest
    {
        public static int Run(string resultPath)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "ubuntu-win-share-client-self-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                TestProtectedConfiguration();
                TestAtomicConfiguration(root);
                TestRsaEncryption();
                TestRobocopy(root);
                TestProcessSupervisor(root);
                TestSyncWorkerProtocol(root);
                TestSyncSchedule();
                TestRobocopyExitCodes();
                TestCredentialLifecycle();
                TestConfigClone();
                TestQueuePublish(root);
                TestUninstallerContract();
                TestWindowsVersionGate();
                WriteResult(resultPath, "WIN7_CLIENT_SELF_TEST_OK");
                return 0;
            }
            catch (Exception ex)
            {
                WriteResult(resultPath, "WIN7_CLIENT_SELF_TEST_FAILED " + ErrorText.Safe(ex));
                return 1;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
                catch
                {
                }
            }
        }

        public static int RunSyncProbe(string source, string target, string resultPath)
        {
            try
            {
                if (Text.IsBlank(source) || Text.IsBlank(target))
                    throw new InvalidOperationException("--source and --target are required.");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(target);
                string probe = Path.Combine(source, "ubuntu-win-share-probe.txt");
                string expected = Guid.NewGuid().ToString("N");
                File.WriteAllText(probe, expected, Encoding.UTF8);
                RunRobocopy(source, target);
                string copied = File.ReadAllText(
                    Path.Combine(target, "ubuntu-win-share-probe.txt"),
                    Encoding.UTF8);
                if (copied != expected)
                    throw new InvalidOperationException("Probe content mismatch.");
                WriteResult(resultPath, "WIN7_CLIENT_SYNC_PROBE_OK");
                return 0;
            }
            catch (Exception ex)
            {
                WriteResult(resultPath, "WIN7_CLIENT_SYNC_PROBE_FAILED " + ErrorText.Safe(ex));
                return 1;
            }
        }

        public static int RunOperatingSystemProbe(string resultPath)
        {
            try
            {
                bool allowed = WindowsVersion.IsWindows7Sp1();
                WriteResult(
                    resultPath,
                    "WIN7_OS_PROBE " +
                    (allowed ? "ALLOWED" : "REJECTED") +
                    " version=" +
                    WindowsVersion.DescribeCurrent());
                return allowed ? 0 : 3;
            }
            catch (Exception ex)
            {
                WriteResult(
                    resultPath,
                    "WIN7_OS_PROBE_FAILED " +
                    ErrorText.Safe(ex));
                return 1;
            }
        }

        public static int RunQueueProbe(
            string share,
            string profile,
            string nasHost,
            string nasUser,
            string nasPassword,
            string nasRemote,
            string resultPath)
        {
            try
            {
                if (Text.IsBlank(share) ||
                    Text.IsBlank(profile) ||
                    Text.IsBlank(nasHost) ||
                    Text.IsBlank(nasUser) ||
                    Text.IsBlank(nasRemote))
                {
                    throw new InvalidOperationException(
                        "--share, --profile, --nas-host, --nas-user and --nas-remote are required.");
                }

                AppConfig config = new AppConfig();
                config.ClientId = "codexacceptance";
                config.ProfileName = profile;
                config.ShareUnc = share.TrimEnd('\\');
                config.NasHost = nasHost;
                config.NasUsername = nasUser;
                config.NasPassword = nasPassword == null ? "" : nasPassword;
                config.NasRemoteRoot = nasRemote;
                config.UploadPublicKeyFingerprint = "";
                QueueResult queued = UploadQueue.Queue(config, false);
                if (!queued.Success)
                {
                    throw new InvalidOperationException(queued.Message);
                }
                WriteResult(
                    resultPath,
                    "WIN7_CLIENT_QUEUE_PROBE_OK job_id=" + queued.JobId);
                return 0;
            }
            catch (Exception ex)
            {
                WriteResult(
                    resultPath,
                    "WIN7_CLIENT_QUEUE_PROBE_FAILED " + ErrorText.Safe(ex));
                return 1;
            }
        }

        private static void TestProtectedConfiguration()
        {
            AppConfig config = new AppConfig();
            config.SharePassword = "smb-test-" + Guid.NewGuid().ToString("N");
            config.NasPassword = "nas-test-" + Guid.NewGuid().ToString("N");

            XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
            byte[] plain;
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, config);
                plain = stream.ToArray();
            }

            byte[] entropy = Encoding.UTF8.GetBytes("UbuntuWinShare.SelfTest");
            byte[] encrypted = ProtectedData.Protect(
                plain,
                entropy,
                DataProtectionScope.CurrentUser);
            byte[] restored = ProtectedData.Unprotect(
                encrypted,
                entropy,
                DataProtectionScope.CurrentUser);
            AppConfig read;
            using (MemoryStream stream = new MemoryStream(restored))
            {
                read = (AppConfig)serializer.Deserialize(stream);
            }
            if (read.SharePassword != config.SharePassword ||
                read.NasPassword != config.NasPassword)
            {
                throw new InvalidOperationException("DPAPI round trip failed.");
            }
            Array.Clear(plain, 0, plain.Length);
            Array.Clear(restored, 0, restored.Length);
        }

        private static void TestAtomicConfiguration(string root)
        {
            string configPath = Path.Combine(root, "config.dat");
            string backupPath = configPath + ".bak";

            AppConfig first = new AppConfig();
            first.ProfileName = "first";
            first.SharePassword = "first-smb-secret";
            ConfigStore.SaveToFiles(
                first,
                configPath,
                backupPath);
            if (!File.Exists(configPath) ||
                !File.Exists(backupPath))
            {
                throw new InvalidOperationException(
                    "The first configuration save did not create both copies.");
            }
            AppConfig firstBackup = ConfigStore.LoadFromFiles(
                backupPath,
                backupPath + ".missing",
                false);
            if (firstBackup == null ||
                firstBackup.ProfileName != "first" ||
                firstBackup.SharePassword != "first-smb-secret")
            {
                throw new InvalidOperationException(
                    "The first configuration backup is not readable.");
            }

            AppConfig second = first.Clone();
            second.ProfileName = "second";
            second.SharePassword = "second-smb-secret";
            ConfigStore.SaveToFiles(
                second,
                configPath,
                backupPath);

            AppConfig current = ConfigStore.LoadFromFiles(
                configPath,
                backupPath,
                false);
            if (current == null ||
                current.ProfileName != "second" ||
                current.SharePassword != "second-smb-secret")
            {
                throw new InvalidOperationException(
                    "Atomic configuration did not preserve the current file.");
            }

            File.WriteAllText(
                configPath,
                "corrupt",
                Encoding.ASCII);
            AppConfig recovered = ConfigStore.LoadFromFiles(
                configPath,
                backupPath,
                false);
            if (recovered == null ||
                recovered.ProfileName != "first" ||
                recovered.SharePassword != "first-smb-secret")
            {
                throw new InvalidOperationException(
                    "Configuration backup recovery failed.");
            }

            AppConfig restored = ConfigStore.LoadFromFiles(
                configPath,
                backupPath,
                false);
            if (restored == null ||
                restored.ProfileName != "first")
            {
                throw new InvalidOperationException(
                    "Recovered configuration was not restored to the main file.");
            }

            string[] stagingFiles = Directory.GetFiles(
                root,
                "config.dat.*.tmp");
            if (stagingFiles.Length != 0)
            {
                throw new InvalidOperationException(
                    "Atomic configuration left temporary files behind.");
            }
        }

        private static void TestRsaEncryption()
        {
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048);
            try
            {
                string publicXml = rsa.ToXmlString(false);
                RSACryptoServiceProvider publicOnly = new RSACryptoServiceProvider();
                try
                {
                    publicOnly.FromXmlString(publicXml);
                    byte[] secret = Encoding.UTF8.GetBytes("NAS_TEST_SECRET");
                    byte[] encrypted = publicOnly.Encrypt(secret, true);
                    byte[] restored = rsa.Decrypt(encrypted, true);
                    string value = Encoding.UTF8.GetString(restored);
                    Array.Clear(secret, 0, secret.Length);
                    Array.Clear(restored, 0, restored.Length);
                    if (value != "NAS_TEST_SECRET")
                        throw new InvalidOperationException("RSA OAEP round trip failed.");
                }
                finally
                {
                    publicOnly.Clear();
                }
            }
            finally
            {
                rsa.Clear();
            }
        }

        private static void TestRobocopy(string root)
        {
            string source = Path.Combine(root, "source");
            string target = Path.Combine(root, "target");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(
                Path.Combine(source, "nested\\sample.txt"),
                "ubuntu-win-share",
                Encoding.UTF8);
            RunRobocopy(source, target);
            string copied = Path.Combine(target, "nested\\sample.txt");
            if (!File.Exists(copied))
                throw new InvalidOperationException("Robocopy did not copy the file.");
        }

        private static void TestSyncSchedule()
        {
            DateTime now = new DateTime(2026, 7, 19, 12, 0, 0);
            DateTime retryAt = now.AddSeconds(30);
            if (SyncSchedule.ShouldStart(
                    now,
                    retryAt,
                    true,
                    true,
                    now))
            {
                throw new InvalidOperationException(
                    "Offline retry gate allowed an early sync.");
            }
            if (!SyncSchedule.ShouldStart(
                    retryAt,
                    retryAt,
                    true,
                    true,
                    retryAt))
            {
                throw new InvalidOperationException(
                    "Offline retry gate blocked a due sync.");
            }
            if (SyncSchedule.OfflineRetryAt(now, 15) != now.AddSeconds(30))
            {
                throw new InvalidOperationException(
                    "Offline retry minimum is not 30 seconds.");
            }
        }

        private static void TestCredentialLifecycle()
        {
            AppConfig config = new AppConfig();
            config.NasPassword = "temporary-password";
            if (!CredentialLifecycle.ClearNasPasswordAfterSuccessfulUpload(config))
            {
                throw new InvalidOperationException(
                    "NAS password cleanup did not report a change.");
            }
            if (!Text.IsBlank(config.NasPassword))
            {
                throw new InvalidOperationException(
                    "NAS password cleanup left the password in memory.");
            }
            if (CredentialLifecycle.ClearNasPasswordAfterSuccessfulUpload(config))
            {
                throw new InvalidOperationException(
                    "NAS password cleanup was not idempotent.");
            }
        }

        private static void TestRobocopyExitCodes()
        {
            if (RobocopyExitCode.HasCopiedFiles(0))
                throw new InvalidOperationException("Robocopy code 0 is not a copy.");
            if (!RobocopyExitCode.HasCopiedFiles(1))
                throw new InvalidOperationException("Robocopy code 1 must report a copy.");
            if (RobocopyExitCode.HasCopiedFiles(2))
                throw new InvalidOperationException(
                    "Robocopy extras must not trigger a NAS upload.");
            if (!RobocopyExitCode.HasCopiedFiles(3))
                throw new InvalidOperationException(
                    "Robocopy copied-plus-extra code must report a copy.");
        }

        private static void TestConfigClone()
        {
            AppConfig original = new AppConfig();
            original.ClientId = "client";
            original.ProfileName = "original";
            original.NasPassword = "temporary-password";
            original.AutoUploadEnabled = true;
            AppConfig clone = original.Clone();
            clone.ProfileName = "edited";
            clone.NasPassword = "";
            if (original.ProfileName != "original" ||
                original.NasPassword != "temporary-password" ||
                clone.ClientId != original.ClientId ||
                !clone.AutoUploadEnabled)
            {
                throw new InvalidOperationException(
                    "Configuration edit clone is incomplete or aliases the original.");
            }
        }

        private static void TestQueuePublish(string root)
        {
            string queueRoot = Path.Combine(root, "queue");
            Directory.CreateDirectory(queueRoot);
            string firstPath = Path.Combine(queueRoot, "client-profile-first.job.xml");
            string secondPath = Path.Combine(queueRoot, "client-profile-second.job.xml");
            string first = firstPath + ".tmp";
            string second = secondPath + ".tmp";
            File.WriteAllText(first, "first", Encoding.UTF8);
            UploadQueue.PublishJob(first, firstPath);
            File.WriteAllText(second, "second", Encoding.UTF8);
            UploadQueue.PublishJob(second, secondPath);
            if (File.ReadAllText(firstPath, Encoding.UTF8) != "first" ||
                File.ReadAllText(secondPath, Encoding.UTF8) != "second")
            {
                throw new InvalidOperationException(
                    "Unique queue files were not published.");
            }
            if (File.Exists(first) || File.Exists(second))
            {
                throw new InvalidOperationException(
                    "Queue replacement left a staging file behind.");
            }

            AppConfig config = new AppConfig();
            config.ClientId = "client";
            config.ProfileName = "profile";
            string jobName = UploadQueue.JobFileName(config, "abc123");
            string resultName = UploadQueue.ResultFileName(config, "abc123");
            if (jobName != "client-profile-abc123.job.xml" ||
                resultName != "client-profile-abc123.result.xml")
            {
                throw new InvalidOperationException(
                    "Unique queue filename contract changed.");
            }
        }

        private static void TestProcessSupervisor(string root)
        {
            string command = Environment.GetEnvironmentVariable("COMSPEC");
            if (Text.IsBlank(command))
            {
                command = "cmd.exe";
            }

            ProcessStartInfo quickStart = new ProcessStartInfo();
            quickStart.FileName = command;
            quickStart.Arguments = "/d /c exit 7";
            quickStart.CreateNoWindow = true;
            quickStart.UseShellExecute = false;
            using (Process quick = Process.Start(quickStart))
            {
                ProcessWaitResult quickResult =
                    ProcessSupervisor.Wait(quick, null, 5000);
                if (!quickResult.Exited ||
                    quickResult.Stopped ||
                    quickResult.TimedOut ||
                    quickResult.ExitCode != 7)
                {
                    throw new InvalidOperationException(
                        "Process supervisor lost a normal exit code.");
                }
            }

            ProcessStartInfo timeoutStart = new ProcessStartInfo();
            timeoutStart.FileName = command;
            timeoutStart.Arguments =
                "/d /c ping 127.0.0.1 -n 30 >nul";
            timeoutStart.CreateNoWindow = true;
            timeoutStart.UseShellExecute = false;
            using (Process hanging = Process.Start(timeoutStart))
            {
                ProcessWaitResult timeoutResult =
                    ProcessSupervisor.Wait(hanging, null, 500);
                if (!timeoutResult.TimedOut ||
                    !timeoutResult.Exited ||
                    !hanging.HasExited)
                {
                    throw new InvalidOperationException(
                        "Process supervisor did not terminate a timeout.");
                }
            }

            using (ManualResetEvent stop = new ManualResetEvent(true))
            {
                ProcessStartInfo stoppedStart =
                    new ProcessStartInfo();
                stoppedStart.FileName = command;
                stoppedStart.Arguments =
                    "/d /c ping 127.0.0.1 -n 30 >nul";
                stoppedStart.CreateNoWindow = true;
                stoppedStart.UseShellExecute = false;
                using (Process stopped = Process.Start(stoppedStart))
                {
                    ProcessWaitResult stoppedResult =
                        ProcessSupervisor.Wait(
                            stopped,
                            stop,
                            5000);
                    if (!stoppedResult.Stopped ||
                        stoppedResult.TimedOut ||
                        !stoppedResult.Exited ||
                        !stopped.HasExited)
                    {
                        throw new InvalidOperationException(
                            "Process supervisor ignored the stop signal.");
                    }
                }
            }

            string childStarted = Path.Combine(
                root,
                "supervisor-child-started.txt");
            string orphanMarker = Path.Combine(
                root,
                "supervisor-orphan-marker.txt");
            string childScript = Path.Combine(
                root,
                "supervisor-child.cmd");
            string parentScript = Path.Combine(
                root,
                "supervisor-parent.cmd");
            File.WriteAllText(
                childScript,
                "@echo off\r\n" +
                ">\"" + childStarted + "\" echo started\r\n" +
                "ping 127.0.0.1 -n 4 >nul\r\n" +
                ">\"" + orphanMarker + "\" echo orphan\r\n",
                Encoding.ASCII);
            File.WriteAllText(
                parentScript,
                "@echo off\r\n" +
                "start \"\" /b cmd.exe /d /c call \"" +
                childScript +
                "\"\r\n" +
                "ping 127.0.0.1 -n 30 >nul\r\n",
                Encoding.ASCII);

            ProcessStartInfo treeStart = new ProcessStartInfo();
            treeStart.FileName = command;
            treeStart.Arguments =
                "/d /c call \"" + parentScript + "\"";
            treeStart.CreateNoWindow = true;
            treeStart.UseShellExecute = false;
            using (Process tree = Process.Start(treeStart))
            {
                DateTime childDeadline =
                    DateTime.UtcNow.AddSeconds(5);
                while (!File.Exists(childStarted) &&
                       DateTime.UtcNow < childDeadline)
                {
                    Thread.Sleep(50);
                }
                if (!File.Exists(childStarted))
                {
                    throw new InvalidOperationException(
                        "Process supervisor child-process test did not start.");
                }

                ProcessWaitResult treeResult =
                    ProcessSupervisor.Wait(tree, null, 500);
                if (!treeResult.TimedOut ||
                    !treeResult.Exited ||
                    !tree.HasExited)
                {
                    throw new InvalidOperationException(
                        "Process supervisor did not terminate the process tree.");
                }
            }

            Thread.Sleep(4000);
            if (File.Exists(orphanMarker))
            {
                throw new InvalidOperationException(
                    "Process supervisor left a child process running.");
            }
        }

        private static void TestSyncWorkerProtocol(string root)
        {
            string path = Path.Combine(root, "sync-worker-result.xml");
            SyncResult expected = new SyncResult();
            expected.Success = false;
            expected.Changed = true;
            expected.ExitCode = -2;
            expected.Destination = "\\\\server\\share\\target";
            expected.Message = "timeout";
            SyncWorkerProtocol.Write(path, expected);
            SyncResult actual = SyncWorkerProtocol.Read(path);
            if (actual.Success != expected.Success ||
                actual.Changed != expected.Changed ||
                actual.ExitCode != expected.ExitCode ||
                actual.Destination != expected.Destination ||
                actual.Message != expected.Message)
            {
                throw new InvalidOperationException(
                    "Sync worker result protocol round trip failed.");
            }
        }

        private static void TestUninstallerContract()
        {
            if (AppSignals.ClientMutexName !=
                    "Local\\UbuntuWinShareClient.Singleton" ||
                AppSignals.ExitEventName !=
                    "Local\\UbuntuWinShareClient.Exit")
            {
                throw new InvalidOperationException(
                    "Client exit signal contract changed.");
            }
            if (!SelfInstaller.HasEmbeddedUninstaller())
            {
                throw new InvalidOperationException(
                    "Embedded uninstaller is missing.");
            }
        }

        private static void TestWindowsVersionGate()
        {
            if (!WindowsVersion.IsWindows7Sp1(6, 1, 7601) ||
                !WindowsVersion.IsWindows7Sp1(6, 1, 9999) ||
                !WindowsVersion.IsWindows7Sp1(6, 1, 7601, 1))
            {
                throw new InvalidOperationException(
                    "Windows 7 SP1 was rejected by the OS gate.");
            }
            if (WindowsVersion.IsWindows7Sp1(6, 1, 7600) ||
                WindowsVersion.IsWindows7Sp1(10, 0, 19045) ||
                WindowsVersion.IsWindows7Sp1(6, 2, 9200) ||
                WindowsVersion.IsWindows7Sp1(6, 1, 7601, 2))
            {
                throw new InvalidOperationException(
                    "A non-Windows 7 SP1 version passed the OS gate.");
            }
        }

        private static void RunRobocopy(string source, string target)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "robocopy.exe");
            start.Arguments =
                "\"" + source + "\" \"" + target + "\" " +
                "/E /COPY:DAT /DCOPY:T /FFT /Z /XJ /R:1 /W:1 " +
                "/NP /NFL /NDL /NJH /NJS";
            start.CreateNoWindow = true;
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            {
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode >= 8)
                    throw new InvalidOperationException(
                        "Robocopy failed with exit code " + process.ExitCode + ".");
            }
        }

        private static void WriteResult(string path, string value)
        {
            if (!Text.IsBlank(path))
            {
                File.WriteAllText(path, value + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
