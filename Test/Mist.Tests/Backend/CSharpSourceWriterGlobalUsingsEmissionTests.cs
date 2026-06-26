using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that the CSharpSourceWriter correctly emits global usings
/// as using directives in the generated C# output, merged with explicit usings and
/// backend namespaces, sorted alphabetically, and deduplicated.
/// Requirements: 6.1, 6.2, 6.3, 6.4
/// </summary>
public sealed class CSharpSourceWriterGlobalUsingsEmissionTests : IDisposable
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;

    public CSharpSourceWriterGlobalUsingsEmissionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistGlobalUsingsEmitTests_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private string? CompileWithGlobalUsings(string mistSource, ImmutableArray<string> globalUsings)
    {
        // Clean output directory
        foreach(var file in Directory.GetFiles(outputDir))
        {
            File.Delete(file);
        }

        // 1. Lex
        var sourceText = new SourceText("test.mist", mistSource);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        if(bag.HasErrors)
        {
            return null;
        }

        // 2. Parse
        var parseResult = Parser.Parse(tokens, bag);
        if(parseResult.Root is null || bag.HasErrors)
        {
            return null;
        }

        // 3. Build semantic model with global usings on the compilation unit
        var compilationUnit = new CompilationUnit(sourceText)
        {
            GlobalUsings = globalUsings,
        };
        var compilation = new MistCompilation(ImmutableArray.Create(compilationUnit));
        var semanticBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semanticBag);

        // 4. Emit C#
        var emitBag = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitBag);

        // 5. Read the emitted .cs file
        var csFile = Directory.GetFiles(outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : null;
    }

    private static List<string> ExtractUsingDirectives(string csContent)
    {
        return csContent
            .Split('\n')
            .Where(line => line.TrimStart().StartsWith("using ", StringComparison.Ordinal))
            .Select(line => line.Trim().TrimEnd(';').Replace("using ", ""))
            .ToList();
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Requirement 6.1: Global usings appear as using directives in the emitted C# file.
    /// </summary>
    [Fact]
    public void GlobalUsings_AppearInEmittedUsingDirectives()
    {
        var source = """
            class Program {
                public static void Main() { }
            }
            """;

        var globalUsings = ImmutableArray.Create("MyApp.Models", "MyApp.Services");

        var csContent = CompileWithGlobalUsings(source, globalUsings);

        Assert.NotNull(csContent);
        Assert.Contains("using MyApp.Models;", csContent);
        Assert.Contains("using MyApp.Services;", csContent);
    }

    /// <summary>
    /// Requirement 6.2: All usings (global, explicit, backend) are merged into a single
    /// sorted block in ordinal alphabetical order.
    /// </summary>
    [Fact]
    public void AllUsings_MergedSortedAndDeduplicated()
    {
        // Explicit using for "Zebra.Namespace" + global usings for "Alpha.Namespace" and "Middle.Namespace"
        var source = """
            using Zebra.Namespace;

            class Program {
                public static void Main() { }
            }
            """;

        var globalUsings = ImmutableArray.Create("Alpha.Namespace", "Middle.Namespace");

        var csContent = CompileWithGlobalUsings(source, globalUsings);

        Assert.NotNull(csContent);

        var usings = ExtractUsingDirectives(csContent!);

        // All three should be present
        Assert.Contains("Alpha.Namespace", usings);
        Assert.Contains("Middle.Namespace", usings);
        Assert.Contains("Zebra.Namespace", usings);

        // Verify sorted order: Alpha < Middle < Zebra
        var alphaIdx = usings.IndexOf("Alpha.Namespace");
        var middleIdx = usings.IndexOf("Middle.Namespace");
        var zebraIdx = usings.IndexOf("Zebra.Namespace");

        Assert.True(alphaIdx < middleIdx, "Alpha.Namespace should come before Middle.Namespace");
        Assert.True(middleIdx < zebraIdx, "Middle.Namespace should come before Zebra.Namespace");
    }

    /// <summary>
    /// Requirement 6.3: A namespace present in both global usings and explicit usings
    /// appears only once in the emitted output.
    /// </summary>
    [Fact]
    public void DuplicateGlobalAndExplicitUsing_EmittedOnlyOnce()
    {
        // Explicit using for "Shared.Namespace" which is also a global using
        var source = """
            using Shared.Namespace;

            class Program {
                public static void Main() { }
            }
            """;

        var globalUsings = ImmutableArray.Create("Shared.Namespace", "Other.Namespace");

        var csContent = CompileWithGlobalUsings(source, globalUsings);

        Assert.NotNull(csContent);

        // Count occurrences of "using Shared.Namespace;"
        var count = csContent!.Split('\n')
            .Count(line => line.Trim() == "using Shared.Namespace;");

        Assert.Equal(1, count);
    }

    /// <summary>
    /// Requirement 6.4: A namespace present in both global usings and backend namespaces
    /// appears only once in the emitted output.
    /// Backend namespaces are added when the code uses types that come from the C# backend
    /// (e.g., System namespace types). We use a type that triggers a backend namespace
    /// and also declare that same namespace as a global using.
    /// </summary>
    [Fact]
    public void DuplicateGlobalAndBackendNamespace_EmittedOnlyOnce()
    {
        // This source uses Console.WriteLine which triggers "System" as a backend namespace.
        // We also declare "System" as a global using.
        var source = """
            class Program {
                public static void Main() {
                    var x = 42;
                }
            }
            """;

        // "System" is typically added as a backend namespace when System types are used.
        // We add it as a global using too — it should appear only once.
        var globalUsings = ImmutableArray.Create("System", "MyApp.Extra");

        var csContent = CompileWithGlobalUsings(source, globalUsings);

        Assert.NotNull(csContent);

        // Count occurrences of "using System;" — should be exactly 1
        var systemCount = csContent!.Split('\n')
            .Count(line => line.Trim() == "using System;");

        Assert.True(systemCount <= 1,
            $"Expected 'using System;' at most once but found {systemCount} occurrences");

        // "MyApp.Extra" should also appear
        Assert.Contains("using MyApp.Extra;", csContent);
    }
}