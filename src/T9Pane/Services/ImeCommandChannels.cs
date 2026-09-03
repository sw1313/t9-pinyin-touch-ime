using System.IO.Pipes;

namespace T9Pane.Services;

internal sealed class ImeCommandChannel(uint pid, ulong hwnd, NamedPipeServerStream pipe)
{
    public uint Pid { get; } = pid;
    public ulong Hwnd { get; } = hwnd;
    public NamedPipeServerStream Pipe { get; } = pipe;
}

internal sealed class ImeCommandChannels : IDisposable
{
    private readonly Dictionary<ulong, ImeCommandChannel> _channels = [];

    public int Count => _channels.Count;

    public void Add(ImeCommandChannel channel)
    {
        if (_channels.Remove(channel.Hwnd, out var old))
        {
            old.Pipe.Dispose();
        }

        _channels[channel.Hwnd] = channel;
    }

    public bool TryGet(ulong hwnd, uint pid, out ImeCommandChannel? channel)
    {
        if (hwnd != 0)
        {
            return _channels.TryGetValue(hwnd, out channel);
        }

        channel = _channels.Values.FirstOrDefault(candidate => candidate.Pid == pid);
        return channel is not null;
    }

    public bool Remove(ulong hwnd, out ImeCommandChannel channel) =>
        _channels.Remove(hwnd, out channel!);

    public bool RemoveIfSame(ulong hwnd, NamedPipeServerStream pipe, out ImeCommandChannel? channel)
    {
        if (_channels.TryGetValue(hwnd, out channel) && ReferenceEquals(channel.Pipe, pipe))
        {
            return _channels.Remove(hwnd);
        }

        channel = null;
        return false;
    }

    public ImeCommandChannel[] Snapshot() => [.. _channels.Values];

    public void Dispose()
    {
        foreach (var channel in _channels.Values)
        {
            channel.Pipe.Dispose();
        }

        _channels.Clear();
    }
}
