// Feature: drop-semantics, Property 17: Modifier erasure
//
// **Validates: Requirements 14.7**

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for substructural modifier erasure in emitted C#.
///
/// Feature: drop-semantics, Property 17: Modifier erasure
/// **Validates: Requirements 14.7**
///
/// For any Mist program containing types declared with the <c>affine</c>,
/// <c>linear</c>, or <c>relevant</c> modifiers, the emitted C# source SHALL NOT
/// contain these keywords as standalone tokens — substructural modifiers have no
/// runtime representation and are erased by the emitter.
///
/// Compilation runs entirely in-process via a shared <see cref="CompilerDriver"/>
/// with <c>EmitToDisk: false</c>; the emitted C# files are read back from
/// <c>result.EmittedFiles</c> and concatenated for inspection.
/// Spawns 0 external processes.
/// </summary>
public sealed class ModifierErasurePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Compiles the source in-process and returns the emitted C# files (excluding
    /// the project file), or <c>null</c> if compilation failed.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? CompileAndGetEmittedFiles(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_modifier_erasure_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);

            if(!result.Succeeded || result.EmittedFiles is null)
                return null;

            return result.EmittedFiles;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Concatenates the emitted C# source files (skipping <c>.csproj</c> files).
    /// </summary>
    private static string ConcatEmittedCSharp(IReadOnlyDictionary<string, string> emittedFiles)
    {
        var sb = new StringBuilder();
        foreach(var (fileName, content) in emittedFiles)
        {
            if(fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine(content);
        }
        return sb.ToString();
    }

    // The substructural modifiers that must be erased from emitted C#.
    private static readonly string[] SubstructuralKeywords = ["affine", "linear", "relevant"];

    /// <summary>
    /// Returns the first substructural keyword present in <paramref name="emitted"/>
    /// as a standalone word (word-boundary match), or <c>null</c> if none are present.
    /// Using a word-boundary regex (rather than a naive substring check) avoids false
    /// positives from identifiers that merely contain one of these substrings.
    /// </summary>
    private static string? FindStandaloneKeyword(string emitted)
    {
        foreach(var keyword in SubstructuralKeywords)
        {
            if(Regex.IsMatch(emitted, $@"\b{Regex.Escape(keyword)}\b"))
                return keyword;
        }
        return null;
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A program declaring a substructural type with fields, a <c>Drop()</c> method,
    /// and some usage. Type and member names are deliberately chosen to NOT contain
    /// the substrings <c>affine</c>/<c>linear</c>/<c>relevant</c>, so even a plain
    /// substring scan would be valid; the test still uses a word-boundary check for
    /// robustness.
    /// </summary>
    public sealed record ModifierScenario(string Source, string Discipline, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools (none contain affine/linear/relevant as substrings) ──

    private static readonly string[] Disciplines = ["affine", "linear", "relevant"];

    private static readonly string[] TypeNames =
        ["Handle", "Buffer", "Socket", "Resource", "Token", "Connection"];

    private static readonly string[] FieldNames =
        ["count", "size", "value", "depth", "width", "offset"];

    private static readonly string[] MethodNames =
        ["Use", "Access", "Touch", "Read", "Inspect", "Peek"];

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<ModifierScenario> GenModifierScenario()
    {
        return from discipline in Gen.Elements(Disciplines)
               from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               from methodName in Gen.Elements(MethodNames)
               select Build(discipline, typeName, fieldName, methodName);
    }

    private static ModifierScenario Build(
        string discipline, string typeName, string fieldName, string methodName)
    {
        var source = $$"""
            using Standard;

            {{discipline}} class {{typeName}}
            {
                int {{fieldName}};

                public {{typeName}}()
                {
                    this.{{fieldName}} = 0;
                }

                public void Drop()
                {
                }

                public int {{methodName}}()
                {
                    return {{fieldName}};
                }
            }
            class Program
            {
                public static void Main()
                {
                    {{typeName}} h = new {{typeName}}();
                    int x = h.{{methodName}}();
                }
            }
            """;

        var label = $"discipline={discipline}, type={typeName}, field={fieldName}, method={methodName}";
        return new ModifierScenario(source, discipline, label);
    }

    public static Arbitrary<ModifierScenario> ArbitraryModifierScenario() =>
        GenModifierScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 17: Substructural modifiers erased in emitted C#
    //
    // For any program containing affine/linear/relevant types, the emitted C#
    // contains none of the keywords `affine`, `linear`, or `relevant` as
    // standalone tokens.
    //
    // **Validates: Requirements 14.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ModifierErasurePropertyTests) })]
    public Property SubstructuralModifiers_AreErased_InEmittedCSharp(ModifierScenario scenario)
    {
        var emittedFiles = CompileAndGetEmittedFiles(scenario.Source);

        if(emittedFiles is null)
        {
            return false.ToProperty()
                .Label($"Compilation failed for {scenario.Label}.\nSource:\n{scenario.Source}");
        }

        var emitted = ConcatEmittedCSharp(emittedFiles);
        var offending = FindStandaloneKeyword(emitted);

        return (offending is null).ToProperty()
            .Label($"Emitted C# contains substructural keyword '{offending}' for {scenario.Label}.\n"
                + $"Emitted C#:\n{emitted}");
    }
}