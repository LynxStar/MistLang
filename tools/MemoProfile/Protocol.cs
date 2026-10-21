using System.Diagnostics;
using System.Runtime;

namespace MemoProfile;

internal static class Protocol
{
    public const int WarmupPassesPerVariant = 3;
    public const int DesignRepeats = 1;
    public const int PassesPerSlot = 2;
    public const int ColdProcessesPerVariant = 6;
    public static readonly TimeSpan WarmBudget = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ColdBudget = TimeSpan.FromMinutes(10);

    public static readonly string[] Variants = [ParserVariant.MistCore, VariantSourceBuilder.Production, VariantSourceBuilder.AutoOnly, VariantSourceBuilder.ExplicitOnly, VariantSourceBuilder.NoMemo];

    public static IEnumerable<(int Round, int[] Order)> Rounds()
    {
        var design = WilliamsDesign(Variants.Length);
        var round = 0;
        for(var repeat = 0; repeat < DesignRepeats; repeat++)
        {
            foreach(var row in design)
            {
                yield return (round++, row);
            }
        }
    }

    // A Williams design puts each variant in each position equally often and immediately after every
    // other variant equally often, so slot-order and carry-over effects cancel within one design.
    public static IReadOnlyList<int[]> WilliamsDesign(int n)
    {
        var first = Enumerable.Range(0, n).Select(j => j % 2 == 1 ? (j + 1) / 2 : (n - j / 2) % n).ToArray();
        var rows = Enumerable.Range(0, n).Select(r => first.Select(x => (x + r) % n).ToArray()).ToList();
        if(n % 2 == 1)
        {
            rows.AddRange(rows.Select(r => r.Reverse().ToArray()).ToList());
        }

        return rows;
    }

    public static PassSample MeasurePass(ParserVariant variant, IReadOnlyList<object> tokenStreams, long[] perInputTicks)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var pauseBefore = GC.GetTotalPauseDuration();
        var total = Stopwatch.StartNew();
        var failed = 0;
        for(var i = 0; i < tokenStreams.Count; i++)
        {
            var start = Stopwatch.GetTimestamp();
            if(variant.Parse(tokenStreams[i]) is null)
            {
                failed++;
            }

            perInputTicks[i] = Stopwatch.GetTimestamp() - start;
        }

        total.Stop();
        process.Refresh();
        return new PassSample(
            total.Elapsed.TotalMilliseconds,
            (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds,
            failed);
    }

    public static string GcDescription()
    {
        return $"server={GCSettings.IsServerGC} latency={GCSettings.LatencyMode} concurrent={AppContext.GetData("System.GC.Concurrent") ?? "default"}";
    }
}

internal sealed record PassSample(double WallMs, double ProcessCpuMs, long AllocatedBytes, int Gen0, int Gen1, int Gen2, double GcPauseMs, int NullRoots);