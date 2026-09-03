using T9Pane.Services;

namespace T9Pane.Tests;

public class SetupArchPolicyTests
{
    [Fact]
    public void Amd64_os_installs_both_imes_and_64bit_pane()
    {
        var plan = SetupArchPolicy.For(NativeOsArch.X64, setupIs32Bit: true);

        Assert.True(SetupArchPolicy.CanInstall(NativeOsArch.X64));
        Assert.False(plan.InstallArm64Ime);
        Assert.True(plan.InstallX64Ime);
        Assert.True(plan.InstallX86Ime);
        Assert.Equal("win-x64", plan.PaneHostRid);
        Assert.Equal("win-x64", plan.DotNetRuntimeRid);
        Assert.True(plan.UninstallKeyWow64);
        Assert.True(plan.UseSysnativeForNative64Regsvr);
        Assert.Equal(["x64", "x86"], SetupArchPolicy.ImeArches(plan));
        Assert.Equal(@"sysnative\regsvr32.exe", SetupArchPolicy.Regsvr32RelativePath(plan, "x64"));
        Assert.Equal(@"SysWOW64\regsvr32.exe", SetupArchPolicy.Regsvr32RelativePath(plan, "x86"));
    }

    [Fact]
    public void X64_pane_process_uses_system32_for_64bit_regsvr()
    {
        var plan = SetupArchPolicy.For(NativeOsArch.X64, setupIs32Bit: false);
        Assert.Equal(@"System32\regsvr32.exe", SetupArchPolicy.Regsvr32RelativePath(plan, "x64"));
    }

    [Fact]
    public void X86_os_installs_only_32bit_ime_and_pane()
    {
        var plan = SetupArchPolicy.For(NativeOsArch.X86, setupIs32Bit: true);

        Assert.True(SetupArchPolicy.CanInstall(NativeOsArch.X86));
        Assert.False(plan.InstallX64Ime);
        Assert.True(plan.InstallX86Ime);
        Assert.Equal("win-x86", plan.PaneHostRid);
        Assert.Equal("win-x86", plan.DotNetRuntimeRid);
        Assert.False(plan.UninstallKeyWow64);
        Assert.Equal(["x86"], SetupArchPolicy.ImeArches(plan));
        Assert.Equal(@"System32\regsvr32.exe", SetupArchPolicy.Regsvr32RelativePath(plan, "x86"));
        Assert.Equal(
            @"Software\Classes\CLSID\{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}\InprocServer32",
            SetupArchPolicy.X86InprocKey(plan, "{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}"));
    }

    [Fact]
    public void X64_os_points_32bit_ime_through_wow64_node()
    {
        var plan = SetupArchPolicy.For(NativeOsArch.X64, setupIs32Bit: true);
        Assert.Equal(
            @"Software\Classes\Wow6432Node\CLSID\{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}\InprocServer32",
            SetupArchPolicy.X86InprocKey(plan, "{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}"));
    }

    [Fact]
    public void Arm64_os_installs_native_ime_and_32bit_ime()
    {
        var plan = SetupArchPolicy.For(NativeOsArch.Arm64, setupIs32Bit: true);

        Assert.True(SetupArchPolicy.CanInstall(NativeOsArch.Arm64));
        Assert.Equal(NativeOsArch.Arm64, SetupArchPolicy.FromProcessorArchitecture(SetupArchPolicy.ProcessorArm64));
        Assert.True(plan.InstallArm64Ime);
        Assert.False(plan.InstallX64Ime);
        Assert.True(plan.InstallX86Ime);
        Assert.Equal("win-arm64", plan.PaneHostRid);
        Assert.Equal("win-arm64", plan.DotNetRuntimeRid);
        Assert.True(plan.UninstallKeyWow64);
        Assert.Equal(["arm64", "x86"], SetupArchPolicy.ImeArches(plan));
        Assert.Equal(@"sysnative\regsvr32.exe", SetupArchPolicy.Regsvr32RelativePath(plan, "arm64"));
        Assert.Equal(@"SysWOW64\regsvr32.exe", SetupArchPolicy.Regsvr32RelativePath(plan, "x86"));
        Assert.Equal(
            @"Software\Classes\Wow6432Node\CLSID\{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}\InprocServer32",
            SetupArchPolicy.X86InprocKey(plan, "{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}"));
        Assert.Equal(
            @"Software\Classes\CLSID\{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}\InprocServer32",
            SetupArchPolicy.Native64InprocKey("{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}"));
    }

    [Fact]
    public void Unknown_architectures_are_rejected()
    {
        Assert.Equal(NativeOsArch.X64, SetupArchPolicy.FromProcessorArchitecture(SetupArchPolicy.ProcessorAmd64));
        Assert.Equal(NativeOsArch.X86, SetupArchPolicy.FromProcessorArchitecture(SetupArchPolicy.ProcessorIntel));
        Assert.Equal(NativeOsArch.Unsupported, SetupArchPolicy.FromProcessorArchitecture(5));
        Assert.False(SetupArchPolicy.CanInstall(NativeOsArch.Unsupported));
        Assert.Throws<ArgumentOutOfRangeException>(() => SetupArchPolicy.For(NativeOsArch.Unsupported, true));
    }

    [Fact]
    public void DotNet_runtime_url_matches_pane_rid()
    {
        Assert.Equal(
            "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe",
            SetupArchPolicy.DotNetRuntimeUrl("win-x86"));
        Assert.Equal(
            "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe",
            SetupArchPolicy.DotNetRuntimeUrl("win-x64"));
        Assert.Equal(
            "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-arm64.exe",
            SetupArchPolicy.DotNetRuntimeUrl("win-arm64"));
    }
}
