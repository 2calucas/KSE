namespace Sandbox;

/// <summary>Rolling frame-time statistics: current/average FPS plus 1% and 10% low FPS over a ~2s window.</summary>
internal sealed class FrameStats
{
    private const double WindowMs = 2000.0;

    private readonly Queue<double> _frameTimesMs = [];
    private double _sumMs;

    public double LastFrameMs { get; private set; }
    public double CurrentFps { get; private set; }
    public double AverageFps { get; private set; }
    public double Low1PercentFps { get; private set; }
    public double Low10PercentFps { get; private set; }

    public void AddFrame(double frameMs)
    {
        LastFrameMs = frameMs;
        CurrentFps = frameMs > 0.0 ? 1000.0 / frameMs : 0.0;

        _frameTimesMs.Enqueue(frameMs);
        _sumMs += frameMs;
        while (_sumMs > WindowMs && _frameTimesMs.Count > 8)
            _sumMs -= _frameTimesMs.Dequeue();

        if (_frameTimesMs.Count == 0)
            return;

        // Sorted slowest-first: the "worst N%" of frames are the ones with the largest frame times.
        double[] slowestFirst = [.. _frameTimesMs.OrderByDescending(t => t)];
        AverageFps = 1000.0 / slowestFirst.Average();
        Low1PercentFps = LowPercentFps(slowestFirst, 0.01);
        Low10PercentFps = LowPercentFps(slowestFirst, 0.10);
    }

    private static double LowPercentFps(double[] slowestFirst, double fraction)
    {
        int count = Math.Max(1, (int)(slowestFirst.Length * fraction));
        double averageMs = slowestFirst.Take(count).Average();
        return 1000.0 / averageMs;
    }
}
