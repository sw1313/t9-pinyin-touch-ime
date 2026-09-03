using System.IO.Pipes;
using T9Pane.Services;

namespace T9Pane.Tests;

public class ImeCommandChannelsTests
{
    [Fact]
    public void Same_process_tsf_instances_keep_independent_channels()
    {
        using var channels = new ImeCommandChannels();
        var first = new ImeCommandChannel(123, 11, NewPipe());
        var second = new ImeCommandChannel(123, 22, NewPipe());

        channels.Add(first);
        channels.Add(second);

        Assert.Equal(2, channels.Count);
        Assert.True(channels.TryGet(11, 123, out var pickedFirst));
        Assert.Same(first, pickedFirst);
        Assert.True(channels.TryGet(22, 123, out var pickedSecond));
        Assert.Same(second, pickedSecond);
        Assert.Equal(2, channels.Snapshot().Length);
    }

    private static NamedPipeServerStream NewPipe() =>
        new($"T9Pane.Tests.{Guid.NewGuid():N}", PipeDirection.InOut, 1);
}
