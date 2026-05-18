// SnippetEmitter — Compiles .mist book snippets and writes the emitted C# to
// their corresponding .cs files. This ensures the book's "Emitted C#" tabs
// always reflect the actual compiler output.
//
// Usage:
//   dotnet run --project tools/SnippetEmitter [snippets-dir]
//
// If no directory is specified, defaults to book/snippets relative to the
// working directory.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

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

        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(tempMistPath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "output"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Snippet",
            NamespaceRoot: "Snippet",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = driver.Compile(options);

        // Check for fatal errors that prevent emission.
        var fatalErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value is not "MIST_TC_0017") // Ignore "no entry point" for non-TLS snippets
            .ToList();

        if (capture.CapturedModel is null)
        {
            Console.Error.WriteLine($"  FAILED (no semantic model): {relativeMist}");
            foreach (var d in fatalErrors.Take(3))
                Console.Error.WriteLine($"    {d.Id.Value}: {d.Message}");
            return null;
        }

        // Emit to memory.
        var emitDiag = new DiagnosticBag();
        var emittedFiles = CSharpEmitter.EmitToMemory(capture.CapturedModel, "Snippet", emitDiag);

        // Find the emitted file (should be named after the .mist file with .cs extension).
        var expectedCsName = Path.ChangeExtension(fileName, ".cs");
        if (!emittedFiles.TryGetValue(expectedCsName, out var csContent))
        {
            // Try the first (and likely only) emitted file.
            if (emittedFiles.Count == 1)
            {
                csContent = emittedFiles.Values.First();
            }
            else if (emittedFiles.Count == 0)
            {
                Console.Error.WriteLine($"  FAILED (no emitted output): {relativeMist}");
                return null;
            }
            else
            {
                // Multiple files emitted — pick the one matching the source file name.
                csContent = emittedFiles.Values.First();
            }
        }

        return csContent;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  FAILED (exception): {relativeMist}: {ex.Message}");
        return null;
    }
    finally
    {
        try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Wraps SemanticModelBuilder to capture the model for EmitToMemory.
/// </summary>
sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
{
    private readonly SemanticModelBuilder _inner = new();
    public ISemanticModel? CapturedModel { get; private set; }

    public ISemanticModel Build(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        IAmbientTypeResolver? ambientTypeResolver,
        string namespaceRoot,
        string projectFolder,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string>? directReferencePaths = null)
    {
        var model = _inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
        CapturedModel = model;
        return model;
    }
}
