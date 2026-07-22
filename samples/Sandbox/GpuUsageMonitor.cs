using System.Diagnostics;

namespace Sandbox;

/// <summary>
/// Reads this process's GPU 3D-engine utilization via the Windows "GPU Engine" performance counter
/// category (the same data source Task Manager's per-process GPU column uses). Not a Vulkan concept,
/// so this stays outside Engine.RHI — it's an OS-level debug/perf reading, not a rendering API capability.
/// </summary>
internal sealed class GpuUsageMonitor : IDisposable
{
    private readonly List<PerformanceCounter> _counters = [];
    private readonly bool _available;

    public GpuUsageMonitor()
    {
        try
        {
            int pid = Environment.ProcessId;
            var category = new PerformanceCounterCategory("GPU Engine");
            string pidTag = $"pid_{pid}_";

            foreach (string instance in category.GetInstanceNames())
            {
                if (!instance.Contains(pidTag, StringComparison.Ordinal)) continue;
                if (!instance.Contains("engtype_3D", StringComparison.Ordinal)) continue;

                var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                counter.NextValue(); // first sample primes the rate calculation; discard it.
                _counters.Add(counter);
            }
            _available = _counters.Count > 0;
        }
        catch
        {
            _available = false;
        }
    }

    public float? GetUsagePercent()
    {
        if (!_available) return null;
        try
        {
            float total = 0f;
            foreach (var counter in _counters)
                total += counter.NextValue();
            return Math.Min(total, 100f);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var counter in _counters)
            counter.Dispose();
    }
}
