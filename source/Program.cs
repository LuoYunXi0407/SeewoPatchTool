using System;
using System.IO;
using System.Windows.Forms;

namespace SeewoPatchTool
{
    internal static class Program
    {
        // 命令行模式 / GUI 模式共用的日志文件，便于排查问题
        public static readonly string CliLogFile =
            Path.Combine(Path.GetTempPath(), "SeewoPatchTool.log");

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0) return RunCli(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        /// <summary>
        /// 解析目录参数，支持两种写法：
        ///   /dir "C:\path"   （两个参数）
        ///   /dir:"C:\path"   （单参数，PowerShell 中需写成 /dir:"C:\path" 或 '/dir:C:\path'）
        /// </summary>
        private static string ParseDir(string[] args)
        {
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "/dir" || a == "-dir")
                {
                    if (i + 1 < args.Length) return args[i + 1].Trim('"');
                }
                else if (a.StartsWith("/dir:", StringComparison.OrdinalIgnoreCase) ||
                         a.StartsWith("-dir:", StringComparison.OrdinalIgnoreCase))
                {
                    return a.Substring(5).Trim('"');
                }
            }
            return null;
        }

        /// <summary>在所有参数中找第一个模式标记（/status /patch /restore），与参数顺序无关</summary>
        private static string ParseMode(string[] args)
        {
            foreach (string a in args)
            {
                if (a.StartsWith("/dir", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("-dir", StringComparison.OrdinalIgnoreCase)) continue;
                string tok = a.TrimStart('/', '-').ToLowerInvariant();
                if (tok == "status" || tok == "patch" || tok == "restore") return tok;
            }
            return null;
        }

        /// <summary>
        /// 命令行模式：
        ///   SeewoPageButtonsPatch.exe /status            查看当前状态
        ///   SeewoPageButtonsPatch.exe /patch             修补（自动关/启应用）
        ///   SeewoPageButtonsPatch.exe /restore           还原（自动关/启应用）
        ///   可用 /dir:"安装目录" 或 /dir "安装目录" 指定安装路径，参数顺序不限。
        /// 退出码：0 成功，1 失败。详细输出写入 %TEMP%\SeewoPatchTool.log
        /// </summary>
        private static int RunCli(string[] args)
        {
            try { File.WriteAllText(CliLogFile, ""); } catch { }

            Action<string> log = delegate(string s)
            {
                string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
                try { File.AppendAllText(CliLogFile, line + Environment.NewLine); } catch { }
            };

            string mode = ParseMode(args);
            if (mode == null)
            {
                log("未指定操作，支持 /status /patch /restore，可用 /dir:\"安装路径\" 指定目录。");
                return 1;
            }
            string dir = ParseDir(args);
            if (dir == null) dir = PatchEngine.FindInstallDir();

            if (dir == null || !File.Exists(Path.Combine(dir, "Sia.PptAssistant.dll")))
            {
                log("未找到希沃课堂助手安装目录，可用 /dir:\"安装路径\" 指定。");
                return 1;
            }

            log("安装目录: " + dir);
            PatchEngine eng = new PatchEngine(dir, log);

            if (mode == "status")
            {
                log("状态: " + eng.GetState());
                return 0;
            }

            bool wasRunning = PatchEngine.IsAnyRunning();
            if (wasRunning)
            {
                log("关闭正在运行的希沃课堂助手...");
                PatchEngine.KillAll(log);
            }

            bool ok = mode == "patch" ? eng.Patch() : eng.Restore();
            if (ok && wasRunning) PatchEngine.StartMain(dir, log);
            log(ok ? "完成。" : "失败。");
            return ok ? 0 : 1;
        }
    }
}
