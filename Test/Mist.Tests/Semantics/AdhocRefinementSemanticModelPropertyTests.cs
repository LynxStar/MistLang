// Feature: adhoc-refinement-constraints, Property 2: Semantic model predicate attachment and evaluation
// **Validates: Requirements 4.1, 4.2, 4.3, 9.6**

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
/// Property-based tests for semantic model predicate attachment and evaluation on adhoc refinement constraints.
///
/// Property 2: For any declaration with an adhoc refinement constraint referencing a valid dependent
/// type parameter or value parameter, the semantic model SHALL attach a RefinementPredicate to that
/// entity such that predicate.Evaluate(value) returns true for values satisfying the constraint and
/// false for values violating it.
/// </summary>
public class AdhocRefinementSemanticModelPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface scenario records

    /// <summary>
    /// Represents a scenario where a dependent type parameter has an adhoc refinement constraint.
    /// </summary>
    public sealed class DependentTypeParamScenario
    {
        public string ClassName { get; }
        public string DependentParamName { get; }
        public string Operator { get; }
        public int ConstraintValue { get; }

        public DependentTypeParamScenario(string className, string dependentParamName, string op, int constraintValue)
        {
            ClassName = className;
            DependentParamName = dependentParamName;
            Operator = op;
            ConstraintValue = constraintValue;
        }

        public string ToSource() =>
            $"class {ClassName}<{DependentParamName}:int> where {DependentParamName} {Operator} {ConstraintValue} {{ }}";

        /// <summary>
        /// Returns a value that should satisfy the constraint.
        /// </summary>
        public int SatisfyingValue() => Operator switch
        {
            ">=" => ConstraintValue,
            "<=" => ConstraintValue,
            ">" => ConstraintValue + 1,
            "<" => ConstraintValue - 1,
            _ => ConstraintValue
        };

        /// <summary>
        /// Returns a value that should violate the constraint.
        /// </summary>
        public int ViolatingValue() => Operator switch
        {
            ">=" => ConstraintValue - 1,
            "<=" => ConstraintValue + 1,
            ">" => ConstraintValue,
            "<" => ConstraintValue,
            _ => ConstraintValue + 1
        };

        public override string ToString() => ToSource();
    }

    /// <summary>
    /// Represents a scenario where a method parameter has an adhoc refinement constraint.
    /// </summary>
    public sealed class ParameterRefinementScenario
    {
        public string ClassName { get; }
        public string MethodName { get; }
        public string ParamName { get; }
        public string Operator { get; }
        public int ConstraintValue { get; }

        public ParameterRefinementScenario(string className, string methodName, string paramName, string op, int constraintValue)
        {
            ClassName = className;
            MethodName = methodName;
            ParamName = paramName;
            Operator = op;
            ConstraintValue = constraintValue;
        }

        public string ToSource() =>
            $"class {ClassName} {{ void {MethodName}(int {ParamName}) where {ParamName} {Operator} {ConstraintValue} {{ }} }}";

        /// <summary>
        /// Returns a value that should satisfy the constraint.
        /// </summary>
        public int SatisfyingValue() => Operator switch
        {
            ">=" => ConstraintValue,
            "<=" => ConstraintValue,
            ">" => ConstraintValue + 1,
            "<" => ConstraintValue - 1,
            _ => ConstraintValue
        };

        /// <summary>
        /// Returns a value that should violate the constraint.
        /// </summary>
        public int ViolatingValue() => Operator switch
        {
            ">=" => ConstraintValue - 1,
            "<=" => ConstraintValue + 1,
            ">" => ConstraintValue,
            "<" => ConstraintValue,
            _ => ConstraintValue + 1
        };

        public override string ToString() => ToSource();
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage", "Cache", "Pool"];

    private static readonly string[] DependentParamNames =
        ["Size", "Count", "Capacity", "Length", "Width", "Height", "Depth", "Limit"];

    private static readonly string[] MethodNames =
        ["Process", "Validate", "Compute", "Transform", "Execute", "Handle", "Apply", "Check"];

    private static readonly string[] ParamNames =
        ["value", "index", "count", "offset", "limit", "threshold", "amount", "level"];

    private static readonly string[] Operators = [">=", "<=", ">", "<"];

    private static Gen<DependentTypeParamScenario> GenDependentTypeParamScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from depParam in Gen.Elements(DependentParamNames)
               from op in Gen.Elements(Operators)
               from value in Gen.Choose(1, 500)
               select new DependentTypeParamScenario(className, depParam, op, value);
    }

    private static Gen<ParameterRefinementScenario> GenParameterRefinementScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               from op in Gen.Elements(Operators)
               from value in Gen.Choose(1, 500)
               select new ParameterRefinementScenario(className, methodName, paramName, op, value);
    }

    public static Arbitrary<DependentTypeParamScenario> ArbitraryDependentTypeParamScenario() =>
        GenDependentTypeParamScenario().ToArbitrary();

    public static Arbitrary<ParameterRefinementScenario> ArbitraryParameterRefinementScenario() =>
        GenParameterRefinementScenario().ToArbitrary();

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
    /// Property 2a: When a type declaration includes an adhoc refinement constraint on a
    /// dependent type parameter, the semantic model SHALL attach the refinement predicate
    /// to the DependentTypeParameterType such that Evaluate returns true for satisfying
    /// values and false for violating values.
    ///
    /// **Validates: Requirements 4.1, 4.3, 9.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdhocRefinementSemanticModelPropertyTests) })]
    public bool DependentTypeParam_PredicateAttached_AndEvaluatesCorrectly(DependentTypeParamScenario scenario)
    {
        var source = scenario.ToSource();
        var (model, diagnostics) = BuildModelFromSource(source);
        if (model is null) return false;

        // No refinement-related diagnostics should be emitted for valid constraints.
        var refinementErrors = diagnostics
            .Where(d => d.Id.Value.StartsWith("MIST_REFINEMENT"))
            .ToList();
        if (refinementErrors.Count > 0) return false;

        // Find the class symbol.
        var classSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ClassName && s.Kind == SymbolKind.Class);
        if (classSymbol is null) return false;

        var classType = classSymbol.DeclaredType as ClassType;
        if (classType is null) return false;

        // Find the dependent type parameter.
        var depParam = classType.TypeParameters
            .OfType<DependentTypeParameterType>()
            .FirstOrDefault(p => p.Name == scenario.DependentParamName);
        if (depParam is null) return false;

        // The AdhocConstraint must be attached.
        if (depParam.AdhocConstraint is null) return false;

        // Evaluate: satisfying value should return true.
        if (!depParam.AdhocConstraint.Evaluate(scenario.SatisfyingValue())) return false;

        // Evaluate: violating value should return false.
        if (depParam.AdhocConstraint.Evaluate(scenario.ViolatingValue())) return false;

        return true;
    }

    /// <summary>
    /// Property 2b: When a method declaration includes an adhoc refinement constraint on a
    /// parameter, the semantic model SHALL attach the refinement predicate to the parameter
    /// symbol such that GetParameterRefinement returns the predicate and Evaluate returns
    /// true for satisfying values and false for violating values.
    ///
    /// **Validates: Requirements 4.2, 4.3, 9.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdhocRefinementSemanticModelPropertyTests) })]
    public bool ParameterRefinement_PredicateAttached_AndEvaluatesCorrectly(ParameterRefinementScenario scenario)
    {
        var source = scenario.ToSource();
        var (model, diagnostics) = BuildModelFromSource(source);
        if (model is null) return false;

        // No refinement-related diagnostics should be emitted for valid constraints.
        var refinementErrors = diagnostics
            .Where(d => d.Id.Value.StartsWith("MIST_REFINEMENT"))
            .ToList();
        if (refinementErrors.Count > 0) return false;

        // Find the parameter symbol.
        var paramSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ParamName && s.Kind == SymbolKind.Parameter);
        if (paramSymbol is null) return false;

        // GetParameterRefinement must return a predicate.
        var predicate = model.GetParameterRefinement(paramSymbol);
        if (predicate is null) return false;

        // Evaluate: satisfying value should return true.
        if (!predicate.Evaluate(scenario.SatisfyingValue())) return false;

        // Evaluate: violating value should return false.
        if (predicate.Evaluate(scenario.ViolatingValue())) return false;

        return true;
    }
}
