// Feature: type-unions, Property 13: Helper Type Structural Correctness
// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
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

/// <summary>
/// Property-based tests verifying that the C# emitter generates structurally correct
/// helper types for union types.
///
/// Property 13: For any distinct union type with N constituents used in a program,
/// the emitter SHALL generate exactly one helper struct with: a tag field, N Is*
/// properties, N As* extraction properties, and N implicit conversion operators.
/// The same union type (same constituents regardless of source order) SHALL reuse
/// the same helper type.
///
/// Uses in-process compilation via shared CompilerDriver — no per-iteration process spawning.
/// </summary>
public sealed class UnionEmitterPropertyTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public UnionEmitterPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_union_emitter_prop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Known primitive types for generation ────────────────────────────

    /// <summary>
    /// Mist type names paired with their expected C# CLR names used in the helper struct.
    /// </summary>
    private static readonly (string MistName, string CSharpName)[] KnownTypes =
    [
        ("int", "Int32"),
        ("string", "String"),
        ("bool", "Boolean"),
        ("double", "Double"),
        ("float", "Single"),
        ("long", "Int64"),
        ("byte", "Byte"),
        ("char", "Char"),
    ];

    // ─── Scenario model ──────────────────────────────────────────────────

    /// <summary>
    /// A test scenario representing a union type with N distinct constituents.
    /// </summary>
#pragma warning disable CA1034
    public sealed class UnionScenario
    {
        public ImmutableArray<int> TypeIndices { get; }

        public UnionScenario(ImmutableArray<int> typeIndices)
        {
            TypeIndices = typeIndices;
        }

        public override string ToString()
        {
            var names = TypeIndices.Select(i => KnownTypes[i].MistName);
            return string.Join(" | ", names);
        }
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    public static Arbitrary<UnionScenario> ArbitraryUnionScenarios()
    {
        // Generate 2-4 distinct type indices from KnownTypes.
        // Always include at least one type with an unambiguous literal (int, string, bool, char)
        // so we can assign a value to the union variable without type errors.
        var unambiguousIndices = new[] { 0, 1, 2, 7 }; // int, string, bool, char

        var gen =
            from count in Gen.Choose(2, 4)
            from guaranteedIdx in Gen.Elements(unambiguousIndices)
            from otherIndices in Gen.Shuffle(
                Enumerable.Range(0, KnownTypes.Length)
                    .Where(i => i != guaranteedIdx)
                    .ToArray())
            let selected = new[] { guaranteedIdx }
                .Concat(otherIndices.Take(count - 1))
                .ToImmutableArray()
            select new UnionScenario(selected);

        return gen.ToArbitrary();
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal Mist program that uses the given union type in a variable declaration,
    /// compiles it in-process, and returns the content of the emitted UnionHelpers.cs file.
    /// Uses in-memory emit (EmitToDisk=false) to avoid disk I/O per iteration.
    /// </summary>
    private (bool Succeeded, string? UnionHelpersContent, string DiagnosticText) CompileAndGetHelpers(UnionScenario scenario)
    {
        var iterDir = Path.Combine(_outputDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iterDir);

        try
        {
            // Build the union type string from the scenario
            var mistTypeNames = scenario.TypeIndices.Select(i => KnownTypes[i].MistName).ToArray();
            var unionTypeStr = string.Join(" | ", mistTypeNames);
            var (defaultLiteral, _) = GetAssignableLiteral(scenario.TypeIndices);

            // Build a minimal Mist program using the union type
            var source = string.Join("\n",
                "using Standard;",
                "",
                "namespace TestProgram;",
                "",
                "public class Program {",
                "    public static void Main(string[] args) {",
               $"        {unionTypeStr} x = {defaultLiteral};",
                "        Console.WriteLine(\"ok\");",
                "    }",
                "}");

            var sourcePath = Path.Combine(iterDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var binDir = Path.Combine(iterDir, "bin");
            var options = new CompilerOptions(
                ProjectFolder: iterDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: iterDir,
                BinDirectory: binDir,
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);

            if(!result.Succeeded)
            {
                var diagnosticText = string.Join("\n",
                    result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"));
                return (false, null, diagnosticText);
            }

            // Read the UnionHelpers.cs from in-memory emitted files
            if(result.EmittedFiles is null || !result.EmittedFiles.TryGetValue("UnionHelpers.cs", out var content))
            {
                return (false, null, "UnionHelpers.cs was not emitted");
            }

            return (true, content, "");
        }
        finally
        {
            try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Compiles two Mist programs with the same union constituents in different orders
    /// and returns both UnionHelpers.cs contents.
    /// </summary>
    private (string? Helpers1, string? Helpers2, string DiagnosticText) CompileWithReorderedUnion(UnionScenario scenario)
    {
        // First order: as given
        var (ok1, helpers1, diag1) = CompileAndGetHelpers(scenario);
        if(!ok1)
        {
            return (null, null, diag1);
        }

        // Second order: reversed
        var reversed = new UnionScenario(scenario.TypeIndices.Reverse().ToImmutableArray());
        var (ok2, helpers2, diag2) = CompileAndGetHelpers(reversed);
        if(!ok2)
        {
            return (null, null, diag2);
        }

        return (helpers1, helpers2, "");
    }

    /// <summary>
    /// Returns a literal value that the Mist type checker will accept for the given type.
    /// Note: numeric literals like 100 are typed as int by default, so we use string
    /// or bool literals when those types are in the union, falling back to int when available.
    /// </summary>
    private static (string Literal, string MistType) GetAssignableLiteral(ImmutableArray<int> typeIndices)
    {
        // Prefer types with unambiguous literals: string, bool, int, char
        // These are types where the literal syntax unambiguously identifies the type.
        var preferredOrder = new[] { "string", "bool", "int", "char" };

        foreach(var preferred in preferredOrder)
        {
            var idx = typeIndices.FirstOrDefault(i => KnownTypes[i].MistName == preferred, -1);
            if(idx >= 0)
            {
                var literal = KnownTypes[idx].MistName switch
                {
                    "string" => "\"hello\"",
                    "bool" => "true",
                    "int" => "42",
                    "char" => "'a'",
                    _ => "0",
                };
                return (literal, KnownTypes[idx].MistName);
            }
        }

        // Fallback: use the first type with a cast-style literal
        // This shouldn't happen with our KnownTypes set since it always includes
        // at least one of the preferred types in a 2-4 element subset of 8 types.
        return ("42", "int");
    }

    /// <summary>
    /// Computes the expected helper type name for a scenario (sorted alphabetically by C# name).
    /// </summary>
    private static string GetExpectedHelperName(UnionScenario scenario)
    {
        var csharpNames = scenario.TypeIndices
            .Select(i => KnownTypes[i].CSharpName)
            .OrderBy(n => n, StringComparer.Ordinal);
        return "Union_" + string.Join("_", csharpNames);
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 13a: The emitted helper struct contains a _tag field of type byte.
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UnionEmitterPropertyTests) }, MaxTest = 100)]
    public Property HelperStruct_ContainsTagField(UnionScenario scenario)
    {
        var (succeeded, content, diagnosticText) = CompileAndGetHelpers(scenario);

        return (succeeded && content is not null &&
                content.Contains("private readonly byte _tag;"))
            .ToProperty()
            .Label($"Expected _tag field in helper struct. Succeeded={succeeded}, Diagnostics: {diagnosticText}");
    }

    /// <summary>
    /// Property 13b: The emitted helper struct contains exactly N Is* properties.
    /// **Validates: Requirements 9.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UnionEmitterPropertyTests) }, MaxTest = 100)]
    public Property HelperStruct_ContainsNIsProperties(UnionScenario scenario)
    {
        var (succeeded, content, diagnosticText) = CompileAndGetHelpers(scenario);

        if(!succeeded || content is null)
        {
            return false.ToProperty().Label($"Compilation failed: {diagnosticText}");
        }

        var n = scenario.TypeIndices.Length;
        var isPropertyCount = Regex.Matches(content, @"public bool Is\w+ =>").Count;

        // Verify each expected Is* property exists
        var allPresent = scenario.TypeIndices.All(i =>
            content.Contains($"public bool Is{KnownTypes[i].CSharpName} =>"));

        return (isPropertyCount == n && allPresent)
            .ToProperty()
            .Label($"Expected {n} Is* properties, found {isPropertyCount}. AllPresent={allPresent}");
    }

    /// <summary>
    /// Property 13c: The emitted helper struct contains exactly N As* properties.
    /// **Validates: Requirements 9.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UnionEmitterPropertyTests) }, MaxTest = 100)]
    public Property HelperStruct_ContainsNAsProperties(UnionScenario scenario)
    {
        var (succeeded, content, diagnosticText) = CompileAndGetHelpers(scenario);

        if(!succeeded || content is null)
        {
            return false.ToProperty().Label($"Compilation failed: {diagnosticText}");
        }

        var n = scenario.TypeIndices.Length;
        var asPropertyCount = Regex.Matches(content, @"public \w+ As\w+ =>").Count;

        // Verify each expected As* property exists
        var allPresent = scenario.TypeIndices.All(i =>
            content.Contains($"As{KnownTypes[i].CSharpName} =>"));

        return (asPropertyCount == n && allPresent)
            .ToProperty()
            .Label($"Expected {n} As* properties, found {asPropertyCount}. AllPresent={allPresent}");
    }

    /// <summary>
    /// Property 13d: The emitted helper struct contains exactly N implicit conversion operators.
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UnionEmitterPropertyTests) }, MaxTest = 100)]
    public Property HelperStruct_ContainsNImplicitOperators(UnionScenario scenario)
    {
        var (succeeded, content, diagnosticText) = CompileAndGetHelpers(scenario);

        if(!succeeded || content is null)
        {
            return false.ToProperty().Label($"Compilation failed: {diagnosticText}");
        }

        var n = scenario.TypeIndices.Length;
        var helperName = GetExpectedHelperName(scenario);
        var implicitOpCount = Regex.Matches(content,
            $@"public static implicit operator {Regex.Escape(helperName)}\(").Count;

        return (implicitOpCount == n)
            .ToProperty()
            .Label($"Expected {n} implicit operators, found {implicitOpCount}");
    }

    /// <summary>
    /// Property 13e: The helper struct name matches the expected naming convention
    /// (Union_ + sorted constituent C# names joined by _).
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UnionEmitterPropertyTests) }, MaxTest = 100)]
    public Property HelperStruct_HasCorrectName(UnionScenario scenario)
    {
        var (succeeded, content, diagnosticText) = CompileAndGetHelpers(scenario);

        if(!succeeded || content is null)
        {
            return false.ToProperty().Label($"Compilation failed: {diagnosticText}");
        }

        var expectedName = GetExpectedHelperName(scenario);
        var containsStructDecl = content.Contains($"public struct {expectedName}");

        return containsStructDecl
            .ToProperty()
            .Label($"Expected struct named '{expectedName}' in UnionHelpers.cs");
    }

    /// <summary>
    /// Property 13f: The same union type (same constituents regardless of source order)
    /// reuses the same helper type name.
    /// **Validates: Requirements 9.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UnionEmitterPropertyTests) }, MaxTest = 100)]
    public Property SameConstituents_DifferentOrder_ProduceSameHelperName(UnionScenario scenario)
    {
        var (helpers1, helpers2, diagnosticText) = CompileWithReorderedUnion(scenario);

        if(helpers1 is null || helpers2 is null)
        {
            return false.ToProperty().Label($"Compilation failed: {diagnosticText}");
        }

        var expectedName = GetExpectedHelperName(scenario);

        // Both should contain the same struct declaration
        var both = helpers1.Contains($"public struct {expectedName}") &&
                   helpers2.Contains($"public struct {expectedName}");

        return both.ToProperty()
            .Label($"Expected both orderings to produce struct '{expectedName}'");
    }
}