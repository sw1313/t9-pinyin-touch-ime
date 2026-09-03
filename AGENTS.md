# T9 九键 — 给 AI 助手的操作须知

## 构建与部署（照抄，别凭记忆）

安装脚本**有两个必填参数**，缺了会挂在 `Supply values for the following parameters: Source:`
交互提示上一直等输入。而且它 `#requires -RunAsAdministrator`，非提权会直接拒跑。

部署的来源目录是 **Release** 输出，不是 Debug。`dotnet build` 不带 `-c Release`
默认出到 Debug，装上去还是旧件——改完没生效先查这一条。

完整一轮：

```powershell
# 1. 托管侧。必须 Release，且必须走 dotnet build（要生成 T9Pane.exe apphost）
dotnet build src\T9Pane\T9Pane.csproj -c Release

# 2. 原生 DLL。只在改了 src\T9Ime 时才需要重跑
pwsh -File src\T9Ime\build.ps1 -Arch x64
pwsh -File src\T9Ime\build.ps1 -Arch x86

# 3. 提权安装
$src = "src\T9Pane\bin\Release\net8.0-windows"
Start-Process pwsh -Verb RunAs -Wait -ArgumentList @(
    '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass',
    '-File', "$src\Tools\Install-UiAccess.ps1",
    '-Source', $src,
    '-Manifest', "$src\app.uia.manifest",
    '-WaitForPid', (Get-Process T9Pane -ErrorAction SilentlyContinue).Id ?? 0
)

# 4. 重启外壳组件，必须在安装脚本跑完之后
Stop-Process -Name explorer,SearchHost,StartMenuExperienceHost -Force -ErrorAction SilentlyContinue
```

参数含义以 `src\T9Pane\Services\UiAccessInstall.cs` 的 `RequestElevatedInstall`
为准（那是产品自己调这个脚本的地方）。安装目标固定是 `%ProgramFiles%\T9Pane`。
安装日志在 `%APPDATA%\T9Pane\install-uiaccess.log`。

验证真的装上了：比对 `%ProgramFiles%\T9Pane\T9Pane.dll` 与 Release 输出的
`LastWriteTime`，以及 `Get-Process T9Pane` 的 `StartTime`。

## 日志

运行日志 `%APPDATA%\T9Pane\t9pane.log`。查弹出/定位问题看这两类行：

- `系统指针解析 origin=... hit=... authorized=...` —— 一次物理点击的判定结果
- `取输入框 表面=... 来源=... 光标=(...)` —— 定位来源与坐标；`(沿用)` 表示用了缓存坐标

`来源` 的可靠度从高到低：`caret` / `uia/text`（真实插入点）> `clicked`（用户点中的框）
> `uia/box`（元素外框）> `searchbox`（按窗口矩形编造，最不可信）。

## 两个容易反复踩的领域约束

**开始菜单搜索框和任务栏搜索框共用同一个 SearchHost 顶层窗口。** 它们是唯一一对
"窗口句柄相同、输入框不同"的文本框。任何"同一个表面就当同一个输入框"的逻辑
在这里都会误判，表现为切框后键盘停在上一个点击位置。判定同一个框必须连
UIA `RuntimeId` 一起比，见 `CaretQualityGate`。原生 TSF 光标也属于上一个框，
切到任务栏搜索时不能拿 y≈87 的开始菜单光标去摆键盘，见 `SearchCaretPolicy`。

**焦点在文本框里，不等于用户想输入。** 切换会话、切到前台会把焦点自动放进输入框。
用户明确要求必须手动点到文本区才弹。所以落点是肯定判据，焦点只用来确定
"落点属于哪个框"；只看焦点类型会让 Unigram 切群组时点群组列表也弹出键盘。
见 `InputInvocationProbe.HitTestFocusedInput`。

## 测试

```powershell
dotnet test src\T9Pane.Tests\T9Pane.Tests.csproj
```

改动策略/判定逻辑时，把场景固化成单元测试再改实现——这些 bug 都是回归型的，
手工点击验证受鼠标状态影响，容易误判。
