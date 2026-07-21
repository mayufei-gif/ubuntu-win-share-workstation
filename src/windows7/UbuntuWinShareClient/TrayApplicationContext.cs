using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace UbuntuWinShareClient
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private AppConfig _config;
        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _statusItem;
        private readonly Timer _timer;
        private readonly BackgroundWorker _worker;
        private readonly BackgroundWorker _resultWorker;
        private FileSystemWatcher _watcher;
        private volatile bool _syncRequested;
        private DateTime _nextPeriodicSync;
        private DateTime _retryNotBefore;
        private long _lastWatcherEventTicks;
        private DateTime _nextResultCheck;
        private string _lastUploadResultSignature = "";
        private readonly System.Threading.EventWaitHandle _exitEvent;

        public TrayApplicationContext(
            AppConfig config,
            System.Threading.EventWaitHandle exitEvent)
        {
            _config = config;
            _exitEvent = exitEvent;

            ContextMenuStrip menu = new ContextMenuStrip();
            _statusItem = new ToolStripMenuItem("状态：等待首次同步");
            _statusItem.Enabled = false;
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("立即同步", null, new EventHandler(SyncNow));
            menu.Items.Add("立即上传到 NAS", null, new EventHandler(UploadNow));
            menu.Items.Add("打开本地目录", null, new EventHandler(OpenSource));
            menu.Items.Add("打开共享目录", null, new EventHandler(OpenDestination));
            menu.Items.Add("设置", null, new EventHandler(OpenSettings));
            menu.Items.Add("查看日志", null, new EventHandler(OpenLog));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("卸载", null, new EventHandler(Uninstall));
            menu.Items.Add("退出", null, new EventHandler(Exit));

            _tray = new NotifyIcon();
            _tray.Icon = SystemIcons.Application;
            _tray.Text = "Ubuntu Win Share";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += new EventHandler(OpenSettings);

            _worker = new BackgroundWorker();
            _worker.DoWork += new DoWorkEventHandler(WorkerDoWork);
            _worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(WorkerCompleted);

            _resultWorker = new BackgroundWorker();
            _resultWorker.DoWork += new DoWorkEventHandler(ResultWorkerDoWork);
            _resultWorker.RunWorkerCompleted +=
                new RunWorkerCompletedEventHandler(ResultWorkerCompleted);

            _timer = new Timer();
            _timer.Interval = 3000;
            _timer.Tick += new EventHandler(TimerTick);
            _timer.Start();

            ConfigureWatcher();
            _syncRequested = true;
            _nextPeriodicSync = DateTime.Now;
            _retryNotBefore = DateTime.MinValue;
            _nextResultCheck = DateTime.Now;
            ShowBalloon("Ubuntu Win Share", "后台同步已启动。", ToolTipIcon.Info);
        }

        private void ConfigureWatcher()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            if (!Directory.Exists(_config.SourceFolder)) return;
            try
            {
                _watcher = new FileSystemWatcher(_config.SourceFolder);
                _watcher.IncludeSubdirectories = true;
                _watcher.NotifyFilter =
                    NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size;
                _watcher.Changed += new FileSystemEventHandler(WatcherEvent);
                _watcher.Created += new FileSystemEventHandler(WatcherEvent);
                _watcher.Deleted += new FileSystemEventHandler(WatcherEvent);
                _watcher.Renamed += new RenamedEventHandler(WatcherRenamed);
                _watcher.Error += new ErrorEventHandler(WatcherError);
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.Write("watcher", ex);
            }
        }

        private void WatcherEvent(object sender, FileSystemEventArgs e)
        {
            System.Threading.Interlocked.Exchange(
                ref _lastWatcherEventTicks,
                DateTime.Now.Ticks);
            _syncRequested = true;
        }

        private void WatcherRenamed(object sender, RenamedEventArgs e)
        {
            System.Threading.Interlocked.Exchange(
                ref _lastWatcherEventTicks,
                DateTime.Now.Ticks);
            _syncRequested = true;
        }

        private void WatcherError(object sender, ErrorEventArgs e)
        {
            Log.Write("watcher-overflow", e.GetException());
            _syncRequested = true;
        }

        private void TimerTick(object sender, EventArgs e)
        {
            if (_exitEvent.WaitOne(0, false))
            {
                if (!_worker.IsBusy && !_resultWorker.IsBusy)
                {
                    ExitThread();
                }
                return;
            }

            if (!_resultWorker.IsBusy && DateTime.Now >= _nextResultCheck)
            {
                _nextResultCheck = DateTime.Now.AddSeconds(15);
                _resultWorker.RunWorkerAsync();
            }
            if (_worker.IsBusy) return;

            long eventTicks = System.Threading.Interlocked.Read(
                ref _lastWatcherEventTicks);
            bool quiet = eventTicks == 0 ||
                         DateTime.Now.Subtract(
                             new DateTime(eventTicks)).TotalSeconds >= 2;
            DateTime now = DateTime.Now;
            if (SyncSchedule.ShouldStart(
                    now,
                    _retryNotBefore,
                    _syncRequested,
                    quiet,
                    _nextPeriodicSync))
            {
                _syncRequested = false;
                _nextPeriodicSync = now.AddSeconds(_config.SyncIntervalSeconds);
                _statusItem.Text = "状态：正在同步";
                _worker.RunWorkerAsync();
            }
        }

        private void ResultWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                if (Text.IsBlank(_config.LastQueuedJobId))
                {
                    return;
                }
                e.Result = UploadQueue.ReadResult(_config);
            }
            catch (Exception ex)
            {
                Log.Write("upload-result", ex);
            }
        }

        private void ResultWorkerCompleted(
            object sender,
            RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                Log.Write("upload-result-worker", e.Error);
                return;
            }

            UploadResultStatus result = e.Result as UploadResultStatus;
            if (result == null ||
                !string.Equals(
                    result.JobId,
                    _config.LastQueuedJobId,
                    StringComparison.OrdinalIgnoreCase) ||
                result.Signature == _lastUploadResultSignature)
            {
                return;
            }

            _lastUploadResultSignature = result.Signature;
            if (string.Equals(
                    result.Status,
                    "success",
                    StringComparison.OrdinalIgnoreCase))
            {
                bool passwordRemoved =
                    CredentialLifecycle.ClearNasPasswordAfterSuccessfulUpload(
                        _config);
                bool persistedCopiesSanitized =
                    ConfigStore.EnsureNasPasswordRemoved(_config);
                if (passwordRemoved || persistedCopiesSanitized)
                {
                    Log.Write(
                        "credentials",
                        "NAS password removed after SSH key upload succeeded.");
                }
                _statusItem.Text = "状态：NAS 上传完成";
                ShowBalloon(
                    "NAS 上传完成",
                    "本次上传文件数：" + result.UploadedFiles,
                    ToolTipIcon.Info);
            }
            else if (string.Equals(
                         result.Status,
                         "failed",
                         StringComparison.OrdinalIgnoreCase))
            {
                _statusItem.Text = "状态：NAS 上传失败";
                ShowBalloon(
                    "NAS 上传失败",
                    result.Message,
                    ToolTipIcon.Error);
            }
            else if (string.Equals(
                         result.Status,
                         "retrying",
                         StringComparison.OrdinalIgnoreCase))
            {
                _statusItem.Text = "状态：等待 NAS 恢复";
            }
        }

        private void WorkerDoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = SyncRunner.Run(_config, _exitEvent);
        }

        private void WorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                Log.Write("sync-worker", e.Error);
                _statusItem.Text = "状态：同步异常";
                return;
            }

            SyncResult result = (SyncResult)e.Result;
            Log.Write("sync", result.Message);
            _statusItem.Text = result.Success
                ? "状态：同步正常"
                : "状态：等待网络恢复";
            _tray.Text = result.Success
                ? "Ubuntu Win Share - 正常"
                : "Ubuntu Win Share - 离线";

            if (!result.Success)
            {
                _syncRequested = true;
                _retryNotBefore = SyncSchedule.OfflineRetryAt(
                    DateTime.Now,
                    _config.SyncIntervalSeconds);
                _nextPeriodicSync = _retryNotBefore;
                return;
            }

            _retryNotBefore = DateTime.MinValue;
            bool queuedFromPrompt = false;
            if (!_config.InitialSyncCompleted)
            {
                _config.InitialSyncCompleted = true;
                ConfigStore.Save(_config);
            }

            if (!_config.UploadPromptShown)
            {
                _config.UploadPromptShown = true;
                ConfigStore.Save(_config);
                DialogResult answer = MessageBox.Show(
                    "本地文件已同步到 Ubuntu 共享盘。\r\n\r\n点击“确定”后，Ubuntu 会把当前目录上传到已配置的 NAS 账号，并在后续有新改动时继续自动提交上传任务。",
                    "启用 NAS 自动上传",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question);
                if (answer == DialogResult.OK)
                {
                    _config.AutoUploadEnabled = true;
                    _config.UploadPending = true;
                    ConfigStore.Save(_config);
                    QueueUpload(true);
                    queuedFromPrompt = true;
                }
            }

            if (!queuedFromPrompt && result.Changed && _config.AutoUploadEnabled)
            {
                _config.UploadPending = true;
                ConfigStore.Save(_config);
            }

            if (!queuedFromPrompt &&
                _config.AutoUploadEnabled &&
                _config.UploadPending)
            {
                QueueUpload(false);
            }
        }

        private void QueueUpload(bool notifySuccess)
        {
            try
            {
                QueueResult queued = UploadQueue.Queue(_config);
                Log.Write("upload-queue", queued.Message);
                if (queued.Success)
                {
                    _config.UploadPending = false;
                    ConfigStore.Save(_config);
                    _statusItem.Text = "状态：已提交 NAS 上传";
                    if (notifySuccess)
                    {
                        ShowBalloon(
                            "NAS 上传已提交",
                            "Ubuntu 上传代理会在后台处理。",
                            ToolTipIcon.Info);
                    }
                }
                else
                {
                    ShowBalloon(
                        "NAS 上传未提交",
                        queued.Message,
                        ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Write("upload-queue", ex);
                ShowBalloon("NAS 上传未提交", ErrorText.Safe(ex), ToolTipIcon.Error);
            }
        }

        private void SyncNow(object sender, EventArgs e)
        {
            _syncRequested = true;
            _retryNotBefore = DateTime.MinValue;
            System.Threading.Interlocked.Exchange(
                ref _lastWatcherEventTicks,
                0);
            TimerTick(this, EventArgs.Empty);
        }

        private void UploadNow(object sender, EventArgs e)
        {
            _config.AutoUploadEnabled = true;
            _config.UploadPending = true;
            ConfigStore.Save(_config);
            QueueUpload(true);
        }

        private void OpenSource(object sender, EventArgs e)
        {
            OpenPath(_config.SourceFolder);
        }

        private void OpenDestination(object sender, EventArgs e)
        {
            OpenPath(SyncRunner.Destination(_config));
        }

        private void OpenSettings(object sender, EventArgs e)
        {
            using (SetupForm setup = new SetupForm(
                _config,
                _exitEvent))
            {
                if (setup.ShowDialog() == DialogResult.OK)
                {
                    _config = setup.SavedConfig;
                    ConfigureWatcher();
                    _syncRequested = true;
                    _retryNotBefore = DateTime.MinValue;
                    System.Threading.Interlocked.Exchange(
                        ref _lastWatcherEventTicks,
                        0);
                }
            }
        }

        private void OpenLog(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (!File.Exists(AppPaths.LogFile))
                {
                    File.WriteAllText(
                        AppPaths.LogFile,
                        "No log entries yet." + Environment.NewLine,
                        Encoding.UTF8);
                }
                Process.Start("notepad.exe", Text.Quote(AppPaths.LogFile));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ErrorText.Safe(ex), "打开日志失败");
            }
        }

        private void OpenPath(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    MessageBox.Show("目录当前不可用：" + path, "Ubuntu Win Share");
                    return;
                }
                Process.Start("explorer.exe", Text.Quote(path));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ErrorText.Safe(ex), "打开目录失败");
            }
        }

        private void Uninstall(object sender, EventArgs e)
        {
            if (!SelfInstaller.LaunchUninstaller())
            {
                MessageBox.Show(
                    "无法启动独立卸载器，请重新运行安装包修复后再试。",
                    "卸载 Ubuntu Win Share",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void Exit(object sender, EventArgs e)
        {
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
            _timer.Stop();
            _timer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            base.ExitThreadCore();
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text.Length > 240 ? text.Substring(0, 240) : text;
                _tray.BalloonTipIcon = icon;
                _tray.ShowBalloonTip(5000);
            }
            catch
            {
            }
        }
    }
}
