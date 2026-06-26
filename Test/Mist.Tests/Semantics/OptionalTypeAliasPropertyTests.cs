// Feature: optional-types, Property 6: Optional<T>, T?, and T|void structural equivalence
// Feature: optional-types, Property 7: No member access on Optional<T> alias
// **Validates: Requirements 2.2, 2.3, 2.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 6: a type name for which Optional&lt;T&gt;, T?, and T|void
/// should be structurally equivalent (cross-assignment produces no errors).
/// </summary>
public sealed class OptionalEquivalenceScenario
{
    /// <summary>The Mist type name to use (e.g., "int", "string", "bool").</summary>
    public string TypeName { get; }

    public OptionalEquivalenceScenario(string typeName)
    {
        TypeName = typeName;
    }

    public override string ToString()
    {
        return $"Optional<{TypeName}> ≡ {TypeName}? ≡ {TypeName} | void";
    }
}

/// <summary>
/// Scenario for Property 7: a type name for which accessing .HasValue or .Value
/// on Optional&lt;T&gt; should produce a diagnostic error.
/// </summary>
public sealed class OptionalMemberRejectionScenario
{
    /// <summary>The Mist type name to use.</summary>
    public string TypeName { get; }

    /// <summary>The member name being accessed (HasValue or Value).</summary>
    public string MemberName { get; }

    public OptionalMemberRejectionScenario(string typeName, string memberName)
    {
        TypeName = typeName;
        MemberName = memberName;
    }

    public override string ToString()
    {
        return $"Optional<{TypeName}>.{MemberName} → error";
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for Optional&lt;T&gt; type alias equivalence and member rejection.
///
/// Property 6: For any valid type T, a program that declares variables of type
/// Optional&lt;T&gt;, T?, and T | void and cross-assigns between them SHALL produce
/// no type-checking errors.
///
/// Property 7: For any valid type T, attempting to access .HasValue or .Value on
/// a variable of type Optional&lt;T&gt; SHALL produce a diagnostic error indicating
/// the member does not exist.
///
/// **Validates: Requirements 2.2, 2.3, 2.4**
/// </summary>
public sealed class OptionalTypeAliasPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Primitive types that can be used with Optional&lt;T&gt;.
    /// These are types that exist without needing additional class declarations.
    /// </summary>
    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double"];

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios for Property 6: a primitive type name for cross-assignment testing.
    /// </summary>
    public static Arbitrary<OptionalEquivalenceScenario> ArbitraryOptionalEquivalenceScenario()
    {
        var gen = Gen.Elements(PrimitiveTypes)
            .Select(t => new OptionalEquivalenceScenario(t));
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios for Property 7: a primitive type name and a member name
    /// (HasValue or Value) for member rejection testing.
    /// </summary>
    public static Arbitrary<OptionalMemberRejectionScenario> ArbitraryOptionalMemberRejectionScenario()
    {
        var gen =
            from typeName in Gen.Elements(PrimitiveTypes)
            from memberName in Gen.Elements("HasValue", "Value")
            select new OptionalMemberRejectionScenario(typeName, memberName);
        return gen.ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_opt_alias_pbt_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
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
                EmitToDisk: false,
                GlobalUsings: ImmutableArray.Create("Standard"));

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static bool HasAnyError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    // ─── Property 6: Structural equivalence ─────────────────────────────

    /// <summary>
    /// Property 6: For any valid type T, cross-assignment between Optional&lt;T&gt;,
    /// T?, and T | void produces no type-checking errors.
    ///
    /// The test declares variables of all three forms and assigns between them
    /// in every direction to verify structural equivalence.
    ///
    /// **Validates: Requirements 2.2, 2.3**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(OptionalTypeAliasPropertyTests) })]
    public Property OptionalT_QuestionMark_And_UnionVoid_AreStructurallyEquivalent(
        OptionalEquivalenceScenario scenario)
    {
        var source = $$"""
            using Standard;

            class Test
            {
                public static void Main()
                {
                    Optional<{{scenario.TypeName}}> a = default;
                    {{scenario.TypeName}}? b = default;
                    {{scenario.TypeName}} | void c = default;

                    // Cross-assign: Optional<T> → T?
                    b = a;
                    // Cross-assign: Optional<T> → T | void
                    c = a;
                    // Cross-assign: T? → Optional<T>
                    a = b;
                    // Cross-assign: T? → T | void
                    c = b;
                    // Cross-assign: T | void → Optional<T>
                    a = c;
                    // Cross-assign: T | void → T?
                    b = c;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        return (!errors.Any()).Label(
            errors.Any()
                ? $"Unexpected errors for {scenario.TypeName}: {string.Join("; ", errors.Select(e => $"{e.Id.Value}: {e.Message}"))}"
                : "No errors");
    }

    // ─── Property 7: Member rejection ───────────────────────────────────

    /// <summary>
    /// Property 7: For any valid type T, accessing .HasValue or .Value on a variable
    /// of type Optional&lt;T&gt; produces a diagnostic error (member not found on union type).
    ///
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(OptionalTypeAliasPropertyTests) })]
    public bool OptionalT_MemberAccess_ProducesDiagnosticError(
        OptionalMemberRejectionScenario scenario)
    {
        var source = $$"""
            using Standard;

            class Test
            {
                public void M()
                {
                    Optional<{{scenario.TypeName}}> x = default;
                    var result = x.{{scenario.MemberName}};
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // An error should be produced — .HasValue and .Value do not exist on the union type.
        return HasAnyError(diagnostics);
    }
}