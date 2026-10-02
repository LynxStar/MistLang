// SnippetEmitter — Compiles .mist book snippets and writes the emitted C# to
// their corresponding .cs files. This ensures the book's
// "Emitted C#" tabs always reflect the actual compiler output.
//
// Usage:
//   dotnet run --project tools/SnippetEmitter [snippets-dir]
//
// If no directory is specified, defaults to book/snippets relative to the
// working directory.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

var snippetsDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("book/snippets");

if (!Directory.Exists(snippetsDir))
{
    Console.Error.WriteLine($"error: snippets directory not found: {snippetsDir}");
    return 1;
}

// Find all .mist files that have a corresponding .cs file.
var mistFiles = Directory.GetFiles(snippetsDir, "*.mist", SearchOption.AllDirectories);
var pairs = new List<(string MistPath, string CsPath)>();

foreach (var mistPath in mistFiles)
{
    var csPath = Path.ChangeExtension(mistPath, ".cs");
    if (File.Exists(csPath))
    {
        pairs.Add((mistPath, csPath));
    }
}

Console.WriteLine($"Found {pairs.Count} .mist/.cs snippet pairs in {snippetsDir}");

var updated = 0;
var skipped = 0;
var failed = 0;

foreach (var (mistPath, csPath) in pairs.OrderBy(p => p.MistPath))
{
    var relativeMist = Path.GetRelativePath(snippetsDir, mistPath);
    var emitted = EmitSnippet(mistPath);

    if (emitted is null)
    {
        failed++;
        continue;
    }

    // Normalize line endings and ensure trailing newline.
    emitted = emitted.ReplaceLineEndings("\n");
    if (!emitted.EndsWith('\n'))
        emitted += "\n";

    var existing = File.ReadAllText(csPath).ReplaceLineEndings("\n");
    if (existing == emitted)
    {
        skipped++;
    }
    else
    {
        File.WriteAllText(csPath, emitted);
        Console.WriteLine($"  updated: {relativeMist}");
        updated++;
    }
}

Console.WriteLine();
Console.WriteLine($"Done: {updated} updated, {skipped} unchanged, {failed} failed");

return failed > 0 ? 1 : 0;

// --- Compilation logic ---

IReadOnlyList<string> Companions(string mistPath)
{
    var found = new List<string>();
    var pending = new Stack<string>();
    pending.Push(mistPath);

    while (pending.Count > 0)
    {
        var current = pending.Pop();
        var directory = Path.GetDirectoryName(current)!;

        foreach (var line in File.ReadLines(current).TakeWhile(l => l.Length == 0 || l.StartsWith("//", StringComparison.Ordinal) || l.StartsWith("using ", StringComparison.Ordinal)))
        {
            var match = Regex.Match(line, @"^//\s*with:\s*(.+)$");
            if (!match.Success)
                continue;

            foreach (var name in match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var companion = Path.GetFullPath(Path.Combine(directory, name));
                if (companion != mistPath && !found.Contains(companion))
                {
                    found.Add(companion);
                    pending.Push(companion);
                }
            }
        }
    }

    return found;
}

string? EmitSnippet(string mistPath)
{
    var relativeMist = Path.GetRelativePath(snippetsDir, mistPath);
    var scratchDir = Path.Combine(Path.GetTempPath(), "mist-snippet-emit-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratchDir);

    try
    {
        var source = File.ReadAllText(mistPath);
        var fileName = Path.GetFileName(mistPath);

        // Copy the .mist file to a temp directory for compilation.
        var tempMistPath = Path.Combine(scratchDir, fileName);
        File.WriteAllText(tempMistPath, source);

        // A snippet that needs a package or a framework reference says so in a project file beside
        // it, named as the snippet is, which becomes the scratch project's mist.toml.
        var sidecarToml = Path.ChangeExtension(mistPath, ".toml");
        ProjectConfiguration? config = null;
        if (File.Exists(sidecarToml))
        {
            File.Copy(sidecarToml, Path.Combine(scratchDir, "mist.toml"));
            var tomlDiagnostics = new DiagnosticBag();
            config = ProjectFileParser.Parse(scratchDir, tomlDiagnostics);
            if (config is null)
            {
                Console.Error.WriteLine($"  FAILED (project file): {relativeMist}");
                foreach (var d in tomlDiagnostics.ToImmutableArray().Take(3))
                    Console.Error.WriteLine($"    {d.Id.Value}: {d.Message}");
                return null;
            }
        }

        // A chapter's snippets build on one another, so a snippet names the siblings it uses with a
        // `// with: a.mist, b.mist` line and they are compiled alongside it, each bringing its own
        // companions; the book shows only the snippet. A demonstration that needs a folder layout --
        // a namespace inferred from a folder -- keeps its other files in a folder beside it instead.
        var sources = new List<string> { tempMistPath };
        foreach (var companion in Companions(mistPath))
        {
            var copied = Path.Combine(scratchDir, Path.GetFileName(companion));
            File.Copy(companion, copied);
            sources.Add(copied);
        }

        var supportDir = Path.Combine(Path.GetDirectoryName(mistPath)!, Path.GetFileNameWithoutExtension(mistPath) + ".support");
        if (Directory.Exists(supportDir))
        {
            foreach (var supportFile in Directory.GetFiles(supportDir, "*.mist", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(supportDir, supportFile);
                var copied = Path.Combine(scratchDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
                File.Copy(supportFile, copied);
                sources.Add(copied);
            }
        }

        var outputDir = Path.Combine(scratchDir, "output");
        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser());

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
            OutputType: config?.OutputType ?? ProjectOutputType.Executable);

        var result = driver.Compile(options);

        // A snippet without a Main is either a top-level-statements program or a library, which a
        // project file would say and a snippet cannot; the emitter refuses an executable without an
        // entry point, so each shape is tried in turn and the emitted C# is the same either way.
        if (result.Diagnostics.Any(d => d.Id.Value == "MIST_MISSING_ENTRY_POINT"))
        {
            result = driver.Compile(options with { EntryPoint = "top-level" });

            if (!result.Succeeded)
            {
                result = driver.Compile(options with { OutputType = ProjectOutputType.Library });
            }
        }

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"  FAILED: {relativeMist}");
            foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(3))
                Console.Error.WriteLine($"    {d.Id.Value}: {d.Message}");
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
        if (expectedCsPath is not null)
        {
            return File.ReadAllText(expectedCsPath);
        }

        var candidates = emitted;

        if (candidates.Count == 0)
        {
            var produced = Directory.Exists(outputDir)
                ? string.Join(", ", Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(name => name))
                : "no output directory";
            Console.Error.WriteLine($"  FAILED (no emitted output): {relativeMist} -- output holds: {produced}");
            return null;
        }

        return File.ReadAllText(candidates[0]);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  FAILED (exception): {relativeMist}: {ex}");
        return null;
    }
    finally
    {
        try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
    }
}
