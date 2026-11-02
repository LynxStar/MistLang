using System.Diagnostics;
using System.Text.Json;

namespace SnippetEmitter;

internal static class SnippetWorkers
{
    internal static async Task<SnippetResult[]> Compile(string[] paths, int jobs)
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"mist-snippet-workers-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(scratch);
        using var cancellation = new CancellationTokenSource();
        void cancel(object? _, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += cancel;
        try
        {
            var batches = Enumerable.Range(0, jobs)
                .Select(index => paths.Where((_, position) => position % jobs == index).ToArray());
            var results = await Task.WhenAll(batches.Select((batch, index) => Run(batch, index, scratch, cancellation.Token)));
            return [.. results.SelectMany(batch => batch)];
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task<SnippetResult[]> Run(string[] paths, int index, string scratch, CancellationToken cancellation)
    {
        var request = Path.Combine(scratch, $"{index}.json");
        var response = Path.Combine(scratch, $"{index}.result.json");
        await File.WriteAllTextAsync(request, JsonSerializer.Serialize(paths), cancellation);
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if(Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
        {
            start.ArgumentList.Add(typeof(SnippetWorkers).Assembly.Location);
        }

        start.ArgumentList.Add("--worker");
        start.ArgumentList.Add(request);
        start.ArgumentList.Add(response);
        using var process = new Process { StartInfo = start };
        var started = false;
        try
        {
            _ = process.Start();
            started = true;
            var stdout = process.StandardOutput.ReadToEndAsync(cancellation);
            var stderr = process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation);
            var output = await stdout;
            var errors = await stderr;
            if(process.ExitCode != 0 || !File.Exists(response))
            {
                throw new IOException($"Worker {index + 1} exited {process.ExitCode}: {errors}{output}");
            }

            var results = JsonSerializer.Deserialize<SnippetResult[]>(await File.ReadAllTextAsync(response, cancellation))
                ?? throw new JsonException("Worker returned no results.");
            if(!results.Select(result => result.Path).SequenceEqual(paths, SnippetSelection.PathComparer))
            {
                throw new IOException($"Worker {index + 1} returned an incomplete selection.");
            }

            Console.WriteLine($"  worker {index + 1}: {results.Length} snippets processed.");
            return results;
        }
        catch(Exception exception) when(exception is IOException or JsonException or System.ComponentModel.Win32Exception)
        {
            return [.. paths.Select(path => new SnippetResult(path, null,
                $"  FAILED (worker): {path}: {exception.Message}\n", 0, 0, []))];
        }
        finally
        {
            if(started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}