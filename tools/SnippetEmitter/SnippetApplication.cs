using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SnippetEmitter;

internal static class SnippetApplication
{
    internal static async Task<int> Run(string[] args)
    {
        try
        {
            if(args is ["--worker", var request, var response])
            {
                var compiler = new SnippetCompiler();
                var paths = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(request))!;
                var workerResults = paths.Select(compiler.Compile).ToArray();
                await File.WriteAllTextAsync(response, JsonSerializer.Serialize(workerResults));
                return 0;
            }

            var options = SnippetOptions.Parse(args);
            if(options.Help)
            {
                Console.WriteLine("""
                    Usage: dotnet run --project tools/SnippetEmitter -- [options] [file.mist|directory ...]
                    Select one .mist/.cs pair, multiple files, or directories (recursive).
                    Without paths, select all pairs under book/snippets.
                      --list        Show the selection without compiling or writing.
                      --jobs N      Worker processes (default: half the CPUs, at most 8; 1 is in-process).
                      --timings     Show per-snippet and aggregate stage times; compilation is always validated.
                      --help        Show this help.
                    """);
                return 0;
            }

            var watch = Stopwatch.StartNew();
            var selected = SnippetSelection.Resolve(options.Paths);
            if(options.List)
            {
                foreach(var path in selected)
                {
                    Console.WriteLine(Path.GetRelativePath(Environment.CurrentDirectory, path));
                }

                Console.WriteLine($"Selected {selected.Length} snippet pairs.");
                return 0;
            }

            var jobs = Math.Min(options.Jobs, selected.Length);
            Console.WriteLine($"Found {selected.Length} .mist/.cs snippet pairs; using {jobs} worker(s).");
            SnippetResult[] results;
            if(jobs == 1)
            {
                var compiler = new SnippetCompiler();
                results = selected.Select(compiler.Compile).ToArray();
            }
            else
            {
                results = await SnippetWorkers.Compile(selected, jobs);
            }

            var updated = 0;
            var unchanged = 0;
            var failed = 0;
            foreach(var result in results.OrderBy(result => result.Path, SnippetSelection.PathComparer))
            {
                var path = Path.GetRelativePath(Environment.CurrentDirectory, result.Path);
                if(options.Timings)
                {
                    var stages = string.Join(", ", result.Stages.OrderBy(stage => stage.Key, StringComparer.Ordinal)
                        .Select(stage => $"{stage.Key}={stage.Value.ToString("F1", CultureInfo.InvariantCulture)} ms"));
                    Console.WriteLine(FormattableString.Invariant($"  timing: {path}: {result.Milliseconds:F1} ms, {result.Attempts} compile(s); {stages}"));
                }

                if(result.Emitted is null)
                {
                    await Console.Error.WriteAsync(result.Errors);
                    failed++;
                    continue;
                }

                try
                {
                    if(!SnippetOutput.Write(result))
                    {
                        unchanged++;
                    }
                    else
                    {
                        Console.WriteLine($"  updated: {path}");
                        updated++;
                    }
                }
                catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
                {
                    await Console.Error.WriteLineAsync($"  FAILED (output): {path}: {exception.Message}");
                    failed++;
                }
            }

            Console.WriteLine($"\nDone: {updated} updated, {unchanged} unchanged, {failed} failed");
            Console.WriteLine(FormattableString.Invariant($"Elapsed: {watch.Elapsed.TotalSeconds:F3} s (generation only; excludes building the tool)."));
            if(options.Timings)
            {
                var sums = results.SelectMany(result => result.Stages).GroupBy(stage => stage.Key)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => $"{group.Key}={group.Sum(stage => stage.Value).ToString("F1", CultureInfo.InvariantCulture)} ms");
                Console.WriteLine($"Stage totals (summed worker time): {string.Join(", ", sums)}; {results.Sum(result => result.Attempts)} compile(s).");
            }

            return failed > 0 ? 1 : 0;
        }
        catch(OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Generation cancelled.");
            return 130;
        }
        catch(Exception exception) when(exception is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            await Console.Error.WriteLineAsync($"error: {exception.Message}");
            return 1;
        }
    }
}