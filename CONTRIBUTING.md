# 参与开发

## 环境

- Windows 10/11（打包机建议 x64）
- .NET 8 SDK
- PowerShell 7（`pwsh`）
- Visual Studio 或 Build Tools，勾选 C++ x64/x86，以及 **MSVC ARM64**（打 Surface / ARM64 包需要）

## 日常改 C# 后怎么装

必须打 **Release**。`dotnet build` 不带 `-c Release` 会装上 Debug 旧件。

```powershell
dotnet build src\T9Pane\T9Pane.csproj -c Release
Stop-Process -Name T9Pane -Force -ErrorAction SilentlyContinue
$src = "src\T9Pane\bin\Release\net8.0-windows"
Start-Process pwsh -Verb RunAs -Wait -ArgumentList @(
  '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass',
  '-File', "$src\Tools\Install-UiAccess.ps1",
  '-Source', (Resolve-Path $src),
  '-Manifest', (Join-Path (Resolve-Path $src) 'app.uia.manifest'),
  '-WaitForPid', '0'
)
Start-Process explorer.exe -ArgumentList '"C:\Program Files\T9Pane\T9Pane.exe"'
```

不要为了换 DLL 去杀 explorer。`uiAccess` 进程必须由 explorer 拉起。

只在改了 `src\T9Ime` 时重编原生 DLL：

```powershell
pwsh -File src\T9Ime\build.ps1 -Arch x64
pwsh -File src\T9Ime\build.ps1 -Arch x86
```

## 测试

```powershell
dotnet test src\T9Pane.Tests\T9Pane.Tests.csproj
```

改策略或命中判定时，先把场景写成测试再改实现。

## 打给别人用的安装程序

```powershell
pwsh -File tools\Pack-Release.ps1
```

把 `dist\T9-Pinyin-Touch-IME-*-Setup.exe` 挂到 GitHub Release。这是 Win32 安装程序，不要把 `dist\` 提交进 git。

## 许可

补丁按 GPL v3 贡献。不要加入与 GPL v3 不兼容的代码或词库。新增第三方文件时更新 [THIRD_PARTY.md](THIRD_PARTY.md)。
