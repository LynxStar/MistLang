// Feature: adhoc-refinement-constraints, Property 1: Pretty-print round-trip
// **Validates: Requirements 1.1, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6,
//              3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 10.1, 10.2, 10.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a generated Mist source fragment with adhoc refinement constraints
/// for round-trip testing.
/// </summary>
public sealed class AdhocRefinementRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public AdhocRefinementRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests for adhoc refinement constraint pretty-print round-trip (idempotence).
///
/// **Validates: Requirements 1.1, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6,
///              3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 10.1, 10.2, 10.5**
///
/// For any valid Mist source text containing adhoc refinement constraints (on type
/// declarations, methods, constructors, and indexers, with single or compound predicates,
/// with or without interleaved generic constraints), parsing the text and then
/// pretty-printing the resulting AST SHALL produce text that, when parsed and
/// pretty-printed again, yields identical output.
/// </summary>
public class AdhocRefinementRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Known names for generation ─────────────────────────────────────────

    private static readonly string[] ClassNames = ["Array", "Matrix", "Buffer", "Container"];
    private static readonly string[] StructNames = ["Cell", "Entry", "Slot", "Wrapper"];
    private static readonly string[] AbstractNames = ["ISized", "IBounded", "ICapacity"];
    private static readonly string[] AliasNames = ["FixedSize", "BoundedInt", "LimitedCount"];
    private static readonly string[] MethodNames = ["Process", "Validate", "Compute", "Transform"];
    private static readonly string[] ParamNames = ["x", "y", "count", "size", "index", "value"];
    private static readonly string[] DependentParamNames = ["Size", "Count", "Capacity", "Length"];
    private static readonly string[] TypeParamNames = ["T", "U", "V"];
    private static readonly string[] PrimitiveTypes = ["int", "byte", "short", "long"];

    // ─── Predicate generators ───────────────────────────────────────────────

    /// <summary>Generates a single relational predicate (e.g., ">= 0", "< 100").</summary>
    private static Gen<string> GenSinglePredicate()
    {
        return from op in Gen.Elements(">=", "<=", ">", "<")
               from value in Gen.Choose(0, 200)
               select $"{op} {value}";
    }

    /// <summary>Generates a compound predicate with 'and' (e.g., ">= 0 and <= 100").</summary>
    private static Gen<string> GenCompoundPredicate()
    {
        return from op1 in Gen.Elements(">=", ">")
               from val1 in Gen.Choose(0, 50)
               from op2 in Gen.Elements("<=", "<")
               from val2 in Gen.Choose(51, 200)
               select $"{op1} {val1} and {op2} {val2}";
    }

    /// <summary>Generates either a single or compound predicate.</summary>
    private static Gen<string> GenPredicate()
    {
        return Gen.OneOf(GenSinglePredicate(), GenCompoundPredicate());
    }

    // ─── Adhoc refinement where clause generators ───────────────────────────

    /// <summary>Generates a single adhoc refinement where clause for a dependent param.</summary>
    private static Gen<string> GenAdhocWhereClause(string name)
    {
        return GenPredicate().Select(pred => $"where {name} {pred}");
    }

    /// <summary>Generates a generic constraint where clause for a type param.</summary>
    private static Gen<string> GenGenericWhereClause(string typeParam)
    {
        return Gen.Elements("class", "struct")
            .Select(constraint => $"where {typeParam} : {constraint}");
    }

    // ─── Declaration generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a class with a single dependent type parameter and a single adhoc refinement.
    /// Example: class Array&lt;TValue, Size:int&gt; where Size >= 0 { }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenClassSingleConstraint()
    {
        return from name in Gen.Elements(ClassNames)
               from depParam in Gen.Elements(DependentParamNames)
               from predicate in GenPredicate()
               let source = $"class {name}<T, {depParam}:int> where {depParam} {predicate} {{\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"class single constraint: {name}");
    }

    /// <summary>
    /// Generates a class with multiple dependent type parameters and multiple adhoc refinements.
    /// Example: class Matrix&lt;Rows:int, Cols:int&gt; where Rows >= 1 where Cols >= 1 { }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenClassMultipleConstraints()
    {
        return from name in Gen.Elements(ClassNames)
               from dep1 in Gen.Constant("Rows")
               from dep2 in Gen.Constant("Cols")
               from pred1 in GenPredicate()
               from pred2 in GenPredicate()
               let source = $"class {name}<{dep1}:int, {dep2}:int> where {dep1} {pred1} where {dep2} {pred2} {{\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"class multiple constraints: {name}");
    }

    /// <summary>
    /// Generates a class with mixed generic + adhoc refinement constraints.
    /// Example: class Container&lt;T, Size:int&gt; where T : class where Size >= 0 { }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenClassMixedConstraints()
    {
        return from name in Gen.Elements(ClassNames)
               from typeParam in Gen.Elements(TypeParamNames)
               from depParam in Gen.Elements(DependentParamNames)
               from genericConstraint in GenGenericWhereClause(typeParam)
               from adhocClause in GenAdhocWhereClause(depParam)
               from order in Gen.Elements(true, false)
               let clauses = order
                   ? $"{genericConstraint} {adhocClause}"
                   : $"{adhocClause} {genericConstraint}"
               let source = $"class {name}<{typeParam}, {depParam}:int> {clauses} {{\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"class mixed constraints: {name}");
    }

    /// <summary>
    /// Generates a struct with adhoc refinement constraints.
    /// Example: struct Cell&lt;Size:int&gt; where Size >= 0 { }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenStructConstraint()
    {
        return from name in Gen.Elements(StructNames)
               from depParam in Gen.Elements(DependentParamNames)
               from predicate in GenPredicate()
               let source = $"struct {name}<{depParam}:int> where {depParam} {predicate} {{\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"struct constraint: {name}");
    }

    /// <summary>
    /// Generates an abstract type with adhoc refinement constraints.
    /// Example: abstract ISized&lt;Size:int&gt; where Size >= 0 { }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenAbstractConstraint()
    {
        return from name in Gen.Elements(AbstractNames)
               from depParam in Gen.Elements(DependentParamNames)
               from predicate in GenPredicate()
               let source = $"abstract {name}<{depParam}:int> where {depParam} {predicate} {{\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"abstract type constraint: {name}");
    }

    /// <summary>
    /// Generates a type alias with adhoc refinement constraints.
    /// Example: type FixedSize&lt;Size:int&gt; = int where Size >= 0;
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenTypeAliasConstraint()
    {
        return from name in Gen.Elements(AliasNames)
               from depParam in Gen.Elements(DependentParamNames)
               from baseType in Gen.Elements(PrimitiveTypes)
               from predicate in GenPredicate()
               let source = $"type {name}<{depParam}:int> = {baseType} where {depParam} {predicate};"
               select new AdhocRefinementRoundTripScenario(source,
                   $"type alias constraint: {name}");
    }

    /// <summary>
    /// Generates a method with a single parameter refinement constraint.
    /// Example: class Foo { void Process(int x) where x >= 0 { } }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenMethodSingleConstraint()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               from paramType in Gen.Elements(PrimitiveTypes)
               from predicate in GenPredicate()
               let source = $"class {className} {{\n    void {methodName}({paramType} {paramName}) where {paramName} {predicate} {{\n    }}\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"method single constraint: {className}.{methodName}");
    }

    /// <summary>
    /// Generates a method with multiple parameter refinement constraints.
    /// Example: class Foo { void Clamp(int min, int max) where min >= 0 where max <= 100 { } }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenMethodMultipleConstraints()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from pred1 in GenPredicate()
               from pred2 in GenPredicate()
               let source = $"class {className} {{\n    void {methodName}(int x, int y) where x {pred1} where y {pred2} {{\n    }}\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"method multiple constraints: {className}.{methodName}");
    }

    /// <summary>
    /// Generates a method with both generic constraint and parameter refinement.
    /// Example: class Foo { void Process&lt;T&gt;(int x) where T : class where x >= 0 { } }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenMethodMixedConstraints()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from typeParam in Gen.Elements(TypeParamNames)
               from paramName in Gen.Elements(ParamNames)
               from paramType in Gen.Elements(PrimitiveTypes)
               from genericConstraint in GenGenericWhereClause(typeParam)
               from adhocClause in GenAdhocWhereClause(paramName)
               from order in Gen.Elements(true, false)
               let clauses = order
                   ? $"{genericConstraint} {adhocClause}"
                   : $"{adhocClause} {genericConstraint}"
               let source = $"class {className} {{\n    void {methodName}<{typeParam}>({paramType} {paramName}) {clauses} {{\n    }}\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"method mixed constraints: {className}.{methodName}");
    }

    /// <summary>
    /// Generates a constructor with a parameter refinement constraint.
    /// Example: class Foo { Foo(int size) where size > 0 { } }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenConstructorConstraint()
    {
        return from className in Gen.Elements(ClassNames)
               from paramName in Gen.Elements(ParamNames)
               from paramType in Gen.Elements(PrimitiveTypes)
               from predicate in GenPredicate()
               let source = $"class {className} {{\n    {className}({paramType} {paramName}) where {paramName} {predicate} {{\n    }}\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"constructor constraint: {className}");
    }

    /// <summary>
    /// Generates an indexer with a parameter refinement constraint.
    /// Example: class Foo { int this[int index] where index >= 0 { get; } }
    /// </summary>
    private static Gen<AdhocRefinementRoundTripScenario> GenIndexerConstraint()
    {
        return from className in Gen.Elements(ClassNames)
               from paramName in Gen.Elements("index", "key", "pos")
               from paramType in Gen.Elements(PrimitiveTypes)
               from returnType in Gen.Elements(PrimitiveTypes)
               from predicate in GenPredicate()
               let source = $"class {className} {{\n    {returnType} this[{paramType} {paramName}] where {paramName} {predicate} {{ get; }}\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"indexer constraint: {className}");
    }

    // ─── Combined generator ─────────────────────────────────────────────────

    private static Gen<AdhocRefinementRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassSingleConstraint(),
            GenClassMultipleConstraints(),
            GenClassMixedConstraints(),
            GenStructConstraint(),
            GenAbstractConstraint(),
            GenTypeAliasConstraint(),
            GenMethodSingleConstraint(),
            GenMethodMultipleConstraints(),
            GenMethodMixedConstraints(),
            GenConstructorConstraint(),
            GenIndexerConstraint());
    }

    // ─── Arbitrary registration ─────────────────────────────────────────────

    public static Arbitrary<AdhocRefinementRoundTripScenario> ArbitraryAdhocRefinementRoundTripScenario() =>
        GenScenario().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Pretty-print round-trip (idempotence)
    //
    // **Validates: Requirements 1.1, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3,
    //              2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 10.1,
    //              10.2, 10.5**
    //
    // For any valid Mist source text containing adhoc refinement constraints,
    // parsing and pretty-printing, then parsing and pretty-printing again,
    // SHALL produce identical output.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdhocRefinementRoundTripPropertyTests) })]
    public Property RoundTrip_AdhocRefinementConstraints_PrintParseIsIdempotent(AdhocRefinementRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}