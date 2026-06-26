// Feature: drop-semantics, Property 7: Relevant auto-drop fires for every reference
//
// **Validates: Requirements 2.9, 4.4, 6.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// How the relevant local is additionally referenced before reaching scope exit.
/// In every case the original relevant local's scope-exit <c>Drop()</c> MUST still be
/// emitted, because relevant types are aliasable and their <c>Drop()</c> is idempotent —
/// passing or aliasing does NOT elide the original drop (unlike linear move-elision).
/// </summary>
public enum RelevantReferenceKind
{
    /// <summary>The relevant local is passed to another function.</summary>
    PassToFunction,

    /// <summary>The relevant local is aliased to another variable.</summary>
    Alias,

    /// <summary>The relevant local is both aliased and passed to another function.</summary>
    AliasAndPass,
}

/// <summary>
/// A generated scenario: a relevant local that is referenced elsewhere (aliased and/or
/// passed) yet must still receive its own scope-exit <c>Drop()</c> in the emitted C#.
/// </summary>
public sealed class RelevantDropScenario
{
    public string Source { get; }
    public string LocalName { get; }
    public string Description { get; }

    public RelevantDropScenario(string source, string localName, string description)
    {
        Source = source;
        LocalName = localName;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class RelevantDropEveryReferenceGenerators
{
    // Distinct identifier pools so the original local, the alias, and the function
    // parameter never collide — keeping the `{local}.Drop()` assertion unambiguous.
    private static readonly string[] LocalNames = ["res", "handle", "shared", "ticket"];
    private static readonly string[] AliasNames = ["alias", "other", "copy", "ref2"];

    public static Gen<RelevantDropScenario> GenRelevantDropScenario()
    {
        return from local in Gen.Elements(LocalNames)
               from alias in Gen.Elements(AliasNames)
               from kind in Gen.Elements(
                   RelevantReferenceKind.PassToFunction,
                   RelevantReferenceKind.Alias,
                   RelevantReferenceKind.AliasAndPass)
               let body = BuildBody(local, alias, kind)
               let source = BuildSource(body)
               select new RelevantDropScenario(
                   source,
                   local,
                   $"relevant local '{local}' referenced via {kind} still scope-exit drops");
    }

    private static string BuildBody(string local, string alias, RelevantReferenceKind kind)
    {
        // The relevant local is created, referenced according to the variant, and then
        // simply falls out of scope (no explicit Drop()).
        return kind switch
        {
            RelevantReferenceKind.PassToFunction =>
                $"        SharedResource {local} = new SharedResource();\n" +
                $"        Sink.Consume({local});\n",

            RelevantReferenceKind.Alias =>
                $"        SharedResource {local} = new SharedResource();\n" +
                $"        SharedResource {alias} = {local};\n",

            RelevantReferenceKind.AliasAndPass =>
                $"        SharedResource {local} = new SharedResource();\n" +
                $"        SharedResource {alias} = {local};\n" +
                $"        Sink.Consume({local});\n",

            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string BuildSource(string mainBody)
    {
        // A relevant type with a void Drop() and an accessor, a sink that consumes a
        // relevant value (parameter named `arg`, distinct from any local), and a Main
        // whose body is the generated variant.
        return
            "namespace TestProgram;\n\n" +
            "relevant class SharedResource\n" +
            "{\n" +
            "    public void Drop()\n" +
            "    {\n" +
            "    }\n\n" +
            "    public int GetId()\n" +
            "    {\n" +
            "        return 1;\n" +
            "    }\n" +
            "}\n\n" +
            "class Sink\n" +
            "{\n" +
            "    public static void Consume(SharedResource arg)\n" +
            "    {\n" +
            "        int id = arg.GetId();\n" +
            "    }\n" +
            "}\n\n" +
            "class Program\n" +
            "{\n" +
            "    public static void Main(string[] args)\n" +
            "    {\n" +
            mainBody +
            "    }\n" +
            "}\n";
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for relevant auto-drop firing for every reference.
///
/// Feature: drop-semantics, Property 7: Relevant auto-drop fires for every reference
///
/// **Validates: Requirements 2.9, 4.4, 6.3**
///
/// For any local of a relevant type at scope exit, the compiler SHALL always insert a
/// <c>Drop()</c> call, regardless of whether the variable was also passed to other
/// functions or aliased. Compilation runs fully in-process via a shared
/// <see cref="CompilerDriver"/> with <c>EmitToDisk: false</c>; the emitted C# text is
/// inspected directly. Spawns 0 external processes.
/// </summary>
public sealed class RelevantDropEveryReferencePropertyTests : IDisposable
{
    private readonly string outputDir;

    public RelevantDropEveryReferencePropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistRelevantDropPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<RelevantDropScenario> ArbitraryRelevantDropScenario() =>
        RelevantDropEveryReferenceGenerators.GenRelevantDropScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Returns the emitted user C# file (the one containing the test namespace), or null.
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Relevant auto-drop fires for every reference
    //
    // **Validates: Requirements 2.9, 4.4, 6.3**
    //
    // For any relevant local that is aliased and/or passed to another function, the
    // emitted C# MUST still contain the scope-exit `{local}.Drop()` for the original
    // relevant local — relevant drop is never elided by aliasing or passing.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A relevant local that is aliased and/or passed elsewhere still emits its own
    /// scope-exit <c>Drop()</c>.
    /// **Validates: Requirements 2.9, 4.4, 6.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RelevantDropEveryReferencePropertyTests) })]
    public Property RelevantLocal_AlwaysScopeExitDrops_EvenWhenAliasedOrPassed(
        RelevantDropScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The original relevant local's scope-exit Drop() must be present despite the
        // alias / function-pass reference.
        var containsScopeExitDrop = emittedCSharp.Contains($"{scenario.LocalName}.Drop();");

        return containsScopeExitDrop.ToProperty()
            .Label($"Expected emitted C# to contain '{scenario.LocalName}.Drop();' " +
                   $"(relevant drop must not be elided by aliasing/passing).\n" +
                   $"Source:\n{scenario.Source}\nEmitted C#:\n{emittedCSharp}");
    }

    // ─── Example-based variants (explicit, deterministic) ────────────────

    /// <summary>
    /// Variant (a): a relevant local passed to a function still scope-exit drops.
    /// **Validates: Requirements 2.9, 4.4, 6.3**
    /// </summary>
    [Fact]
    public void RelevantLocal_PassedToFunction_StillScopeExitDrops()
    {
        var source = RelevantDropEveryReferenceGenerators_BuildForTest(
            RelevantReferenceKind.PassToFunction, out var local);

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}")));

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);
        Assert.Contains($"{local}.Drop();", emitted);
    }

    /// <summary>
    /// Variant (b): a relevant local aliased to another variable still scope-exit drops
    /// (and the alias also drops — both references drop, idempotently).
    /// **Validates: Requirements 2.9, 4.4, 6.3**
    /// </summary>
    [Fact]
    public void RelevantLocal_Aliased_StillScopeExitDrops()
    {
        var source = RelevantDropEveryReferenceGenerators_BuildForTest(
            RelevantReferenceKind.Alias, out var local);

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}")));

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);
        Assert.Contains($"{local}.Drop();", emitted);
    }

    // Deterministic builder for the example-based tests, reusing the generator's body
    // construction with fixed names.
    private static string RelevantDropEveryReferenceGenerators_BuildForTest(
        RelevantReferenceKind kind, out string local)
    {
        local = "res";
        const string alias = "alias";
        var body = kind switch
        {
            RelevantReferenceKind.PassToFunction =>
                $"        SharedResource res = new SharedResource();\n" +
                $"        Sink.Consume(res);\n",
            RelevantReferenceKind.Alias =>
                $"        SharedResource res = new SharedResource();\n" +
                $"        SharedResource {alias} = res;\n",
            _ =>
                $"        SharedResource res = new SharedResource();\n" +
                $"        SharedResource {alias} = res;\n" +
                $"        Sink.Consume(res);\n",
        };

        return
            "namespace TestProgram;\n\n" +
            "relevant class SharedResource\n" +
            "{\n" +
            "    public void Drop()\n" +
            "    {\n" +
            "    }\n\n" +
            "    public int GetId()\n" +
            "    {\n" +
            "        return 1;\n" +
            "    }\n" +
            "}\n\n" +
            "class Sink\n" +
            "{\n" +
            "    public static void Consume(SharedResource arg)\n" +
            "    {\n" +
            "        int id = arg.GetId();\n" +
            "    }\n" +
            "}\n\n" +
            "class Program\n" +
            "{\n" +
            "    public static void Main(string[] args)\n" +
            "    {\n" +
            body +
            "    }\n" +
            "}\n";
    }
}