# 希沃课堂助手 · 翻页按钮位置修补工具

把希沃课堂助手（原 PPT 小工具）放映批注时**屏幕左右两侧居中**的翻页按钮，
移动到**左下角 / 右下角**。支持一键修补、一键还原。

## 文件说明

### 仓库文件说明

```
SeewoPatchTool\
├─ README.md                      ← 本文件
└─ source\                        ← 完整源代码
   ├─ Program.cs                  ← 入口：双击开 GUI；带 /patch 等参数走命令行模式
   ├─ MainForm.cs                 ← WinForms 界面（修补 / 还原两个按钮 + 日志）
   ├─ PatchEngine.cs              ← 补丁引擎（定位、字节替换、备份、还原、校验清单）
   ├─ app.manifest                ← 要求管理员权限（写 Program Files 必需）
   ├─ build.cmd                   ← 编译脚本（只用 Windows 自带的 csc.exe，无需装任何东西）
   └─ embedded\
      └─ Sia.PptAssistant.dll.orig ← 内嵌的官方原版 DLL（0.0.3.1309，还原兜底用）（仓库不含该希沃的二进制文件，需要的话前往 Release 下载）
```

### 发布文件说明
```
SeewoPatchTool\
├─ SeewoPageButtonsPatch.exe              ← 工具本体（双击使用，不内嵌原版数据，单文件即可用）
├─ SeewoPageButtonsPatchEmbedded.exe      ← 工具本体（双击使用，内嵌 0.0.3.1309 原版数据，单文件即可用）
├─ Sia.PptAssistant.dll.orig              ← 内嵌的官方原版 DLL（0.0.3.1309，还原兜底用）、
└─ SeewoIwbAssistant_0.0.3.1309.exe       ← 希沃课堂助手 0.0.3.1309 版本的安装包
```

## 使用方法

### 图形界面（推荐）

1. 双击 `SeewoPageButtonsPatch.exe`（会弹 UAC 提示，点是）。
2. 工具自动检测安装目录并显示当前状态：
   - **官方原版（未修补）** → 可以点「修 补」
   - **已修补** → 可以点「还 原」
   - **无法识别** → 说明软件版本更新导致界面布局变化，工具会拒绝修改，请勿强行处理
3. 点按钮后工具会自动：关闭希沃课堂助手（含守护进程）→ 处理文件 → 重新启动应用。

### 命令行（可选）

```
SeewoPageButtonsPatch.exe /status     查看状态
SeewoPageButtonsPatch.exe /patch      修补
SeewoPageButtonsPatch.exe /restore    还原
```
- 可附加 `/dir:"安装目录"` 指定路径（默认自动检测）。
- 退出码：0 成功，1 失败；详细日志在 `%TEMP%\SeewoPatchTool.log`。

## 还原机制（两层保障）

1. **备份优先**：每次「修补」前，工具会在安装目录生成 `Sia.PptAssistant.dll.bak`
   和 `ChecksumMd5.txt.bak`。还原时优先用它们——保证恢复的一定是**当前安装对应**的原版。
2. **内嵌兜底**：如果备份丢了，工具内置了一份官方原版 DLL（0.0.3.1309）。
   只有当目标文件大小与内置原版**完全一致**时才会写入，防止把旧版文件覆盖到升级后的新版上。

## 工作原理（为什么改 6 个字节就够）

PPT 助手的翻页按钮（`LeftSlideManagerArea` / `RightSlideManagerArea`）定义在
`MainWindow.xaml` 中，编译后以 BAML（二进制 XAML）形式嵌在 `Sia.PptAssistant.dll`
的资源 `ui/mainwindow.baml` 里。BAML 中属性名是 ID 编码，但**属性值是明文字符串**：

- `VerticalAlignment="Center"` → `"Bottom"`：两者恰好都是 6 个字符，等长原位替换；
- `Margin="6 0"` → `"6 6"`：同样是等长替换，让按钮离屏幕底边留 6 DIP 边距。

等长替换不移动任何字节、无需重打包资源，且该 DLL 未做强命名签名，改完即可正常加载。
定位时使用带上下文的唯一字节模式（含 `RenderTransformOrigin` 值和 `Left`/`Right` 对齐值），
保证不会误改其它出现 `Center` 的地方；每次写入前自检、失败即中止。

工具还会同步更新 `ChecksumMd5.txt` 中该文件的 MD5 条目，
避免希沃自带的 `Sia.UpdateFix` 更新校验报错。

## 重新编译

修改 `source\` 下任意源码后，双击 `source\build.cmd` 即可。
编译只用 Windows 自带的 .NET Framework 4.x C# 编译器（`csc.exe`），
**无需安装 Visual Studio 或 .NET SDK**，产物是免依赖的单文件 exe（内嵌原版 DLL）。

## 注意事项

- **软件升级后补丁会被官方新文件覆盖**，重新运行一次「修补」即可；
  若提示"无法识别"，说明新版界面布局变了，需要重新分析适配（本仓库的分析方法适用）。
- 操作前不需要手动退出希沃课堂助手，工具会自动处理并恢复运行。
- 如果机器上同时残留多个 `SeewoIwbAssistant_版本号` 目录，工具自动选择版本号最高的那个。

## 已知问题

- 点击希沃课堂助手的页码按钮显示全部幻灯片时动画由原位置展开。不打算处理。
- 与希沃 PPT 小工具不完全相同，希沃 PPT 小工具是横向排列，该补丁修补后依然为竖向排列。不打算处理。

## 声明

- 该项目由 GLM-5.3-Flash 生成
- 只对 0.0.3.1309 版本测试通过，未测试其他版本
- 使用后发生的异常情况与作者无关。如果修补后希沃课堂助手无法使用，建议卸载重装