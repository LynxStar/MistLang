using System.Diagnostics;

namespace Mist.Driver.Generators;

internal sealed class GeneratorCache(string root, string toolchain, Action<string>? progress = null, Action? waiting = null) : IGeneratorCache
{
    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("MIST_CACHE_ROOT");
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mist", "Cache")
                : Path.GetFullPath(configured);
        }
    }

    public CachedGeneration? Use(string kind, string key, TimeSpan timeout, Func<string, ProvenanceRecord?> produce)
    {
        var destination = Path.Combine(root, "generators", toolchain, kind, key);
        if(Read(destination, key) is { } hit)
        {
            progress?.Invoke($"generator {kind} hit {key}");
            return hit;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var lease = Lock(destination + ".lock", timeout, waiting);
        if(Read(destination, key) is { } winner)
        {
            progress?.Invoke($"generator {kind} hit after wait {key}");
            return winner;
        }

        progress?.Invoke($"generator {kind} miss {key}");
        var staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
        _ = Directory.CreateDirectory(staging);
        try
        {
            if(produce(staging) is not { } record)
            {
                return null;
            }

            record = record with { Files = [.. GeneratorProvenance.Snapshot(staging)] };
            File.WriteAllText(Path.Combine(staging, GeneratorProvenance.FileName), GeneratorProvenance.Serialize(record));
            if(Directory.Exists(destination) && Read(destination, key) is null)
            {
                var invalid = destination + ".invalid-" + Guid.NewGuid().ToString("N");
                Directory.Move(destination, invalid);
                Directory.Delete(invalid, recursive: true);
            }

            return Publish(staging, destination, key);
        }
        finally
        {
            if(Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    internal static CachedGeneration? Read(string folder, string key)
    {
        try
        {
            var record = GeneratorProvenance.Deserialize(File.ReadAllText(Path.Combine(folder, GeneratorProvenance.FileName)));
            return record is not null && record.RunKey == key && GeneratorProvenance.Verify(folder, record)
                ? new CachedGeneration(folder, record) : null;
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static CachedGeneration? Publish(string staging, string destination, string key)
    {
        try
        {
            Directory.Move(staging, destination);
        }
        catch(IOException) when(Read(destination, key) is not null)
        {
        }

        return Read(destination, key);
    }

    private static FileStream Lock(string path, TimeSpan timeout, Action? waiting)
    {
        var clock = Stopwatch.StartNew();
        while(true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch(IOException) when(clock.Elapsed < timeout)
            {
                waiting?.Invoke();
                Thread.Sleep(25);
            }
            catch(IOException)
            {
                throw new TimeoutException();
            }
        }
    }
}