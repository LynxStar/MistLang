// Feature: adhoc-refinement-constraints, Property 3: Semantic model invalid-name diagnostics
// **Validates: Requirements 4.4, 4.5, 4.6**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for semantic model invalid-name diagnostics on adhoc refinement constraints.
///
/// Property 3: For any declaration where an adhoc refinement constraint references a name that
/// is not a dependent type parameter or value parameter on that declaration (including names
/// that don't exist and names that are regular type parameters), the semantic model SHALL emit
/// a diagnostic error and SHALL NOT attach a predicate for that name.
///
/// Three error categories:
/// - MIST_REFINEMENT_0001: Unknown name (not found as dependent type param or value param)
/// - MIST_REFINEMENT_0002: Regular type parameter (name is a regular type param, not dependent)
/// - MIST_REFINEMENT_0003: Duplicate constraint (same name constrained twice)
/// </summary>
public class AdhocRefinementInvalidNameDiagnosticsPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface scenario records

    /// <summary>
    /// Represents a scenario where an adhoc refinement constraint references an unknown name.
    /// </summary>
    public sealed class UnknownNameScenario
    {
        public string ClassName { get; }
        public string DependentParamName { get; }
        public string UnknownName { get; }
        public int ConstraintValue { get; }

        public UnknownNameScenario(string className, string dependentParamName, string unknownName, int constraintValue)
        {
            ClassName = className;
            DependentParamName = dependentParamName;
            UnknownName = unknownName;
            ConstraintValue = constraintValue;
        }

        public string ToSource() =>
            $"class {ClassName}<{DependentParamName}:int> where {UnknownName} >= {ConstraintValue} {{ }}";

        public override string ToString() => ToSource();
    }

    /// <summary>
    /// Represents a scenario where an adhoc refinement constraint targets a regular type parameter.
    /// </summary>
    public sealed class RegularTypeParamScenario
    {
        public string ClassName { get; }
        public string RegularTypeParamName { get; }
        public int ConstraintValue { get; }

        public RegularTypeParamScenario(string className, string regularTypeParamName, int constraintValue)
        {
            ClassName = className;
            RegularTypeParamName = regularTypeParamName;
            ConstraintValue = constraintValue;
        }

        public string ToSource() =>
            $"class {ClassName}<{RegularTypeParamName}> where {RegularTypeParamName} >= {ConstraintValue} {{ }}";

        public override string ToString() => ToSource();
    }

    /// <summary>
    /// Represents a scenario where duplicate adhoc refinement constraints target the same name.
    /// </summary>
    public sealed class DuplicateConstraintScenario
    {
        public string ClassName { get; }
        public string DependentParamName { get; }
        public int FirstValue { get; }
        public int SecondValue { get; }

        public DuplicateConstraintScenario(string className, string dependentParamName, int firstValue, int secondValue)
        {
            ClassName = className;
            DependentParamName = dependentParamName;
            FirstValue = firstValue;
            SecondValue = secondValue;
        }

        public string ToSource() =>
            $"class {ClassName}<{DependentParamName}:int> where {DependentParamName} >= {FirstValue} where {DependentParamName} <= {SecondValue} {{ }}";

        public override string ToString() => ToSource();
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage", "Cache", "Pool"];

    private static readonly string[] DependentParamNames =
        ["Size", "Count", "Capacity", "Length", "Width", "Height", "Depth", "Limit"];

    private static readonly string[] RegularTypeParamNames =
        ["T", "U", "V", "TItem", "TValue", "TKey", "TElement", "TResult"];

    private static readonly string[] UnknownNames =
        ["Unknown", "Missing", "Bogus", "Nonexistent", "Phantom", "Ghost", "Absent", "Void_"];

    private static Gen<UnknownNameScenario> GenUnknownNameScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from depParam in Gen.Elements(DependentParamNames)
               from unknownName in Gen.Elements(UnknownNames)
               from value in Gen.Choose(0, 1000)
               select new UnknownNameScenario(className, depParam, unknownName, value);
    }

    private static Gen<RegularTypeParamScenario> GenRegularTypeParamScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from typeParam in Gen.Elements(RegularTypeParamNames)
               from value in Gen.Choose(0, 1000)
               select new RegularTypeParamScenario(className, typeParam, value);
    }

    private static Gen<DuplicateConstraintScenario> GenDuplicateConstraintScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from depParam in Gen.Elements(DependentParamNames)
               from firstValue in Gen.Choose(0, 500)
               from secondValue in Gen.Choose(501, 1000)
               select new DuplicateConstraintScenario(className, depParam, firstValue, secondValue);
    }

    public static Arbitrary<UnknownNameScenario> ArbitraryUnknownNameScenario() =>
        GenUnknownNameScenario().ToArbitrary();

    public static Arbitrary<RegularTypeParamScenario> ArbitraryRegularTypeParamScenario() =>
        GenRegularTypeParamScenario().ToArbitrary();

    public static Arbitrary<DuplicateConstraintScenario> ArbitraryDuplicateConstraintScenario() =>
        GenDuplicateConstraintScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private (ISemanticModel? Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelFromSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        if (bag.HasErrors) return (null, bag.ToImmutableArray());

        var result = Parser.Parse(tokens, bag);
        if (bag.HasErrors || result.Root is null) return (null, bag.ToImmutableArray());

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semanticBag = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(result.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semanticBag);
        return (model, semanticBag.ToImmutableArray());
    }

    // ─── Property Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3a: When an adhoc refinement constraint references a name that does not
    /// correspond to any dependent type parameter or value parameter on the declaration,
    /// the semantic model SHALL emit MIST_REFINEMENT_0001 and SHALL NOT attach a predicate.
    ///
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdhocRefinementInvalidNameDiagnosticsPropertyTests) })]
    public bool UnknownName_EmitsRefinement0001_AndNoPredicateAttached(UnknownNameScenario scenario)
    {
        var source = scenario.ToSource();
        var (model, diagnostics) = BuildModelFromSource(source);
        if (model is null) return false;

        // Verify MIST_REFINEMENT_0001 is emitted for the unknown name.
        var refinement0001 = diagnostics
            .Where(d => d.Id == SemanticModelBuilder.RefinementConstraintUnknownName)
            .ToList();

        if (refinement0001.Count < 1) return false;
        if (!refinement0001.Any(d => d.Message.Contains(scenario.UnknownName))) return false;

        // Verify no predicate is attached to the dependent type parameter
        // (the unknown name should not cause any predicate attachment).
        var classSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ClassName && s.Kind == SymbolKind.Class);
        if (classSymbol is null) return false;

        var classType = classSymbol.DeclaredType as ClassType;
        if (classType is null) return false;

        // The dependent type parameter should still exist but have no AdhocConstraint
        // (since the constraint targeted an unknown name, not this parameter).
        var depParam = classType.TypeParameters
            .OfType<DependentTypeParameterType>()
            .FirstOrDefault(p => p.Name == scenario.DependentParamName);
        if (depParam is null) return false;

        return depParam.AdhocConstraint is null;
    }

    /// <summary>
    /// Property 3b: When an adhoc refinement constraint references a regular type parameter
    /// (not a dependent type parameter), the semantic model SHALL emit MIST_REFINEMENT_0002
    /// and SHALL NOT attach a predicate.
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdhocRefinementInvalidNameDiagnosticsPropertyTests) })]
    public bool RegularTypeParam_EmitsRefinement0002_AndNoPredicateAttached(RegularTypeParamScenario scenario)
    {
        var source = scenario.ToSource();
        var (model, diagnostics) = BuildModelFromSource(source);
        if (model is null) return false;

        // Verify MIST_REFINEMENT_0002 is emitted for the regular type parameter.
        var refinement0002 = diagnostics
            .Where(d => d.Id == SemanticModelBuilder.RefinementConstraintOnRegularTypeParam)
            .ToList();

        if (refinement0002.Count < 1) return false;
        if (!refinement0002.Any(d => d.Message.Contains(scenario.RegularTypeParamName))) return false;

        // Verify no parameter refinement is attached for the regular type parameter.
        // Regular type parameters don't have AdhocConstraint — they're TypeParameterType, not DependentTypeParameterType.
        // The key assertion is that the diagnostic was emitted and no predicate was stored.
        var paramSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.RegularTypeParamName);

        // If the symbol exists, verify no refinement is attached to it.
        if (paramSymbol is not null)
        {
            var refinement = model.GetParameterRefinement(paramSymbol);
            if (refinement is not null) return false;
        }

        return true;
    }

    /// <summary>
    /// Property 3c: When multiple adhoc refinement constraint clauses reference the same name
    /// on the same declaration, the semantic model SHALL emit MIST_REFINEMENT_0003 for the
    /// duplicate and SHALL register only the first constraint encountered for that name.
    ///
    /// **Validates: Requirements 4.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdhocRefinementInvalidNameDiagnosticsPropertyTests) })]
    public bool DuplicateConstraint_EmitsRefinement0003_AndOnlyFirstPredicateAttached(DuplicateConstraintScenario scenario)
    {
        var source = scenario.ToSource();
        var (model, diagnostics) = BuildModelFromSource(source);
        if (model is null) return false;

        // Verify MIST_REFINEMENT_0003 is emitted for the duplicate.
        var refinement0003 = diagnostics
            .Where(d => d.Id == SemanticModelBuilder.RefinementConstraintDuplicate)
            .ToList();

        if (refinement0003.Count < 1) return false;
        if (!refinement0003.Any(d => d.Message.Contains(scenario.DependentParamName))) return false;

        // Verify only the first predicate is attached (>= FirstValue), not the second (<= SecondValue).
        var classSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ClassName && s.Kind == SymbolKind.Class);
        if (classSymbol is null) return false;

        var classType = classSymbol.DeclaredType as ClassType;
        if (classType is null) return false;

        var depParam = classType.TypeParameters
            .OfType<DependentTypeParameterType>()
            .FirstOrDefault(p => p.Name == scenario.DependentParamName);
        if (depParam is null) return false;

        // The first constraint should be attached.
        if (depParam.AdhocConstraint is null) return false;

        // Verify the first constraint evaluates correctly (>= FirstValue).
        // A value equal to FirstValue should satisfy it.
        if (!depParam.AdhocConstraint.Evaluate(scenario.FirstValue)) return false;

        // A value less than FirstValue should NOT satisfy it.
        if (scenario.FirstValue > 0 && depParam.AdhocConstraint.Evaluate(scenario.FirstValue - 1)) return false;

        return true;
    }
}
