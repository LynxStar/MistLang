using System.Diagnostics;
using System.Text.RegularExpressions;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;

namespace SnippetEmitter;

internal sealed class SnippetCompiler
{
    private readonly MistLexer lexer = new();
    private readonly MistParser parser = new();
    private readonly SnippetTimings timings = new();
    private readonly StringWriter errors = new();
    private readonly CompilerDriver driver;

    internal SnippetCompiler()
    {
        driver = new CompilerDriver(lexer, parser, timings);
    }

    internal SnippetResult Compile(string path)
    {
        timings.Clear();
        _ = errors.GetStringBuilder().Clear();
        var watch = Stopwatch.StartNew();
        var emitted = EmitSnippet(path);
        return new SnippetResult(path, emitted, errors.ToString(), watch.Elapsed.TotalMilliseconds,
            timings.Attempts, new Dictionary<string, double>(timings.Stages));
    }

    private static void CopyAsLf(string from, string to)
    {
        File.WriteAllText(to, File.ReadAllText(from).ReplaceLineEndings("\n"));
    }

    internal static IReadOnlyList<string> Companions(string mistPath)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(mistPath);

        while(pending.Count > 0)
        {
            var current = pending.Pop();
            var directory = Path.GetDirectoryName(current)!;

            foreach(var line in File.ReadLines(current).TakeWhile(l => l.Length == 0 || l.StartsWith("//", StringComparison.Ordinal) || l.StartsWith("using ", StringComparison.Ordinal)))
            {
                var match = Regex.Match(line, @"^//\s*with:\s*(.+)$");
                if(!match.Success)
                {
                    continue;
                }

                foreach(var name in match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var companion = Path.GetFullPath(Path.Combine(directory, name));
                    if(companion != mistPath && !found.Contains(companion))
                    {
                        found.Add(companion);
                        pending.Push(companion);
                    }
                }
            }
        }

        return found;
    }

    private string? EmitSnippet(string mistPath)
    {
        var preparation = Stopwatch.StartNew();
        var relativeMist = Path.GetRelativePath(Environment.CurrentDirectory, mistPath);
        var scratchDir = Path.Combine(Path.GetTempPath(), "mist-snippet-emit-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);

        try
        {
            var fileName = Path.GetFileName(mistPath);

            // Copy the .mist file to a temp directory for compilation, as LF whatever the checkout: emitted
            // temporaries are named by source offset, so a CRLF working copy would rename them.
            var tempMistPath = Path.Combine(scratchDir, fileName);
            CopyAsLf(mistPath, tempMistPath);

            // A snippet that needs a package or a framework reference says so in a project file beside
            // it, named as the snippet is, which becomes the scratch project's mist.toml.
            var sidecarToml = Path.ChangeExtension(mistPath, ".toml");
            ProjectConfiguration? config = null;
            if(File.Exists(sidecarToml))
            {
                File.Copy(sidecarToml, Path.Combine(scratchDir, "mist.toml"));
                var tomlDiagnostics = new DiagnosticBag();
                config = ProjectFileParser.Parse(scratchDir, tomlDiagnostics);
                if(config is null)
                {
                    errors.WriteLine($"  FAILED (project file): {relativeMist}");
                    foreach(var d in tomlDiagnostics.ToImmutableArray().Take(3))
                    {
                        errors.WriteLine($"    {d.Descriptor.Number}: {d.Message}");
                    }

                    return null;
                }
            }

            // A chapter's snippets build on one another, so a snippet names the siblings it uses with a
            // `// with: a.mist, b.mist` line and they are compiled alongside it, each bringing its own
            // companions; the book shows only the snippet. A demonstration that needs a folder layout --
            // a namespace inferred from a folder -- keeps its other files in a folder beside it instead.
            var sources = new List<string> { tempMistPath };
            foreach(var companion in Companions(mistPath))
            {
                var copied = Path.Combine(scratchDir, Path.GetFileName(companion));
                CopyAsLf(companion, copied);
                sources.Add(copied);
            }

            var supportDir = Path.Combine(Path.GetDirectoryName(mistPath)!, Path.GetFileNameWithoutExtension(mistPath) + ".support");
            if(Directory.Exists(supportDir))
            {
                foreach(var supportFile in Directory.GetFiles(supportDir, "*.mist", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(supportDir, supportFile);
                    var copied = Path.Combine(scratchDir, relative);
                    _ = Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
                    CopyAsLf(supportFile, copied);
                    sources.Add(copied);
                }
            }

            var outputDir = Path.Combine(scratchDir, "output");

            var options = new CompilerOptions(
                ProjectFolder: scratchDir,
                SourcePaths: [.. sources],
                Backend: BackendId.CSharp,
                OutputDirectory: outputDir,
                BinDirectory: Path.Combine(scratchDir, "bin"),
                ProjectName: config?.Name ?? "Snippet",
                NamespaceRoot: config?.NamespaceRoot ?? "Snippet",
                EntryPoint: config?.EntryPoint,
                GlobalUsings: config?.GlobalUsings ?? default,
                Packages: config?.Packages ?? default,
                FrameworkReferences: config?.FrameworkReferences ?? default,
                TargetFramework: config?.TargetFramework ?? "net10.0",
                VerboseResolution: true,
                CsprojProperties: config?.CsprojProperties ?? default,
                OutputType: config?.OutputType ?? ProjectOutputType.Executable);

            if(config is null)
            {
                options = options with { OutputType = SnippetOutputKind.Infer(sources, lexer, parser) };
            }

            timings.Add("prepare", preparation.Elapsed);
            timings.Attempts++;
            var result = driver.Compile(options);

            // A syntactic Main may not have an entry-point signature; only the compiler can settle that case.
            if(result.Diagnostics.Any(d => d.Descriptor == EntryPointDiagnostics.MissingEntryPoint))
            {
                timings.Attempts++;
                result = driver.Compile(options with { OutputType = ProjectOutputType.Library });
            }

            if(!result.Succeeded)
            {
                errors.WriteLine($"  FAILED: {relativeMist}");
                foreach(var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(3))
                {
                    errors.WriteLine($"    {d.Descriptor.Number}: {d.Message}");
                }

                return null;
            }

            // The emitted file is named after the .mist file and sits under the assembly's folder; the
            // rest of the output is runtime support the book does not show.
            var expectedCsName = Path.ChangeExtension(fileName, ".cs");
            var emitted = Directory.GetFiles(outputDir, "*.cs", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).StartsWith("__", StringComparison.Ordinal))
                .OrderBy(path => path)
                .ToList();

            var expectedCsPath = emitted.FirstOrDefault(path => Path.GetFileName(path) == expectedCsName);
            if(expectedCsPath is not null)
            {
                return File.ReadAllText(expectedCsPath);
            }

            var candidates = emitted;

            if(candidates.Count == 0)
            {
                var produced = Directory.Exists(outputDir)
                    ? string.Join(", ", Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(name => name))
                    : "no output directory";
                errors.WriteLine($"  FAILED (no emitted output): {relativeMist} -- output holds: {produced}");
                return null;
            }

            return File.ReadAllText(candidates[0]);
        }
        catch(Exception ex)
        {
            errors.WriteLine($"  FAILED (exception): {relativeMist}: {ex}");
            return null;
        }
        finally
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
    }
}