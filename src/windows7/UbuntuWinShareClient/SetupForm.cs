using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace UbuntuWinShareClient
{
    internal sealed class SetupForm : Form
    {
        private readonly AppConfig _existing;
        private readonly System.Threading.EventWaitHandle _exitEvent;
        private readonly Timer _exitTimer;
        private TextBox _source;
        private TextBox _profile;
        private TextBox _shareUnc;
        private TextBox _drive;
        private TextBox _shareUser;
        private TextBox _sharePassword;
        private TextBox _nasHost;
        private NumericUpDown _nasPort;
        private TextBox _nasUser;
        private TextBox _nasPassword;
        private TextBox _nasRemote;
        private CheckBox _mirrorDeletes;
        private Label _status;

        public AppConfig SavedConfig { get; private set; }

        public SetupForm(
            AppConfig existing,
            System.Threading.EventWaitHandle exitEvent)
        {
            _existing = existing;
            _exitEvent = exitEvent;
            BuildUi();
            LoadValues(existing);

            _exitTimer = new Timer();
            _exitTimer.Interval = 500;
            _exitTimer.Tick += new EventHandler(ExitTimerTick);
            _exitTimer.Start();
        }

        private void ExitTimerTick(object sender, EventArgs e)
        {
            if (_exitEvent != null &&
                _exitEvent.WaitOne(0, false))
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _exitTimer != null)
            {
                _exitTimer.Stop();
                _exitTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void BuildUi()
        {
            Text = "Ubuntu Win Share 安装设置";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(690, 610);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(14);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            Label title = new Label();
            title.AutoSize = true;
            title.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
            title.Text = "Win7 文件同步与 NAS 上传";
            title.Margin = new Padding(0, 0, 0, 12);
            root.Controls.Add(title, 0, 0);

            GroupBox syncGroup = new GroupBox();
            syncGroup.Text = "本地到 Ubuntu 共享盘";
            syncGroup.Dock = DockStyle.Fill;
            syncGroup.Padding = new Padding(10);
            root.Controls.Add(syncGroup, 0, 1);

            TableLayoutPanel syncTable = CreateFieldTable();
            syncGroup.Controls.Add(syncTable);

            _source = AddBrowseField(syncTable, 0, "本地源文件夹");
            _profile = AddTextField(syncTable, 1, "同步名称");
            _shareUnc = AddTextField(syncTable, 2, "Ubuntu 共享 UNC");
            _drive = AddTextField(syncTable, 3, "映射盘符");
            _shareUser = AddTextField(syncTable, 4, "Samba 用户");
            _sharePassword = AddTextField(syncTable, 5, "Samba 密码");
            _sharePassword.UseSystemPasswordChar = true;
            _mirrorDeletes = new CheckBox();
            _mirrorDeletes.Text = "同步本地删除操作";
            _mirrorDeletes.AutoSize = true;
            _mirrorDeletes.Margin = new Padding(3, 5, 3, 3);
            syncTable.Controls.Add(_mirrorDeletes, 1, 6);
            syncTable.SetColumnSpan(_mirrorDeletes, 2);

            GroupBox nasGroup = new GroupBox();
            nasGroup.Text = "Ubuntu 到 NAS";
            nasGroup.Dock = DockStyle.Fill;
            nasGroup.Padding = new Padding(10);
            root.Controls.Add(nasGroup, 0, 2);

            TableLayoutPanel nasTable = CreateFieldTable();
            nasGroup.Controls.Add(nasTable);
            _nasHost = AddTextField(nasTable, 0, "NAS 主机");
            _nasPort = new NumericUpDown();
            _nasPort.Minimum = 1;
            _nasPort.Maximum = 65535;
            _nasPort.Dock = DockStyle.Fill;
            nasTable.Controls.Add(new Label {
                Text = "SSH/SFTP 端口",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            }, 0, 1);
            nasTable.Controls.Add(_nasPort, 1, 1);
            _nasUser = AddTextField(nasTable, 2, "NAS 用户");
            _nasPassword = AddTextField(nasTable, 3, "NAS 密码");
            _nasPassword.UseSystemPasswordChar = true;
            _nasRemote = AddTextField(nasTable, 4, "账号内目标目录");

            Label keyNote = new Label();
            keyNote.Text = "NAS 密码留空时，Ubuntu 上传代理会使用已配置的 SSH 密钥。";
            keyNote.AutoSize = true;
            keyNote.ForeColor = Color.DimGray;
            keyNote.Margin = new Padding(3, 4, 3, 2);
            nasTable.Controls.Add(keyNote, 1, 5);
            nasTable.SetColumnSpan(keyNote, 2);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.AutoSize = true;
            actions.Padding = new Padding(0, 10, 0, 0);
            root.Controls.Add(actions, 0, 3);

            Button install = new Button();
            install.Text = "安装并开始";
            install.AutoSize = true;
            install.Padding = new Padding(12, 4, 12, 4);
            install.Click += new EventHandler(InstallClick);
            actions.Controls.Add(install);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.AutoSize = true;
            cancel.Padding = new Padding(12, 4, 12, 4);
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(cancel);

            _status = new Label();
            _status.AutoSize = true;
            _status.ForeColor = Color.Firebrick;
            _status.Margin = new Padding(0, 8, 12, 0);
            actions.Controls.Add(_status);

            AcceptButton = install;
            CancelButton = cancel;
        }

        private static TableLayoutPanel CreateFieldTable()
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.ColumnCount = 3;
            table.RowCount = 8;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int row;
            for (row = 0; row < 8; row++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            return table;
        }

        private static TextBox AddTextField(TableLayoutPanel table, int row, string label)
        {
            Label name = new Label();
            name.Text = label;
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.Dock = DockStyle.Fill;
            name.Margin = new Padding(3, 6, 3, 4);
            table.Controls.Add(name, 0, row);

            TextBox value = new TextBox();
            value.Dock = DockStyle.Fill;
            value.Margin = new Padding(3, 3, 3, 4);
            table.Controls.Add(value, 1, row);
            table.SetColumnSpan(value, 2);
            return value;
        }

        private TextBox AddBrowseField(TableLayoutPanel table, int row, string label)
        {
            Label name = new Label();
            name.Text = label;
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.Dock = DockStyle.Fill;
            name.Margin = new Padding(3, 6, 3, 4);
            table.Controls.Add(name, 0, row);

            TextBox value = new TextBox();
            value.Dock = DockStyle.Fill;
            value.Margin = new Padding(3, 3, 3, 4);
            table.Controls.Add(value, 1, row);

            Button browse = new Button();
            browse.Text = "浏览...";
            browse.AutoSize = true;
            browse.Margin = new Padding(3, 2, 3, 3);
            browse.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.SelectedPath = value.Text;
                    dialog.Description = "选择 Win7 本地需要同步的文件夹";
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        value.Text = dialog.SelectedPath;
                    }
                }
            };
            table.Controls.Add(browse, 2, row);
            return value;
        }

        private void LoadValues(AppConfig config)
        {
            if (config == null) config = new AppConfig();
            _source.Text = config.SourceFolder;
            _profile.Text = config.ProfileName;
            _shareUnc.Text = config.ShareUnc;
            _drive.Text = config.DriveLetter;
            _shareUser.Text = config.ShareUsername;
            _sharePassword.Text = config.SharePassword;
            _nasHost.Text = config.NasHost;
            _nasPort.Value = config.NasPort;
            _nasUser.Text = config.NasUsername;
            _nasPassword.Text = config.NasPassword;
            _nasRemote.Text = config.NasRemoteRoot;
            _mirrorDeletes.Checked = config.MirrorDeletes;
        }

        private void InstallClick(object sender, EventArgs e)
        {
            _status.Text = "";
            AppConfig config = BuildConfig();
            string error = ValidateConfig(config);
            if (!UbuntuWinShareClient.Text.IsBlank(error))
            {
                _status.Text = error;
                return;
            }

            UseWaitCursor = true;
            try
            {
                ShareMapResult mapped = NetworkShare.EnsureMapped(config);
                if (!mapped.Success)
                {
                    _status.Text = mapped.Message;
                    return;
                }

                string keyPath = UploadQueue.PublicKeyPath(config);
                if (!File.Exists(keyPath))
                {
                    _status.Text = "Ubuntu 上传代理尚未就绪，缺少公钥文件。";
                    return;
                }

                string fingerprint = UploadQueue.PublicKeyFingerprint(config);
                if (_existing != null &&
                    !UbuntuWinShareClient.Text.IsBlank(
                        _existing.UploadPublicKeyFingerprint) &&
                    !string.Equals(
                        _existing.UploadPublicKeyFingerprint,
                        fingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    DialogResult trust = MessageBox.Show(
                        "Ubuntu 上传代理公钥发生变化。只有确认 Ubuntu 端重新安装过代理时，才应信任新公钥。\r\n\r\n是否信任当前公钥？",
                        "确认公钥变化",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (trust != DialogResult.Yes)
                    {
                        _status.Text = "已取消保存，原公钥指纹未变更。";
                        return;
                    }
                }

                config.UploadPublicKeyFingerprint = fingerprint;
                ConfigStore.Save(config);
                SelfInstaller.RegisterApplication();
                SavedConfig = config;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Log.Write("setup-save", ex);
                _status.Text = ErrorText.Safe(ex);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private AppConfig BuildConfig()
        {
            AppConfig config = _existing == null
                ? new AppConfig()
                : _existing.Clone();
            config.ProfileName = _profile.Text.Trim();
            config.SourceFolder = _source.Text.Trim();
            config.ShareUnc = _shareUnc.Text.Trim().TrimEnd('\\');
            config.DriveLetter = UbuntuWinShareClient.Text.NormalizeDrive(_drive.Text);
            config.ShareUsername = _shareUser.Text.Trim();
            config.SharePassword = _sharePassword.Text;
            config.NasHost = _nasHost.Text.Trim();
            config.NasPort = Decimal.ToInt32(_nasPort.Value);
            config.NasUsername = _nasUser.Text.Trim();
            config.NasPassword = _nasPassword.Text;
            config.NasRemoteRoot = _nasRemote.Text.Trim().Replace('\\', '/').Trim('/');
            config.MirrorDeletes = _mirrorDeletes.Checked;
            return config;
        }

        private static string ValidateConfig(AppConfig config)
        {
            if (!Directory.Exists(config.SourceFolder))
                return "请选择有效的本地源文件夹。";
            if (!config.ShareUnc.StartsWith("\\\\"))
                return "Ubuntu 共享地址必须是 UNC 路径。";
            if (config.DriveLetter.Length != 2 ||
                config.DriveLetter[1] != ':' ||
                config.DriveLetter[0] < 'A' ||
                config.DriveLetter[0] > 'Z')
                return "映射盘符格式应为 I:。";
            if (config.SourceFolder.StartsWith(
                    config.ShareUnc,
                    StringComparison.OrdinalIgnoreCase))
                return "本地源文件夹不能位于 Ubuntu 共享目录内。";
            if (config.SourceFolder.StartsWith(
                    config.DriveLetter + "\\",
                    StringComparison.OrdinalIgnoreCase))
                return "本地源文件夹不能位于目标映射盘内。";
            if (UbuntuWinShareClient.Text.IsBlank(config.ProfileName))
                return "请输入同步名称。";
            if (UbuntuWinShareClient.Text.IsBlank(config.NasHost))
                return "请输入 NAS 主机。";
            if (UbuntuWinShareClient.Text.IsBlank(config.NasUsername))
                return "请输入 NAS 用户。";
            if (UbuntuWinShareClient.Text.IsBlank(config.NasRemoteRoot))
                return "请输入 NAS 账号内目标目录。";
            if (config.NasRemoteRoot.StartsWith("/") ||
                config.NasRemoteRoot.IndexOf("..") >= 0)
                return "NAS 目标目录必须是账号 HOME 内的相对路径。";
            return "";
        }
    }
}
