// Feature: generic-constraints, Property 1: Constraint Clause Round-Trip

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public sealed class ConstraintClauseRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public ConstraintClauseRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public class ConstraintClauseRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static readonly string[] TypeParamNames = ["T", "U", "V"];
    private static readonly string[] ClassNames = ["Box", "Factory", "Container", "Sorter"];
    private static readonly string[] StructNames = ["Wrapper", "Entry", "Cell"];
    private static readonly string[] AbstractNames = ["IRepository", "IReadable", "IWritable"];
    private static readonly string[] MethodNames = ["Create", "Transform", "Convert"];
    private static readonly string[] AliasNames = ["MyList", "MyMap", "MySet"];
    private static readonly string[] TypeRefConstraints = ["IFoo", "IBar", "IComparable"];
    private static readonly string[] PrimitiveTypes = ["int", "string", "bool", "float", "double"];

    private static Gen<string> GenConstraint()
    {
        return Gen.OneOf(
            Gen.Constant("class"),
            Gen.Constant("struct"),
            GenConstructorConstraint(),
            GenTypeRefConstraint());
    }

    private static Gen<string> GenConstructorConstraint()
    {
        return Gen.Choose(0, 3).SelectMany(paramCount =>
        {
            if(paramCount == 0)
            {
                return Gen.Constant("new()");
            }

            return Gen.ArrayOf(Gen.Elements(PrimitiveTypes), paramCount)
                .Select(types => $"new({string.Join(", ", types)})");
        });
    }

    private static Gen<string> GenTypeRefConstraint()
    {
        return Gen.Elements(TypeRefConstraints);
    }

    private static Gen<string> GenConstraintList()
    {
        return Gen.Choose(1, 4).SelectMany(count =>
            Gen.ArrayOf(GenConstraint(), count)
                .Select(constraints => string.Join(", ", constraints)));
    }

    private static Gen<string> GenWhereClause(string typeParam)
    {
        return GenConstraintList().Select(cl => $"where {typeParam} : {cl}");
    }

    private static Gen<string> GenWhereClauses(string[] availableTypeParams)
    {
        var maxClauses = Math.Min(3, availableTypeParams.Length);

        return Gen.Choose(0, maxClauses).SelectMany(clauseCount =>
        {
            if(clauseCount == 0)
            {
                return Gen.Constant("");
            }

            var selectedParams = availableTypeParams.Take(clauseCount).ToArray();

            var result = Gen.Constant("");
            for(var i = 0; i < selectedParams.Length; i++)
            {
                var tp = selectedParams[i];
                result = i == 0
                    ? GenWhereClause(tp).Select(c => " " + c)
                    : result.SelectMany(prev =>
                        GenWhereClause(tp).Select(c => prev + " " + c));
            }

            return result;
        });
    }

    private static Gen<ConstraintClauseRoundTripScenario> GenClassDeclaration()
    {
        return from name in Gen.Elements(ClassNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from whereClauses in GenWhereClauses(typeParams)
               let source = $"class {name}<{typeParamStr}>{whereClauses} {{\n}}"
               select new ConstraintClauseRoundTripScenario(source, $"class: {name}<{typeParamStr}>");
    }

    private static Gen<ConstraintClauseRoundTripScenario> GenStructDeclaration()
    {
        return from name in Gen.Elements(StructNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from whereClauses in GenWhereClauses(typeParams)
               let source = $"struct {name}<{typeParamStr}>{whereClauses} {{\n}}"
               select new ConstraintClauseRoundTripScenario(source, $"struct: {name}<{typeParamStr}>");
    }

    private static Gen<ConstraintClauseRoundTripScenario> GenAbstractDeclaration()
    {
        return from name in Gen.Elements(AbstractNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from whereClauses in GenWhereClauses(typeParams)
               let source = $"abstract {name}<{typeParamStr}>{whereClauses} {{\n}}"
               select new ConstraintClauseRoundTripScenario(source, $"abstract: {name}<{typeParamStr}>");
    }

    private static Gen<ConstraintClauseRoundTripScenario> GenMethodDeclaration()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from whereClauses in GenWhereClauses(typeParams)
               let source = $"class {className} {{\n    public void {methodName}<{typeParamStr}>(){whereClauses} {{\n    }}\n}}"
               select new ConstraintClauseRoundTripScenario(source, $"method: {className}.{methodName}<{typeParamStr}>");
    }

    private static Gen<ConstraintClauseRoundTripScenario> GenTypeAliasDeclaration()
    {
        return from name in Gen.Elements(AliasNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from targetType in Gen.Elements(PrimitiveTypes)
               from whereClauses in GenWhereClauses(typeParams)
               let source = $"type {name}<{typeParamStr}> = {targetType}{whereClauses};"
               select new ConstraintClauseRoundTripScenario(source, $"type alias: {name}<{typeParamStr}>");
    }

    private static Gen<ConstraintClauseRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassDeclaration(),
            GenStructDeclaration(),
            GenAbstractDeclaration(),
            GenMethodDeclaration(),
            GenTypeAliasDeclaration());
    }

    public static Arbitrary<ConstraintClauseRoundTripScenario> ArbitraryConstraintClauseRoundTripScenario()
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
    // **Validates: Requirements 2.7, 9.2, 9.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConstraintClauseRoundTripPropertyTests) })]
    public Property RoundTrip_ConstraintClauses_PrintParseIsIdempotent(ConstraintClauseRoundTripScenario scenario)
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