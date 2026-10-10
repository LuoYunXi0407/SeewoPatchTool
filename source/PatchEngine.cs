using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;

namespace SeewoPatchTool
{
    public enum PatchState
    {
        Patched,     // 已修补：翻页按钮在左下角/右下角
        Original,    // 官方原版：翻页按钮在左右居中
        Unknown,     // 无法识别：可能软件升级后界面布局变化
        FileMissing  // 目标 DLL 不存在
    }

    /// <summary>
    /// 希沃课堂助手“PPT 助手”翻页按钮位置补丁引擎。
    ///
    /// 原理：放映批注时，翻页按钮（LeftSlideManagerArea / RightSlideManagerArea）
    /// 位于 MainWindow 的 XAML 中，编译后以 BAML 形式内嵌在 Sia.PptAssistant.dll
    /// 的资源 ui/mainwindow.baml 里。本补丁对其做“等长字节替换”：
    ///   VerticalAlignment="Center"  ->  "Bottom"   （Center 与 Bottom 同为 6 字符）
    ///   Margin="6 0"                ->  "6 6"      （离屏幕底边留 6 DIP）
    /// 等长替换不移动任何字节，不需要重新打包资源，直接写回即可被 WPF 正常加载。
    /// </summary>
    public class PatchEngine
    {
        private const string DllName = "Sia.PptAssistant.dll";
        private const string ChkName = "ChecksumMd5.txt";
        private const int CenterOffsetInCtx = 12; // "Center" 在上下文模式中的偏移
        private const int MarginWindow = 60;      // 在对齐字段向前多大窗口内找 Margin
                                                  // （实测目标 Margin 距对齐字段 7~36 字节；
                                                  //   窗口收紧可避免误中同一 BAML 中更远的其它 "6 0" 边距）

        // mainwindow.baml 中两个 SlideManagerArea 的唯一属性编码上下文：
        // RenderTransformOrigin("0,0.5"/"1,0.5") ... "Center" ... "Left"/"Right"
        private static readonly byte[] CtxL = H("302c302e3526fe240cc8ff0643656e7465723dff240ad0ff044c656674");
        private static readonly byte[] CtxR = H("312c302e3526fe240cc8ff0643656e7465723dff240bd0ff055269676874");
        // 修补后的对应模式（用于状态识别与自检）
        private static readonly byte[] CtxLBottom = H("302c302e3526fe240cc8ff06426f74746f6d3dff240ad0ff044c656674");
        private static readonly byte[] CtxRBottom = H("312c302e3526fe240cc8ff06426f74746f6d3dff240bd0ff055269676874");
        // Margin 值 "6 0" 的编码：03(字符串长度) "6 0" q(下一记录字节)
        private static readonly byte[] MarginPattern = H("0336203071");
        private static readonly byte[] CenterBytes = H("43656e746572"); // "Center"
        private static readonly byte[] BottomBytes = H("426f74746f6d"); // "Bottom"

        // 操作前需要关闭的进程（Guard 必须最先关，防止它把主程序拉起来）
        private static readonly string[] ProcessNames =
        {
            "Sia.Guard", "SeewoIwbAssistant", "ENMirrorSIA", "Sia.BrowserSubprocess"
        };

        private readonly Action<string> _log;

        public PatchEngine(string installDir, Action<string> log)
        {
            InstallDir = installDir;
            _log = log ?? delegate { };
        }

        public string InstallDir { get; private set; }

        public string DllPath { get { return Path.Combine(InstallDir, DllName); } }
        public string ChkPath { get { return Path.Combine(InstallDir, ChkName); } }
        public string DllBackupPath { get { return DllPath + ".bak"; } }
        public string ChkBackupPath { get { return ChkPath + ".bak"; } }

        // ------------------------------------------------------------------ 状态

        public PatchState GetState()
        {
            if (!File.Exists(DllPath)) return PatchState.FileMissing;
            byte[] data = File.ReadAllBytes(DllPath);
            if (Find(data, CtxLBottom, 0) >= 0 && Find(data, CtxRBottom, 0) >= 0) return PatchState.Patched;
            if (Find(data, CtxL, 0) >= 0 && Find(data, CtxR, 0) >= 0) return PatchState.Original;
            return PatchState.Unknown;
        }

        // ------------------------------------------------------------------ 修补

        public bool Patch()
        {
            PatchState st = GetState();
            if (st == PatchState.Patched) { _log("已经是补丁状态，无需重复修补。"); return true; }
            if (st != PatchState.Original)
            {
                _log("无法识别目标文件（可能软件已升级、界面布局变化），为安全起见不做修改。");
                return false;
            }

            byte[] data = File.ReadAllBytes(DllPath);

            // 备份“本次修补前”的真实原版（还原时优先使用）
            File.Copy(DllPath, DllBackupPath, true);
            if (File.Exists(ChkPath)) File.Copy(ChkPath, ChkBackupPath, true);
            _log("已备份原版 -> " + DllBackupPath);

            foreach (byte[] ctx in new byte[][] { CtxL, CtxR })
            {
                int p = Find(data, ctx, 0);
                if (p < 0) { _log("错误：修补过程中定位失败，已中止（未写入任何数据）。"); return false; }

                // VerticalAlignment: Center -> Bottom（等长，6 字节）
                Array.Copy(BottomBytes, 0, data, p + CenterOffsetInCtx, BottomBytes.Length);

                // Margin "6 0" -> "6 6"（等长，离屏幕底边留 6 DIP）
                int winStart = Math.Max(0, p - MarginWindow);
                int m = Find(data, MarginPattern, winStart);
                if (m >= 0 && m < p) data[m + 3] = (byte)'6';
                else _log("警告：未找到边距字段，本次仅修改了对齐方式。");
            }

            if (Find(data, CtxLBottom, 0) < 0 || Find(data, CtxRBottom, 0) < 0)
            {
                _log("错误：自检失败，已中止（未写入任何数据）。");
                return false;
            }

            WriteFileWithRetry(DllPath, data);
            _log("DLL 修补完成。");
            UpdateChecksumFile();
            _log("修补成功。MD5=" + Md5File(DllPath));
            return true;
        }

        // ------------------------------------------------------------------ 还原

        public bool Restore()
        {
            PatchState st = GetState();
            bool hasBak = File.Exists(DllBackupPath);

            // 1) 目标文件被删空时，直接用备份补回（不存在降级风险）
            if (st == PatchState.FileMissing)
            {
                if (hasBak)
                {
                    CopyWithRetry(DllBackupPath, DllPath, true);
                    if (File.Exists(ChkBackupPath)) File.Copy(ChkBackupPath, ChkPath, true);
                    else UpdateChecksumFile();
                    _log("目标文件不存在，已用备份补回。MD5=" + Md5File(DllPath));
                    return true;
                }
                _log("目标文件不存在且没有备份，无法还原。");
                return false;
            }

            // 2) 只有当前文件“确为本工具修补产物”时，.bak 才一定与它配套，可安全回写
            if (st == PatchState.Patched)
            {
                if (hasBak)
                {
                    CopyWithRetry(DllBackupPath, DllPath, true);
                    if (File.Exists(ChkBackupPath)) File.Copy(ChkBackupPath, ChkPath, true);
                    else UpdateChecksumFile();
                    _log("已从备份还原 DLL 与校验清单。");
                    _log("还原成功。MD5=" + Md5File(DllPath));
                    return true;
                }

                // 无备份时，若文件大小与内嵌原版一致，则用内嵌原版覆盖
                byte[] embedded = ReadEmbeddedOriginal();
                if (embedded != null && new FileInfo(DllPath).Length == embedded.Length)
                {
                    WriteFileWithRetry(DllPath, embedded);
                    UpdateChecksumFile();
                    _log("已使用工具内置原版数据还原（内置原版对应 0.0.3.1309 版本）。");
                    return true;
                }
                if (embedded == null)
                {
                    _log("本编译版本未内嵌原版数据（编译时未提供 embedded 文件），且无 .bak 备份。");
                    _log("请通过官方重新安装/修复获得原版文件。");
                }
                else
                {
                    _log("当前文件大小与内置原版不一致（可能已升级），拒绝盲写。");
                    _log("请先通过官方升级/修复功能恢复该文件，再重新修补。");
                }
                return false;
            }

            // 3) 当前已是原版：无需还原。若备份还在且与当前文件不同（原地升级过），
            //    保留当前新版本，绝不用旧备份降级覆盖。顺带把校验清单与当前文件对齐。
            if (st == PatchState.Original)
            {
                if (hasBak && Md5File(DllPath) != Md5File(DllBackupPath))
                {
                    _log("当前文件已是官方原版，且与 .bak 备份内容不同（可能软件已原地升级）。");
                    _log("已保留当前新版本，未用旧备份覆盖。旧的 .bak 备份可手动删除。");
                }
                else
                {
                    _log("当前文件本来就是原版，无需还原。");
                }
                UpdateChecksumFile();
                return true;
            }

            // 4) 状态未知：可能是新版软件或文件被改动。自动还原有降级风险，拒绝。
            if (hasBak)
            {
                _log("无法识别当前文件（可能软件已更新、界面布局变化）。");
                _log("为避免旧备份覆盖新版本，已拒绝自动还原；如确有必要，可手动复制 .bak 覆盖后重试。");
            }
            else
            {
                _log("文件无法识别且没有本工具生成的备份，无法还原。");
            }
            return false;
        }

        // ------------------------------------------------------------------ ChecksumMd5.txt

        private void UpdateChecksumFile()
        {
            if (!File.Exists(ChkPath)) { _log("未找到 ChecksumMd5.txt，跳过校验清单更新。"); return; }
            try
            {
                XmlDocument xml = new XmlDocument();
                xml.Load(ChkPath);
                XmlNode hit = null;
                foreach (XmlNode info in xml.SelectNodes("//FileMd5Info"))
                {
                    XmlNode p = info.SelectSingleNode("RelativeFilePath");
                    if (p != null && p.InnerText == DllName) { hit = info; break; }
                }
                if (hit == null) { _log("校验清单中没有 " + DllName + " 条目，跳过。"); return; }

                SetNode(hit, "Md5", Md5File(DllPath));
                SetNode(hit, "FileSize", new FileInfo(DllPath).Length.ToString());
                xml.Save(ChkPath);
                _log("ChecksumMd5.txt 已同步更新。");
            }
            catch (Exception ex)
            {
                _log("更新校验清单失败（不影响补丁本身）: " + ex.Message);
            }
        }

        private static void SetNode(XmlNode parent, string name, string value)
        {
            XmlNode n = parent.SelectSingleNode(name);
            if (n == null)
            {
                n = parent.OwnerDocument.CreateElement(name);
                parent.AppendChild(n);
            }
            n.InnerText = value;
        }

        // ------------------------------------------------------------------ 进程管理

        public static bool IsAnyRunning()
        {
            foreach (string name in ProcessNames)
                if (Process.GetProcessesByName(name).Length > 0) return true;
            return false;
        }

        public static void KillAll(Action<string> log)
        {
            foreach (string name in ProcessNames)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        log("结束进程 " + p.ProcessName + " (PID " + p.Id + ")");
                        p.Kill();
                        if (!p.WaitForExit(5000)) p.Kill();
                    }
                    catch (Exception ex) { log("结束进程出错: " + ex.Message); }
                }
            }
            Thread.Sleep(500);
        }

        public static void StartMain(string installDir, Action<string> log)
        {
            string exe = Path.Combine(installDir, "SeewoIwbAssistant.exe");
            if (!File.Exists(exe)) { log("未找到主程序，跳过重启。"); return; }
            try
            {
                // 通过 explorer 启动，使程序以与用户正常登录启动相同的权限级别运行
                Process.Start("explorer.exe", "\"" + exe + "\"");
                log("已重新启动希沃课堂助手。");
            }
            catch (Exception ex) { log("重启失败: " + ex.Message); }
        }

        // ------------------------------------------------------------------ 工具方法

        public static string FindInstallDir()
        {
            string[] roots =
            {
                @"C:\Program Files (x86)\Seewo\SeewoIwbAssistant",
                @"C:\Program Files\Seewo\SeewoIwbAssistant"
            };
            string best = null;
            Version bestV = null;
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root, "SeewoIwbAssistant_*"))
                {
                    if (!File.Exists(Path.Combine(dir, DllName))) continue;
                    string tail = dir.Substring(dir.LastIndexOf('_') + 1);
                    Version v;
                    Version.TryParse(tail, out v);
                    if (best == null || (v != null && (bestV == null || v > bestV)))
                    {
                        best = dir;
                        bestV = v;
                    }
                }
            }
            return best;
        }

        public static string Md5File(string path)
        {
            using (MD5 md5 = MD5.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in md5.ComputeHash(fs)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private byte[] ReadEmbeddedOriginal()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream("SeewoPatchTool.OriginalDll"))
                {
                    if (s == null) return null;
                    byte[] buf = new byte[s.Length];
                    int off = 0;
                    while (off < buf.Length)
                    {
                        int n = s.Read(buf, off, buf.Length - off);
                        if (n <= 0) break;
                        off += n;
                    }
                    return buf;
                }
            }
            catch { return null; }
        }

        private static void WriteFileWithRetry(string path, byte[] data)
        {
            for (int i = 1; ; i++)
            {
                try { File.WriteAllBytes(path, data); return; }
                catch (IOException)
                {
                    if (i >= 8) throw;
                    Thread.Sleep(400);
                }
            }
        }

        private static void CopyWithRetry(string src, string dst, bool overwrite)
        {
            for (int i = 1; ; i++)
            {
                try { File.Copy(src, dst, overwrite); return; }
                catch (IOException)
                {
                    if (i >= 8) throw;
                    Thread.Sleep(400);
                }
            }
        }

        /// <summary>朴素字节模式查找（文件仅 600KB，无需高性能算法）</summary>
        private static int Find(byte[] hay, byte[] needle, int start)
        {
            int limit = hay.Length - needle.Length;
            for (int i = start; i <= limit; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private static byte[] H(string hex)
        {
            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
