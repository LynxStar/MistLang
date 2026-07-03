// Feature: adhoc-refinement-constraints, Property 8: Predicate-type compatibility validation
// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7**

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
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for predicate-type compatibility validation.
///
/// Property 8: For any adhoc refinement constraint, the type checker SHALL accept the
/// predicate if and only if the predicate kind is compatible with the constrained type:
/// relational predicates require a comparable type (integers, char, float, double, decimal)
/// and SHALL be rejected on bool and string; constant patterns and and combinators are
/// valid on all equality-comparable types.
///
/// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7**
/// </summary>
public class PredicateCompatibilityPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
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
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// The kind of predicate applied in the where clause.
    /// </summary>
    public enum PredicateKind
    {
        /// <summary>Relational predicate (e.g., >= 0, &lt; 100).</summary>
        Relational,

        /// <summary>Constant pattern (e.g., == true, == 42).</summary>
        Constant
    }

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface scenario records

    /// <summary>
    /// Represents a type that can be used as a parameter type in a method with
    /// an adhoc refinement constraint.
    /// </summary>
    public sealed class MistType
    {
        public string Name { get; }
        public bool IsComparable { get; }
        public string SampleConstant { get; }

        public MistType(string name, bool isComparable, string sampleConstant)
        {
            Name = name;
            IsComparable = isComparable;
            SampleConstant = sampleConstant;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Represents a scenario: a method parameter of a given type with a specific
    /// predicate kind applied via a where clause.
    /// </summary>
    public sealed class CompatibilityScenario
    {
        public MistType Type { get; }
        public PredicateKind Predicate { get; }
        public string Operator { get; }

        public CompatibilityScenario(MistType type, PredicateKind predicate, string op)
        {
            Type = type;
            Predicate = predicate;
            Operator = op;
        }

        /// <summary>
        /// Whether this combination should be accepted by the type checker.
        /// Relational predicates require a comparable type; constant patterns are
        /// valid on all equality-comparable types.
        /// </summary>
        public bool ShouldAccept => Predicate switch
        {
            PredicateKind.Relational => Type.IsComparable,
            PredicateKind.Constant => true, // constant patterns valid on all types
            _ => false
        };

        /// <summary>
        /// Generates the where clause predicate text.
        /// </summary>
        public string PredicateText => Predicate switch
        {
            PredicateKind.Relational => $"{Operator} {Type.SampleConstant}",
            PredicateKind.Constant => Type.SampleConstant,
            _ => ""
        };

        public string ToSource()
        {
            return $$"""
                class Program {
                    void Process({{Type.Name}} value) where value {{PredicateText}} { }
                    public static void Main() { }
                }
                """;
        }

        public override string ToString()
        {
            return $"({Type.Name}, {Predicate}, {Operator}) → expect {(ShouldAccept ? "accept" : "reject")}";
        }
    }

#pragma warning restore CA1034

    // ─── Type definitions ────────────────────────────────────────────────

    /// <summary>
    /// Comparable types: relational predicates are valid on these.
    /// </summary>
    private static readonly MistType[] ComparableTypes =
    [
        new("int", true, "0"),
        new("uint", true, "0"),
        new("long", true, "0"),
        new("ulong", true, "0"),
        new("short", true, "0"),
        new("ushort", true, "0"),
        new("byte", true, "0"),
        new("sbyte", true, "0"),
        new("char", true, "'a'"),
        new("float", true, "0.0"),
        new("double", true, "0.0"),
        new("decimal", true, "0.0"),
    ];

    /// <summary>
    /// Non-comparable types: relational predicates are NOT valid on these.
    /// </summary>
    private static readonly MistType[] NonComparableTypes =
    [
        new("bool", false, "true"),
        new("string", false, "\"hello\""),
    ];

    private static readonly MistType[] AllTypes = [.. ComparableTypes, .. NonComparableTypes];

    private static readonly string[] RelationalOperators = [">=", "<=", ">", "<"];

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<CompatibilityScenario> GenRelationalOnComparable()
    {
        return from type in Gen.Elements(ComparableTypes)
               from op in Gen.Elements(RelationalOperators)
               select new CompatibilityScenario(type, PredicateKind.Relational, op);
    }

    private static Gen<CompatibilityScenario> GenRelationalOnNonComparable()
    {
        return from type in Gen.Elements(NonComparableTypes)
               from op in Gen.Elements(RelationalOperators)
               select new CompatibilityScenario(type, PredicateKind.Relational, op);
    }

    private static Gen<CompatibilityScenario> GenConstantOnAnyType()
    {
        return from type in Gen.Elements(AllTypes)
               select new CompatibilityScenario(type, PredicateKind.Constant, "==");
    }

    private static Gen<CompatibilityScenario> GenCompatibilityScenario()
    {
        // Mix of all three categories to cover the full space.
        return Gen.OneOf(
            GenRelationalOnComparable(),
            GenRelationalOnNonComparable(),
            GenConstantOnAnyType());
    }

    public static Arbitrary<CompatibilityScenario> ArbitraryCompatibilityScenario()
    {
        return GenCompatibilityScenario().ToArbitrary();
    }

    // ─── Property Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 8: Predicate-type compatibility validation.
    ///
    /// For any predicate+type combination:
    /// - Relational predicates on comparable types (int, uint, long, ulong, short, ushort,
    ///   byte, sbyte, char, float, double, decimal) SHALL be accepted.
    /// - Relational predicates on non-comparable types (bool, string) SHALL be rejected
    ///   with MIST_REFINEMENT_0007.
    /// - Constant patterns on all equality-comparable types SHALL be accepted.
    ///
    /// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PredicateCompatibilityPropertyTests) })]
    public bool PredicateAccepted_IffCompatibleWithType(CompatibilityScenario scenario)
    {
        var source = scenario.ToSource();
        var diagnostics = CompileAndGetDiagnostics(source);

        var incompatibleDiagnostics = diagnostics
            .Where(d => d.Id == TypeCheckDiagnostics.RefinementPredicateIncompatible.Id)
            .ToList();

        if(scenario.ShouldAccept)
        {
            // Compatible combination: no MIST_REFINEMENT_0007 diagnostic expected.
            return incompatibleDiagnostics.Count == 0;
        }
        else
        {
            // Incompatible combination: exactly one MIST_REFINEMENT_0007 diagnostic expected.
            return incompatibleDiagnostics.Count == 1;
        }
    }
}