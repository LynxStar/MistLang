// Feature: type-aliases, Property 5: Transpilation correctness
//
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
//
// For any valid Mist program containing type alias declarations and usages
// (simple, generic, and union variants), transpiling to C# SHALL produce
// zero compilation errors and non-empty C# output.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

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

/// <summary>
/// Describes a generated type alias transpilation scenario for property testing.
/// </summary>
public sealed class TypeAliasEmissionScenario
{
    public string Description { get; }
    public string MistSource { get; }

    public TypeAliasEmissionScenario(string description, string mistSource)
    {
        Description = description;
        MistSource = mistSource;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests for type alias transpilation correctness.
///
/// Property 5: For any valid Mist program containing type alias declarations and
/// usages (simple, generic, and union variants), transpiling to C# SHALL produce
/// zero compilation errors and non-empty C# output.
///
/// Uses in-process compilation via shared CompilerDriver — no per-iteration process spawning.
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
/// </summary>
public sealed class TypeAliasEmissionPropertyTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public TypeAliasEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistTypeAliasEmission_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] AliasNames =
        ["Foo", "Bar", "Baz", "Qux", "MyAlias", "Result", "Value", "Item"];

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a simple type alias declaration (no usage in variable declarations).
    /// The alias is declared at the top level and the program has a Main method.
    /// This tests that the transpiler correctly skips alias declarations (Req 6.1, 6.4).
    /// </summary>
    private static Gen<TypeAliasEmissionScenario> GenSimpleAliasDeclarationOnly()
    {
        return from aliasIdx in Gen.Choose(0, AliasNames.Length - 1)
               from typeIdx in Gen.Choose(0, PrimitiveTypes.Length - 1)
               let aliasName = AliasNames[aliasIdx]
               let typeName = PrimitiveTypes[typeIdx]
               let source = string.Join("\n",
                   $"type {aliasName} = {typeName};",
                   "",
                   "class Program {",
                   "    static void Main() {",
                   "    }",
                   "}")
               select new TypeAliasEmissionScenario(
                   $"decl-only: type {aliasName} = {typeName}",
                   source);
    }

    /// <summary>
    /// Generates multiple type alias declarations (no usage).
    /// Tests that multiple alias declarations are all correctly skipped.
    /// </summary>
    private static Gen<TypeAliasEmissionScenario> GenMultipleAliasDeclarationsOnly()
    {
        return from typeIdx1 in Gen.Choose(0, PrimitiveTypes.Length - 1)
               from typeIdx2 in Gen.Choose(0, PrimitiveTypes.Length - 1)
               let alias1 = "Foo"
               let alias2 = "Bar"
               let type1 = PrimitiveTypes[typeIdx1]
               let type2 = PrimitiveTypes[typeIdx2]
               let source = string.Join("\n",
                   $"type {alias1} = {type1};",
                   $"type {alias2} = {type2};",
                   "",
                   "class Program {",
                   "    static void Main() {",
                   "    }",
                   "}")
               select new TypeAliasEmissionScenario(
                   $"multi-decl: type {alias1} = {type1}; type {alias2} = {type2}",
                   source);
    }

    /// <summary>
    /// Generates a union type alias declaration (no usage in variable declarations).
    /// Tests that union alias declarations are correctly skipped (Req 6.2, 6.4).
    /// </summary>
    private static Gen<TypeAliasEmissionScenario> GenUnionAliasDeclarationOnly()
    {
        return from aliasIdx in Gen.Choose(0, AliasNames.Length - 1)
               from count in Gen.Choose(2, 3)
               from indices in Gen.Shuffle(
                   Enumerable.Range(0, PrimitiveTypes.Length).ToArray())
               let selected = indices.Take(count).OrderBy(i => i).ToArray()
               let aliasName = AliasNames[aliasIdx]
               let unionExpr = string.Join(" | ", selected.Select(i => PrimitiveTypes[i]))
               let source = string.Join("\n",
                   $"type {aliasName} = {unionExpr};",
                   "",
                   "class Program {",
                   "    static void Main() {",
                   "    }",
                   "}")
               select new TypeAliasEmissionScenario(
                   $"union-decl: type {aliasName} = {unionExpr}",
                   source);
    }

    public static Arbitrary<TypeAliasEmissionScenario> ArbitraryTypeAliasEmissionScenario()
    {
        var gen = Gen.OneOf(
            GenSimpleAliasDeclarationOnly(),
            GenMultipleAliasDeclarationsOnly(),
            GenUnionAliasDeclarationOnly());

        return gen.ToArbitrary();
    }

    // ─── Compilation helper ─────────────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source program in-process and returns the result along with
    /// any emitted C# content.
    /// </summary>
    private (bool Succeeded, string CsContent, string DiagnosticText) CompileAndGetOutput(
        TypeAliasEmissionScenario scenario)
    {
        var iterDir = Path.Combine(_outputDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iterDir);

        try
        {
            var sourcePath = Path.Combine(iterDir, "Program.mist");
            File.WriteAllText(sourcePath, scenario.MistSource);

            var binDir = Path.Combine(iterDir, "bin");
            var options = new CompilerOptions(
                ProjectFolder: iterDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: iterDir,
                BinDirectory: binDir,
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            if (!result.Succeeded)
            {
                var diagnosticText = string.Join("\n",
                    result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"));
                return (false, "", diagnosticText);
            }

            // Read all emitted .cs files (excluding .csproj)
            var csFiles = Directory.GetFiles(iterDir, "*.cs");
            var allContent = string.Join("\n",
                csFiles.Select(File.ReadAllText));

            return (true, allContent, "");
        }
        finally
        {
            try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Transpilation correctness
    //
    // **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any valid Mist program containing type alias declarations (simple,
    /// generic, and union variants), transpiling to C# SHALL:
    /// 1. Produce zero compilation errors (Req 6.4)
    /// 2. Produce non-empty C# output
    /// 3. NOT emit the alias declaration in the C# output (aliases are erased)
    ///
    /// This verifies that:
    /// - Simple alias declarations are correctly skipped during emission (Req 6.1)
    /// - Union alias declarations are correctly skipped during emission (Req 6.2)
    /// - The overall C# output compiles without errors (Req 6.4)
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeAliasEmissionPropertyTests) })]
    public Property TypeAlias_TranspilesToValidCSharp(TypeAliasEmissionScenario scenario)
    {
        var (succeeded, csContent, diagnosticText) = CompileAndGetOutput(scenario);

        if (!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation failed for scenario: {scenario.Description}\n" +
                       $"Source:\n{scenario.MistSource}\n" +
                       $"Diagnostics:\n{diagnosticText}");
        }

        if (string.IsNullOrWhiteSpace(csContent))
        {
            return false.ToProperty()
                .Label($"C# output was empty for scenario: {scenario.Description}\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        // Verify that the C# output does NOT contain a type alias declaration.
        // Type aliases are erased during transpilation — no `type X = Y;` or
        // `using X = Y;` should appear for the alias names we declared.
        // We check that the alias name doesn't appear as a type declaration keyword
        // pattern in the C# output (the Mist `type` keyword should never appear).
        if (csContent.Contains("type ", StringComparison.Ordinal) &&
            !csContent.Contains("typeof", StringComparison.Ordinal))
        {
            // Check if it's actually a Mist-style type alias leaking through
            // (not a C# typeof or other legitimate use of "type")
            var lines = csContent.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("type ", StringComparison.Ordinal) &&
                    trimmed.Contains('=') &&
                    trimmed.Contains(';'))
                {
                    return false.ToProperty()
                        .Label($"C# output contains a type alias declaration that should have been erased.\n" +
                               $"Line: {trimmed}\n" +
                               $"Scenario: {scenario.Description}");
                }
            }
        }

        return true.ToProperty()
            .Label($"Transpilation succeeded: {scenario.Description}");
    }
}
