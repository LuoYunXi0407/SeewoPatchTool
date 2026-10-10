using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SeewoPatchTool
{
    public class MainForm : Form
    {
        private static readonly string LogFile =
            Path.Combine(Path.GetTempPath(), "SeewoPatchTool.log");

        private TextBox _txtDir;
        private Label _lblState;
        private Button _btnBrowse;
        private Button _btnPatch;
        private Button _btnRestore;
        private TextBox _txtLog;

        private string _installDir;
        private bool _busy;

        public MainForm()
        {
            BuildUi();
            Load += delegate { RefreshAll(); };
        }

        private void BuildUi()
        {
            Font = new Font("Microsoft YaHei UI", 9F);
            Text = "希沃课堂助手 · 翻页按钮位置修补工具 v1.0";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 414);

            Label lblDir = new Label { Text = "安装目录：", Location = new Point(14, 14), AutoSize = true };
            _txtDir = new TextBox
            {
                Location = new Point(14, 36),
                Width = 440,
                ReadOnly = true,
                BackColor = Color.White
            };
            _btnBrowse = new Button { Text = "浏览...", Location = new Point(462, 34), Size = new Size(84, 25) };
            _btnBrowse.Click += delegate { Browse(); };

            _lblState = new Label
            {
                Text = "当前状态：检测中...",
                Location = new Point(14, 70),
                AutoSize = true,
                ForeColor = Color.DarkSlateGray
            };

            _btnPatch = new Button
            {
                Text = "修 补 —— 把翻页按钮移到左下角 / 右下角",
                Location = new Point(14, 100),
                Size = new Size(532, 52),
                BackColor = Color.FromArgb(232, 245, 233)
            };
            _btnPatch.Click += delegate { PatchClick(); };

            _btnRestore = new Button
            {
                Text = "还 原 —— 恢复官方原版",
                Location = new Point(14, 160),
                Size = new Size(532, 52),
                BackColor = Color.FromArgb(255, 243, 224)
            };
            _btnRestore.Click += delegate { RestoreClick(); };

            Label lblNote = new Label
            {
                Text = "说明：修补/还原会自动关闭并重启希沃课堂助手；修补时自动在安装目录生成 .bak 备份；\r\n" +
                       "软件升级后需重新修补。命令行：SeewoPageButtonsPatch.exe /status | /patch | /restore",
                Location = new Point(14, 222),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            _txtLog = new TextBox
            {
                Location = new Point(14, 262),
                Size = new Size(532, 138),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White
            };

            Controls.Add(lblDir);
            Controls.Add(_txtDir);
            Controls.Add(_btnBrowse);
            Controls.Add(_lblState);
            Controls.Add(_btnPatch);
            Controls.Add(_btnRestore);
            Controls.Add(lblNote);
            Controls.Add(_txtLog);
        }

        // ---------------------------------------------------------------- 日志

        private void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            if (InvokeRequired) BeginInvoke((Action)delegate { AppendLog(line); });
            else AppendLog(line);
            try { File.AppendAllText(LogFile, line + Environment.NewLine); } catch { }
        }

        private void AppendLog(string line)
        {
            _txtLog.AppendText(line + Environment.NewLine);
        }

        // ---------------------------------------------------------------- 状态刷新

        private void RefreshAll()
        {
            _installDir = PatchEngine.FindInstallDir();
            _txtDir.Text = _installDir ?? "(未自动检测到，请点“浏览...”选择包含 Sia.PptAssistant.dll 的目录)";
            RefreshState();
        }

        private void RefreshState()
        {
            PatchState st = PatchState.FileMissing;
            bool hasBackup = false;

            if (_installDir != null && File.Exists(Path.Combine(_installDir, "Sia.PptAssistant.dll")))
            {
                PatchEngine eng = new PatchEngine(_installDir, null);
                st = eng.GetState();
                hasBackup = File.Exists(eng.DllBackupPath);
            }

            string text;
            switch (st)
            {
                case PatchState.Original:
                    text = "当前状态：官方原版（未修补）";
                    break;
                case PatchState.Patched:
                    text = "当前状态：已修补（翻页按钮在左下角/右下角）";
                    break;
                case PatchState.Unknown:
                    text = "当前状态：无法识别（可能软件版本已更新，请勿强行修补）";
                    break;
                default:
                    text = "当前状态：未找到 Sia.PptAssistant.dll";
                    break;
            }
            if (hasBackup) text += "　[存在备份]";
            _lblState.Text = text;

            _btnPatch.Enabled = !_busy && st == PatchState.Original;
            _btnRestore.Enabled = !_busy && (hasBackup || st == PatchState.Patched);
        }

        // ---------------------------------------------------------------- 操作入口

        private void PatchClick()
        {
            if (_busy) return;
            DialogResult r = MessageBox.Show(
                "将自动关闭希沃课堂助手，修补完成后自动重新启动。\r\n\r\n" +
                "修补内容：放映批注时，屏幕左右两侧的翻页按钮改为出现在左下角/右下角。\r\n\r\n继续吗？",
                "确认修补", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            RunOp(true);
        }

        private void RestoreClick()
        {
            if (_busy) return;
            DialogResult r = MessageBox.Show(
                "将自动关闭希沃课堂助手，还原完成后自动重新启动。\r\n\r\n继续吗？",
                "确认还原", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            RunOp(false);
        }

        private async void RunOp(bool patch)
        {
            if (_installDir == null)
            {
                MessageBox.Show("请先选择安装目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _busy = true;
            RefreshState();
            try
            {
                bool wasRunning = PatchEngine.IsAnyRunning();
                if (wasRunning)
                {
                    Log("检测到希沃课堂助手正在运行，先关闭...");
                    PatchEngine.KillAll(Log);
                }

                PatchEngine eng = new PatchEngine(_installDir, Log);
                bool ok = await Task.Run(() => patch ? eng.Patch() : eng.Restore());

                if (ok && wasRunning) PatchEngine.StartMain(_installDir, Log);
                Log(patch
                    ? (ok ? "√ 修补完成。" : "× 修补失败，详见上方日志。")
                    : (ok ? "√ 还原完成。" : "× 还原失败，详见上方日志。"));
            }
            catch (Exception ex)
            {
                Log("发生异常: " + ex.Message);
                MessageBox.Show("操作出错：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
                RefreshState();
            }
        }

        private void Browse()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择希沃课堂助手安装目录（包含 Sia.PptAssistant.dll 的 SeewoIwbAssistant_版本号 文件夹）";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _installDir = dlg.SelectedPath;
                    _txtDir.Text = _installDir;
                    RefreshState();
                }
            }
        }
    }
}
