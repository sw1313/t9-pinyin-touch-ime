using System.Diagnostics;

namespace T9Pane.Services;

/// <summary>
/// 瀑布流惯性：Android OverScroller 样条甩动 + iOS 橡皮筋回弹。
/// 甩得越快滚得越远，松手后先快后慢缓停，出界再弹回。
/// </summary>
internal static class FallInertia
{
    public const double MinFlingVelocity = 240;
    public const double MinCoastVelocity = 48;
    public const double CoastSeconds = 0.2;
    public const double MaxTick = 0.032;
    public const double MaxFlingVelocity = 8000;
    public const double Friction = 0.015;
    public const double Inflexion = 0.35;
    public const double RubberConstant = 0.55;
    public const double SpringOmega = 13.5;
    public const double SettlePixels = 0.6;
    public const double SettleVelocity = 10;
    public const double MaxSpringSeconds = 0.55;
    public const double SampleWindow = 0.1;
    public const double WheelImpulse = 1400;

    public static readonly double DecelerationRate = Math.Log(0.78) / Math.Log(0.9);

    public static readonly double PhysicalCoeff =
        9.80665 * 39.37 * 160.0 * 0.84;

    private static readonly long ClockStart = Stopwatch.GetTimestamp();

    public static double Now =>
        (Stopwatch.GetTimestamp() - ClockStart) / (double)Stopwatch.Frequency;

    public static double MaxOffset(double content, double viewport) =>
        Math.Max(0, content - viewport);

    public static double ClampVelocity(double velocity) =>
        Math.Clamp(velocity, -MaxFlingVelocity, MaxFlingVelocity);

    /// <summary>
    /// 抓帧卡住时不能按墙上时钟一次跳完，否则缓停看起来像直接终止。
    /// </summary>
    public static double Tick(double lastWall, double now)
    {
        if (lastWall <= 0)
        {
            return 0;
        }

        return Math.Min(MaxTick, Math.Max(0, now - lastWall));
    }

    /// <summary>
    /// 系统浮层只上报起点终点，高 DPI 下短滑速度会低于甩动门槛。
    /// 已经认出是滑动时，至少按最低甩动速度飞出去。
    /// </summary>
    public static double EnsureFling(double velocity, double delta)
    {
        var signed = velocity != 0 ? velocity : delta;
        if (Math.Abs(delta) >= 8 && Math.Abs(velocity) < MinFlingVelocity)
        {
            return Math.CopySign(MinFlingVelocity, signed);
        }

        return ClampVelocity(velocity);
    }

    public static double CoastDistance(double velocity)
    {
        var speed = Math.Abs(velocity);
        if (speed < MinCoastVelocity)
        {
            return 0;
        }

        return Math.CopySign(speed * CoastSeconds * 0.5, velocity);
    }

    public static double FlingDistance(double velocity)
    {
        var speed = Math.Abs(ClampVelocity(velocity));
        if (speed < MinFlingVelocity)
        {
            return 0;
        }

        var reference = Friction * PhysicalCoeff / Inflexion;
        var logVelocity = Math.Log(speed / reference);
        var distance = Friction * PhysicalCoeff
            * Math.Exp(logVelocity * DecelerationRate / (DecelerationRate - 1.0));
        return Math.CopySign(distance, velocity);
    }

    public static double FlingDuration(double velocity)
    {
        var speed = Math.Abs(ClampVelocity(velocity));
        if (speed < MinFlingVelocity)
        {
            return 0;
        }

        var reference = Friction * PhysicalCoeff / Inflexion;
        var androidDuration = Math.Pow(speed / reference, 1.0 / (DecelerationRate - 1.0));
        return DecelerationRate * Inflexion * androidDuration;
    }

    public static double FlingOffset(double start, double distance, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return start + distance * (1.0 - Math.Pow(1.0 - t, DecelerationRate));
    }

    public static double FlingVelocityAt(double velocity, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return velocity * Math.Pow(1.0 - t, DecelerationRate - 1.0);
    }

    public static double Rubber(double overscroll, double dimension)
    {
        if (overscroll <= 0 || dimension <= 0)
        {
            return 0;
        }

        return (1.0 - (1.0 / ((overscroll * RubberConstant / dimension) + 1.0))) * dimension;
    }

    public static (double Scroll, double Rubber) Project(
        double logical,
        double content,
        double viewport)
    {
        var max = MaxOffset(content, viewport);
        if (logical < 0)
        {
            return (0, Rubber(-logical, viewport));
        }

        if (logical > max)
        {
            return (max, -Rubber(logical - max, viewport));
        }

        return (logical, 0);
    }

    public static double Velocity(IReadOnlyList<(double Time, double Position)> samples)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        var last = samples[^1];
        var first = samples[0];
        for (var i = samples.Count - 2; i >= 0; i--)
        {
            if (last.Time - samples[i].Time <= SampleWindow)
            {
                first = samples[i];
            }
            else
            {
                break;
            }
        }

        var dt = last.Time - first.Time;
        return dt < 0.008 ? 0 : ClampVelocity((last.Position - first.Position) / dt);
    }

    public static double WheelVelocity(int wheelDelta) =>
        -wheelDelta / 120.0 * WheelImpulse;

    public static double Spring(
        double from,
        double target,
        double velocity,
        double elapsed)
    {
        var displacement = from - target;
        var omega = SpringOmega;
        var b = velocity + omega * displacement;
        return target + (displacement + b * elapsed) * Math.Exp(-omega * elapsed);
    }

    public static double SpringVelocity(double from, double target, double velocity, double elapsed)
    {
        var displacement = from - target;
        var omega = SpringOmega;
        var b = velocity + omega * displacement;
        var damp = Math.Exp(-omega * elapsed);
        return (b - omega * (displacement + b * elapsed)) * damp;
    }

    public static bool SpringSettled(double from, double target, double velocity, double elapsed) =>
        elapsed >= MaxSpringSeconds
        || (Math.Abs(Spring(from, target, velocity, elapsed) - target) < SettlePixels
            && Math.Abs(SpringVelocity(from, target, velocity, elapsed)) < SettleVelocity);

    public static double BlendVelocity(double leftover, double incoming)
    {
        if (leftover == 0 || Math.Sign(leftover) != Math.Sign(incoming))
        {
            return incoming;
        }

        return ClampVelocity(incoming + leftover * 0.5);
    }
}

internal enum FallRunKind
{
    Fling,
    Spring
}

internal sealed class FallRun
{
    public FallRunKind Kind { get; set; }
    public double Origin { get; init; }
    public double Distance { get; init; }
    public double Duration { get; init; }
    public double Velocity { get; init; }
    public double Content { get; init; }
    public double Viewport { get; init; }
    public double StartTime { get; set; }
    public double SpringFrom { get; set; }
    public double SpringTarget { get; set; }
    public double SpringVelocity { get; set; }
    public double SpringStart { get; set; }

    public static FallRun? Release(double offset, double velocity, double content, double viewport, double now)
    {
        var max = FallInertia.MaxOffset(content, viewport);
        if (offset < 0 || offset > max)
        {
            return new FallRun
            {
                Kind = FallRunKind.Spring,
                Origin = offset,
                Content = content,
                Viewport = viewport,
                StartTime = now,
                SpringFrom = offset,
                SpringTarget = FallFlow.Clamp(offset, content, viewport),
                SpringVelocity = velocity,
                SpringStart = now
            };
        }

        var speed = FallInertia.ClampVelocity(velocity);
        if (Math.Abs(speed) < FallInertia.MinCoastVelocity)
        {
            return null;
        }

        if (Math.Abs(speed) < FallInertia.MinFlingVelocity)
        {
            return new FallRun
            {
                Kind = FallRunKind.Fling,
                Origin = offset,
                Distance = FallInertia.CoastDistance(speed),
                Duration = FallInertia.CoastSeconds,
                Velocity = speed,
                Content = content,
                Viewport = viewport,
                StartTime = now
            };
        }

        return new FallRun
        {
            Kind = FallRunKind.Fling,
            Origin = offset,
            Distance = FallInertia.FlingDistance(speed),
            Duration = FallInertia.FlingDuration(speed),
            Velocity = speed,
            Content = content,
            Viewport = viewport,
            StartTime = now
        };
    }

    public double Evaluate(double now)
    {
        if (Kind == FallRunKind.Spring)
        {
            return FallInertia.Spring(SpringFrom, SpringTarget, SpringVelocity, now - SpringStart);
        }

        var duration = Math.Max(0.001, Duration);
        var t = Math.Clamp((now - StartTime) / duration, 0, 1);
        var x = FallInertia.FlingOffset(Origin, Distance, t);
        var max = FallInertia.MaxOffset(Content, Viewport);
        if (x >= 0 && x <= max)
        {
            return x;
        }

        Kind = FallRunKind.Spring;
        SpringFrom = x;
        SpringTarget = x < 0 ? 0 : max;
        SpringVelocity = FallInertia.FlingVelocityAt(Velocity, t);
        SpringStart = now;
        return x;
    }

    public bool IsDone(double now)
    {
        if (Kind == FallRunKind.Spring)
        {
            return FallInertia.SpringSettled(SpringFrom, SpringTarget, SpringVelocity, now - SpringStart);
        }

        return now - StartTime >= Duration;
    }

    public double VelocityAt(double now)
    {
        if (Kind == FallRunKind.Spring)
        {
            return FallInertia.SpringVelocity(SpringFrom, SpringTarget, SpringVelocity, now - SpringStart);
        }

        var duration = Math.Max(0.001, Duration);
        var t = Math.Clamp((now - StartTime) / duration, 0, 1);
        return FallInertia.FlingVelocityAt(Velocity, t);
    }
}

internal sealed class FallScroller
{
    private readonly List<(double Time, double Position)> _samples = [];
    private FallRun? _run;
    private double _clock;
    private double _lastWall;

    public bool IsRunning => _run is not null;

    public void Reset()
    {
        _samples.Clear();
        _run = null;
        _clock = 0;
        _lastWall = 0;
    }

    public void Note(double logical, double time)
    {
        _samples.Add((time, logical));
        while (_samples.Count > 12
            || (_samples.Count > 2 && time - _samples[0].Time > FallInertia.SampleWindow))
        {
            _samples.RemoveAt(0);
        }
    }

    public double DragVelocity() => FallInertia.Velocity(_samples);

    public double LeftoverVelocity(double now) => _run?.VelocityAt(_clock == 0 ? now : _clock) ?? 0;

    public void Begin(FallRun run)
    {
        _run = run;
        _clock = run.StartTime;
        _lastWall = 0;
    }

    public double? Step(double now)
    {
        if (_run is null)
        {
            return null;
        }

        _clock += FallInertia.Tick(_lastWall, now);
        _lastWall = now;
        var value = _run.Evaluate(_clock);
        if (_run.IsDone(_clock))
        {
            var settled = FallFlow.Clamp(value, _run.Content, _run.Viewport);
            _run = null;
            _clock = 0;
            _lastWall = 0;
            return settled;
        }

        return value;
    }

    public void Stop()
    {
        _run = null;
        _clock = 0;
        _lastWall = 0;
    }
}
