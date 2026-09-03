using System.IO.Pipes;
using T9Pane.Services;

namespace T9Pane.Tests;

public class PipeAclTests
{
    [Fact]
    public void Command_pipe_acl_allows_app_container()
    {
        var security = PipeAcl.ForHostAndAppContainer();
        Assert.True(PipeAcl.AllowsAppContainer(security));
        Assert.Equal("T9Pane.Ime", PipeAcl.NotificationPipe);
        Assert.Equal("T9Pane.Ime.Cmd", PipeAcl.CommandPipe);
        Assert.Equal(@"LOCAL\T9Pane.Ime", PipeAcl.AppContainerNotificationPipe);
        Assert.Equal(@"LOCAL\T9Pane.Ime.Cmd", PipeAcl.AppContainerCommandPipe);
    }

    [Fact]
    public void Host_frame_payload_exceeds_old_8kb_pipe_limit()
    {
        const int width = 620;
        const int height = 360;
        var bytes = 16 + width * height * 4;
        Assert.True(bytes > 8192, "旧命令管道 8KB 上限会丢掉 Band 帧，这条必须走新通道");
    }

    [Fact]
    public void Host_frame_limit_accepts_keyboard_at_200_percent_dpi()
    {
        const int width = 1680;
        const int height = 800;
        var bytes = 16 + width * height * 4;

        Assert.True(bytes < ImeHost.MaxPacketBytes);
        Assert.True(bytes > 2 * 1024 * 1024, "测试必须覆盖旧 2MB 限制造成的高 DPI 丢帧");
    }

    [Fact]
    public void Dotnet_accepts_appcontainer_local_pipe_namespace()
    {
        using var pipe = NamedPipeServerStreamAcl.Create(
            $@"LOCAL\T9Pane.Tests.{Guid.NewGuid():N}",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            256,
            256,
            PipeAcl.ForHostAndAppContainer());
        Assert.False(pipe.IsConnected);
    }
}
