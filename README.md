# T9 拼音触屏输入法

独立的 Windows 触屏拼音输入法：九键、26 键、英文、全键、数字和符号盘。  
语言栏里是一层薄的 TSF DLL，词库和键盘界面跑在本进程。手指点进带文字模式的输入框才弹出，焦点离开文本就收起。语言栏是 T9 时，宿主进程里会取消系统输入面板的显示请求，官方触摸键盘不会先闪一下再被藏掉。不改「显示触摸键盘」注册表。

**许可：** [GNU GPL v3](LICENSE)  
**仓库名：** `t9-pinyin-touch-ime`  
**作者：** [sw1313](https://github.com/sw1313)

本项目**不是**[小白 T9](https://github.com/HuanSoft-Open-Source-Community/xiaobai-t9) 的分支，也不使用它的 Rime 引擎。随包词库来自多个开源字典，说明见 [THIRD_PARTY.md](THIRD_PARTY.md)。

## 终端用户：安装现成包

1. 打开 [Releases](https://github.com/sw1313/t9-pinyin-touch-ime/releases)，下载 `T9-Pinyin-Touch-IME-*-Setup.exe`。一份安装包支持 **x64、x86 和 ARM64** Windows（新 Surface / Snapdragon 机型走 ARM64）。
2. 双击安装程序，在 UAC 点「是」。没有 .NET 8 时会自动下载对应架构的桌面运行时。
3. 程序装到 `%ProgramFiles%\T9Pane`。x64 系统注册 x64 + x86 IME。ARM64 系统用 **Arm64X** 转发：原生应用走 ARM64 IME，x64 模拟应用走 x64 IME，另注册 x86 IME。32 位系统只注册 32 位 DLL。覆盖安装会先清掉旧输入法注册和旧 DLL，**保留** `%APPDATA%\T9Pane` 里的设置、用户词库和键盘背景。
4. 按 **Win+空格** 切到 **T9 九键**。
5. **用手指或鼠标点一下文本框**，键盘才会出现。只靠窗口自己聚焦、或程序 `SetFocus` 不会弹。
6. 覆盖安装后，已打开过 T9 的进程（至少 **SearchHost**、资源管理器）需要重启，新的 `T9Ime.dll` 才会进到搜索框里。

卸载：在「设置 → 应用」里卸载「T9 拼音触屏输入法」，或再次运行 Setup 时使用 `T9Setup.exe /uninstall`。

自己从源码打安装程序（得到单个 `Setup.exe`）：

```powershell
pwsh -File tools\Pack-Release.ps1
```

生成文件在 `dist\T9-Pinyin-Touch-IME-*-Setup.exe`。这是原生 Win32 安装程序，用户不用跑 PowerShell 或 bat。

## 当前版本 0.1.58

对照官方触摸键盘（WPF TipTsfHelper / `RequireTouchInEditControl` / `ITfContextView.GetTextExt` / CFS_EXCLUDE）：

- 弹出：手指点进带 UIA Text 的框。窗口自带焦点不够。
- 收起：焦点落定后不再是文本（Edit↔Button）。Chromium 短暂 `SetFocus(null)` 或探测失败不会把刚弹出来的盘收掉。
- 位置：只用插入点（GetTextExt / TextPattern），不用元素外框顶边；键盘不得盖住正在打的框。
- 搜索：开始菜单 / 任务栏搜索走 SearchHost 里的系统浮层位图键盘（HostRender）。组词只上拼音，联想走 `ITfFnSearchCandidateProvider`，不再把汉字和分隔符塞进普通输入框。

## 功能

- 九键拼音、26 键拼音、英文、全键、数字、符号
- 候选条与联想；英文 / 全键也可展开
- 托盘菜单：开机启动、透明度、按盘面尺寸换背景图、重载词库
- 必须点到文本区才弹出；切走输入框会收起
- 语言栏是 T9 时在宿主进程取消官方输入面板，切走 T9 立刻停拦
- 开始菜单搜索和任务栏搜索走 SearchHost 浮层键盘，按输入框而不是按窗口句柄区分

## 从源码编译

需要：Windows 10/11（打包机建议 x64）、.NET 8 SDK、PowerShell 7、带 C++ 的 Visual Studio / Build Tools。打 ARM64 包还要勾选 **MSVC C++ ARM64 生成工具**。

```powershell
dotnet publish src\T9Pane\T9Pane.csproj -c Release -r win-x64 --self-contained false
dotnet publish src\T9Pane\T9Pane.csproj -c Release -r win-x86 --self-contained false
dotnet publish src\T9Pane\T9Pane.csproj -c Release -r win-arm64 --self-contained false
pwsh -File src\T9Ime\build.ps1 -Arch x64
pwsh -File src\T9Ime\build.ps1 -Arch x86
pwsh -File src\T9Ime\build.ps1 -Arch arm64
pwsh -File src\T9Ime\build-arm64x.ps1
```

提权安装（来源必须是 **Release** 输出）：

```powershell
$src = (Resolve-Path "src\T9Pane\bin\Release\net8.0-windows").Path
Start-Process pwsh -Verb RunAs -Wait -ArgumentList @(
  '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass',
  '-File', "$src\Tools\Install-UiAccess.ps1",
  '-Source', $src,
  '-Manifest', "$src\app.uia.manifest",
  '-WaitForPid', '0'
)
Start-Process explorer.exe -ArgumentList '"C:\Program Files\T9Pane\T9Pane.exe"'
```

或打好 zip 后用 `Install.bat`。`uiAccess` 必须由 **explorer.exe** 拉起已安装的副本，不要直接从开发目录当输入法用。

注册到输入法选择器（安装脚本一般会做）：

```powershell
& "$env:ProgramFiles\T9Pane\T9Pane.exe" /register
```

注销：`T9Pane.exe /unregister`。

## 测试

```powershell
dotnet test src\T9Pane.Tests\T9Pane.Tests.csproj
```

已安装后，可再跑 UWP/搜索框场景：

```powershell
pwsh -File src\T9Pane\Tools\Test-UwpIme.ps1 -Scenario All
```

改弹出/定位逻辑时先补单元测试。这些回归用手点很容易误判。

## 使用

1. 托盘出现「T9 九键输入法」，或切到 T9 后 DLL 会拉起后端。
2. 点搜索框或普通输入框。
3. 用触摸九键输入。
4. 右键托盘可调透明度、换键盘背景图、打开用户词库和日志。

用户词库：`%APPDATA%\T9Pane\`，格式与 Rime 相同：`词语<TAB>拼音(空格分音节)<TAB>词频`。  
运行日志：`%APPDATA%\T9Pane\t9pane.log`。

## 仓库里有什么

| 路径 | 内容 |
| --- | --- |
| `src/T9Pane` | WPF 键盘、词库引擎、安装脚本 |
| `src/T9Ime` | 原生 TSF 输入法 DLL（x64 / x86 / ARM64 / Arm64X） |
| `src/T9Setup` | 原生 Win32 安装程序（双击安装 / 卸载） |
| `src/T9Pane.Tests` | 单元测试 |
| `src/T9Pane/Data/xiaobai-t9` | 随包开源词库（约 43 MB） |
| `tools/Pack-Release.ps1` | 打出 Win32 `Setup.exe` |

`bin/`、`obj/`、`dist/` 不进 git。二进制安装包只放在 GitHub Release。

## 许可

Copyright (C) 2026 sw1313

本程序是自由软件，你可以按 GNU GPL v3（或更新版本）再分发和修改。  
程序按「原样」提供，不附带任何担保。完整条款见 [LICENSE](LICENSE)。  
第三方词库的各自许可见 [THIRD_PARTY.md](THIRD_PARTY.md)。
