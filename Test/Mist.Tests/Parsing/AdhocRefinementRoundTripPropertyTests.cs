// Feature: adhoc-refinement-constraints, Property 1: Pretty-print round-trip
// **Validates: Requirements 1.1, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6,
//              3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 10.1, 10.2, 10.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public sealed class AdhocRefinementRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public AdhocRefinementRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public class AdhocRefinementRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static readonly string[] ClassNames = ["Array", "Matrix", "Buffer", "Container"];
    private static readonly string[] StructNames = ["Cell", "Entry", "Slot", "Wrapper"];
    private static readonly string[] AbstractNames = ["ISized", "IBounded", "ICapacity"];
    private static readonly string[] AliasNames = ["FixedSize", "BoundedInt", "LimitedCount"];
    private static readonly string[] MethodNames = ["Process", "Validate", "Compute", "Transform"];
    private static readonly string[] ParamNames = ["x", "y", "count", "size", "index", "value"];
    private static readonly string[] DependentParamNames = ["Size", "Count", "Capacity", "Length"];
    private static readonly string[] TypeParamNames = ["T", "U", "V"];
    private static readonly string[] PrimitiveTypes = ["int", "byte", "short", "long"];

    private static Gen<string> GenSinglePredicate()
    {
        return from op in Gen.Elements(">=", "<=", ">", "<")
               from value in Gen.Choose(0, 200)
               select $"{op} {value}";
    }

    private static Gen<string> GenCompoundPredicate()
    {
        return from op1 in Gen.Elements(">=", ">")
               from val1 in Gen.Choose(0, 50)
               from op2 in Gen.Elements("<=", "<")
               from val2 in Gen.Choose(51, 200)
               select $"{op1} {val1} and {op2} {val2}";
    }

    private static Gen<string> GenPredicate()
    {
        return Gen.OneOf(GenSinglePredicate(), GenCompoundPredicate());
    }

    private static Gen<string> GenAdhocWhereClause(string name)
    {
        return GenPredicate().Select(pred => $"where {name} {pred}");
    }

    private static Gen<string> GenGenericWhereClause(string typeParam)
    {
        return Gen.Elements("class", "struct")
            .Select(constraint => $"where {typeParam} : {constraint}");
    }

    private static Gen<AdhocRefinementRoundTripScenario> GenClassSingleConstraint()
    {
        return from name in Gen.Elements(ClassNames)
               from depParam in Gen.Elements(DependentParamNames)
               from predicate in GenPredicate()
               let source = $"class {name}<T, {depParam}:int> where {depParam} {predicate} {{\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"class single constraint: {name}");
    }

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

    private static Gen<AdhocRefinementRoundTripScenario> GenStructConstraint()
    {
        return from name in Gen.Elements(StructNames)
               from depParam in Gen.Elements(DependentParamNames)
               from predicate in GenPredicate()
               let source = $"struct {name}<{depParam}:int> where {depParam} {predicate} {{\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"struct constraint: {name}");
    }

    private static Gen<AdhocRefinementRoundTripScenario> GenAbstractConstraint()
    {
        return from name in Gen.Elements(AbstractNames)
               from depParam in Gen.Elements(DependentParamNames)
               from predicate in GenPredicate()
               let source = $"abstract {name}<{depParam}:int> where {depParam} {predicate} {{\n}}"
               select new AdhocRefinementRoundTripScenario(source,
                   $"abstract type constraint: {name}");
    }

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

    public static Arbitrary<AdhocRefinementRoundTripScenario> ArbitraryAdhocRefinementRoundTripScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
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
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AdhocRefinementRoundTripPropertyTests) })]
    public Property RoundTrip_AdhocRefinementConstraints_PrintParseIsIdempotent(AdhocRefinementRoundTripScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var ast2 = ParseOrFail(printed1);

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}