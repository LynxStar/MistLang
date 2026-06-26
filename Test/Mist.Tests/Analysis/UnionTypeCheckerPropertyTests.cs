// Feature: type-unions, Property 7: Constituent Assignability
// Feature: type-unions, Property 8: Non-Constituent Rejection
// Feature: type-unions, Property 9: Common Member Access
// Feature: type-unions, Property 10: Missing Member Rejection
// Feature: type-unions, Property 11: Pattern Matching Narrows to Constituent Type
// Feature: type-unions, Property 12: Non-Constituent Pattern Warning
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 7.1, 7.2, 7.3, 8.1, 8.2, 8.3, 12.2, 12.3, 12.4, 12.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for the type checker's union type handling.
/// Properties 7–12 cover assignability, member access, and pattern matching.
/// </summary>
public class UnionTypeCheckerPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Shared infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Primitive types available for union construction.
    /// These are the legacy primitive singletons that exist without stdlib.
    /// </summary>
    private static readonly IType[] PrimitivePool =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.String,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.Double,
    ];



    /// <summary>
    /// Compiles Mist source and returns the semantic model and type checker diagnostics.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) Compile(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return (null!, ImmutableArray<Diagnostic>.Empty);
        }

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        return (model, tcDiag.ToImmutableArray());
    }

    private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Any(d => d.Id.Value == id);
    }

    private static bool HasNoDiagnosticWithId(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return !diagnostics.Any(d => d.Id.Value == id);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Constituent Assignability
    //
    // For any type T that is a constituent of union U, assignment from T to U
    // is valid (no diagnostic). For any union S whose every constituent is
    // also a constituent of U, assignment from S to U is valid.
    //
    // **Validates: Requirements 6.1, 6.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario for Property 7: a source type and a target union where the
    /// source is assignable to the target.
    /// </summary>
#pragma warning disable CA1034
    public sealed record ConstituentAssignabilityScenario(
        string Label,
        IType Source,
        UnionType Target)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates scenarios where a single constituent type is assigned to a union
    /// that contains it, or a sub-union is assigned to a wider union.
    /// </summary>
    public static Arbitrary<ConstituentAssignabilityScenario> ArbitraryConstituentAssignability()
    {
        var singleConstituent =
            from count in Gen.Choose(2, 4)
            from types in Gen.Shuffle(PrimitivePool).Select(t => t.Take(count).ToArray())
            from pickIdx in Gen.Choose(0, count - 1)
            let union = new UnionType(types)
            let source = types[pickIdx]
            select new ConstituentAssignabilityScenario(
                $"{source.DisplayName} → {union.DisplayName}",
                source,
                union);

        var subUnionToWiderUnion =
            from count in Gen.Choose(3, 5)
            from types in Gen.Shuffle(PrimitivePool).Select(t => t.Take(count).ToArray())
            from subCount in Gen.Choose(2, count - 1)
            let target = new UnionType(types)
            let sourceTypes = types.Take(subCount).ToArray()
            let source = new UnionType(sourceTypes)
            select new ConstituentAssignabilityScenario(
                $"{source.DisplayName} → {target.DisplayName}",
                source,
                target);

        var gen = Gen.Frequency(
            (3, singleConstituent),
            (2, subUnionToWiderUnion));

        return gen.ToArbitrary();
    }

    // Feature: type-unions, Property 7: Constituent Assignability
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionTypeCheckerPropertyTests) })]
    public bool ConstituentType_IsAssignableToUnion(ConstituentAssignabilityScenario scenario)
    {
        return TypeCheckerAnalyzer.IsAssignable(scenario.Source, scenario.Target);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Non-Constituent Rejection
    //
    // For any type T that is NOT a constituent of union U, assigning T to U
    // produces a rejection. For any union U assigned to a non-union type T,
    // a rejection is produced.
    //
    // **Validates: Requirements 6.3, 6.4, 12.3, 12.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario for Property 8: a source type and a target where the assignment
    /// should be rejected.
    /// </summary>
#pragma warning disable CA1034
    public sealed record NonConstituentRejectionScenario(
        string Label,
        IType Source,
        IType Target)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates scenarios where:
    /// (a) A non-constituent type is assigned to a union (rejected).
    /// (b) A union type is assigned to a non-union type (rejected).
    /// </summary>
    public static Arbitrary<NonConstituentRejectionScenario> ArbitraryNonConstituentRejection()
    {
        // Non-constituent → union: pick a type NOT in the union.
        var nonConstituentToUnion =
            from count in Gen.Choose(2, 3)
            from types in Gen.Shuffle(PrimitivePool).Select(t => t.Take(count).ToArray())
            let remaining = PrimitivePool.Where(p => !types.Contains(p)).ToArray()
            where remaining.Length > 0
            from nonConstituent in Gen.Elements(remaining)
            let union = new UnionType(types)
            select new NonConstituentRejectionScenario(
                $"{nonConstituent.DisplayName} → {union.DisplayName}",
                nonConstituent,
                union);

        // Union → non-union: assign a union to a single type (rejected).
        var unionToNonUnion =
            from count in Gen.Choose(2, 4)
            from types in Gen.Shuffle(PrimitivePool).Select(t => t.Take(count).ToArray())
            from targetIdx in Gen.Choose(0, PrimitivePool.Length - 1)
            let union = new UnionType(types)
            let target = PrimitivePool[targetIdx]
            select new NonConstituentRejectionScenario(
                $"{union.DisplayName} → {target.DisplayName}",
                union,
                target);

        var gen = Gen.Frequency(
            (3, nonConstituentToUnion),
            (2, unionToNonUnion));

        return gen.ToArbitrary();
    }

    // Feature: type-unions, Property 8: Non-Constituent Rejection
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionTypeCheckerPropertyTests) })]
    public bool NonConstituentType_IsNotAssignableToUnion(NonConstituentRejectionScenario scenario)
    {
        return !TypeCheckerAnalyzer.IsAssignable(scenario.Source, scenario.Target);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Common Member Access
    //
    // For any union U and member M that exists on ALL constituents, accessing
    // M is valid (no MIST_TC_0073 diagnostic).
    //
    // **Validates: Requirements 7.1, 7.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario for Property 9: classes with a common member form a union,
    /// and accessing that member should produce no diagnostic.
    /// </summary>
#pragma warning disable CA1034
    public sealed record CommonMemberScenario(
        string Label,
        string Source)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    private static readonly string[] MemberNames = ["GetValue", "Compute", "Name", "Size"];

    public static Arbitrary<CommonMemberScenario> ArbitraryCommonMemberScenario()
    {
        // Generate 2-3 classes that all share a common method, then form a union.
        var gen =
            from classCount in Gen.Choose(2, 3)
            from memberName in Gen.Elements(MemberNames)
            let classNames = Enumerable.Range(0, classCount).Select(i => $"C{i}").ToArray()
            let classDecls = string.Join("\n", classNames.Select(cn =>
                $"class {cn} {{ public int {memberName}() {{ return 0; }} }}"))
            let unionType = string.Join(" | ", classNames)
            let source = $@"{classDecls}
class Program {{
    public static void Main() {{
        {unionType} x = new C0();
        int r = x.{memberName}();
    }}
}}"
            select new CommonMemberScenario(
                $"Union({string.Join(", ", classNames)}).{memberName}",
                source);

        return gen.ToArbitrary();
    }

    // Feature: type-unions, Property 9: Common Member Access
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionTypeCheckerPropertyTests) })]
    public bool CommonMember_OnUnion_ProducesNoDiagnostic(CommonMemberScenario scenario)
    {
        var (_, diagnostics) = Compile(scenario.Source);
        return HasNoDiagnosticWithId(diagnostics, "MIST_TC_0073");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Missing Member Rejection
    //
    // For any union U and member M that does NOT exist on all constituents,
    // accessing M produces a MIST_TC_0073 diagnostic.
    //
    // **Validates: Requirements 7.2, 12.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario for Property 10: classes where at least one lacks a member,
    /// forming a union where accessing that member should produce a diagnostic.
    /// </summary>
#pragma warning disable CA1034
    public sealed record MissingMemberScenario(
        string Label,
        string Source)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    public static Arbitrary<MissingMemberScenario> ArbitraryMissingMemberScenario()
    {
        // Generate 2-3 classes where at least one does NOT have the target member.
        var gen =
            from classCount in Gen.Choose(2, 3)
            from memberName in Gen.Elements(MemberNames)
            from missingIdx in Gen.Choose(0, classCount - 1)
            let classNames = Enumerable.Range(0, classCount).Select(i => $"D{i}").ToArray()
            let classDecls = string.Join("\n", classNames.Select((cn, idx) =>
                idx == missingIdx
                    ? $"class {cn} {{ public int Other() {{ return 0; }} }}"
                    : $"class {cn} {{ public int {memberName}() {{ return 0; }} }}"))
            let unionType = string.Join(" | ", classNames)
            let source = $@"{classDecls}
class Program {{
    public static void Main() {{
        {unionType} x = new D0();
        int r = x.{memberName}();
    }}
}}"
            select new MissingMemberScenario(
                $"Union({string.Join(", ", classNames)}).{memberName} (missing on {classNames[missingIdx]})",
                source);

        return gen.ToArbitrary();
    }

    // Feature: type-unions, Property 10: Missing Member Rejection
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionTypeCheckerPropertyTests) })]
    public bool MissingMember_OnUnion_ProducesDiagnostic(MissingMemberScenario scenario)
    {
        var (_, diagnostics) = Compile(scenario.Source);
        return HasDiagnostic(diagnostics, "MIST_TC_0073");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Pattern Matching Narrows to Constituent Type
    //
    // For any union U with constituent T, `is T t` introduces t with type T
    // (not U), and no MIST_TC_0074 warning is emitted.
    //
    // **Validates: Requirements 8.1, 8.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario for Property 11: a union type and a constituent used in a
    /// type pattern. The pattern variable should have the constituent type.
    /// </summary>
#pragma warning disable CA1034
    public sealed record PatternNarrowsScenario(
        string Label,
        string Source,
        string ConstituentClassName,
        string PatternVarName)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    public static Arbitrary<PatternNarrowsScenario> ArbitraryPatternNarrowsScenario()
    {
        var gen =
            from classCount in Gen.Choose(2, 3)
            from pickIdx in Gen.Choose(0, classCount - 1)
            let classNames = Enumerable.Range(0, classCount).Select(i => $"E{i}").ToArray()
            let classDecls = string.Join("\n", classNames.Select(cn =>
                $"class {cn} {{ }}"))
            let unionType = string.Join(" | ", classNames)
            let constituent = classNames[pickIdx]
            let varName = "narrowed"
            let source = $@"{classDecls}
class Program {{
    public static void Main() {{
        {unionType} x = new E0();
        if (x is {constituent} {varName}) {{
            {constituent} y = {varName};
        }}
    }}
}}"
            select new PatternNarrowsScenario(
                $"is {constituent} {varName} on {unionType}",
                source,
                constituent,
                varName);

        return gen.ToArbitrary();
    }

    // Feature: type-unions, Property 11: Pattern Matching Narrows to Constituent Type
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionTypeCheckerPropertyTests) })]
    public bool PatternMatch_NarrowsToConstituentType(PatternNarrowsScenario scenario)
    {
        var (_, diagnostics) = Compile(scenario.Source);

        // No MIST_TC_0074 warning (constituent IS a valid pattern type).
        if(HasDiagnostic(diagnostics, "MIST_TC_0074"))
        {
            return false;
        }

        // The pattern variable should have the constituent type, not the union.
        // Verify by checking that assigning the pattern variable to a variable
        // of the constituent type produces no type mismatch error.
        if(HasDiagnostic(diagnostics, "MIST_TC_0001"))
        {
            return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Non-Constituent Pattern Warning
    //
    // For any union U and type T not a constituent, `is T t` produces a
    // MIST_TC_0074 warning.
    //
    // **Validates: Requirements 8.3, 12.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario for Property 12: a union type and a non-constituent type used
    /// in a type pattern. A warning should be emitted.
    /// </summary>
#pragma warning disable CA1034
    public sealed record NonConstituentPatternScenario(
        string Label,
        string Source)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    public static Arbitrary<NonConstituentPatternScenario> ArbitraryNonConstituentPatternScenario()
    {
        // Generate 2-3 classes forming a union, plus an extra class NOT in the union.
        var gen =
            from classCount in Gen.Choose(2, 3)
            let allClassNames = Enumerable.Range(0, classCount + 1).Select(i => $"F{i}").ToArray()
            let unionClassNames = allClassNames.Take(classCount).ToArray()
            let nonConstituent = allClassNames[classCount]
            let classDecls = string.Join("\n", allClassNames.Select(cn =>
                $"class {cn} {{ }}"))
            let unionType = string.Join(" | ", unionClassNames)
            let source = $@"{classDecls}
class Program {{
    public static void Main() {{
        {unionType} x = new F0();
        if (x is {nonConstituent} narrowed) {{
        }}
    }}
}}"
            select new NonConstituentPatternScenario(
                $"is {nonConstituent} on {unionType}",
                source);

        return gen.ToArbitrary();
    }

    // Feature: type-unions, Property 12: Non-Constituent Pattern Warning
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionTypeCheckerPropertyTests) })]
    public bool NonConstituentPattern_ProducesWarning(NonConstituentPatternScenario scenario)
    {
        var (_, diagnostics) = Compile(scenario.Source);
        return HasDiagnostic(diagnostics, "MIST_TC_0074");
    }
}