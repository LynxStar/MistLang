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

/// <summary>
/// Represents a generated Mist declaration with constraint clauses for round-trip testing.
/// </summary>
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

/// <summary>
/// Property-based tests for constraint clause round-trip (idempotence).
///
/// **Validates: Requirements 2.7, 9.2, 9.3**
///
/// For any valid Mist declaration (class, struct, abstract, method, or type alias)
/// with any combination of constraint clauses containing any valid constraint variants
/// (class, struct, type reference, new(...)), parsing the source text then pretty-printing
/// then re-parsing SHALL produce an equivalent AST (verified via idempotent pretty-print output).
/// </summary>
public class ConstraintClauseRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Known names for generation ─────────────────────────────────────────

    private static readonly string[] TypeParamNames = ["T", "U", "V"];
    private static readonly string[] ClassNames = ["Box", "Factory", "Container", "Sorter"];
    private static readonly string[] StructNames = ["Wrapper", "Entry", "Cell"];
    private static readonly string[] AbstractNames = ["IRepository", "IReadable", "IWritable"];
    private static readonly string[] MethodNames = ["Create", "Transform", "Convert"];
    private static readonly string[] AliasNames = ["MyList", "MyMap", "MySet"];
    private static readonly string[] TypeRefConstraints = ["IFoo", "IBar", "IComparable"];
    private static readonly string[] PrimitiveTypes = ["int", "string", "bool", "float", "double"];

    // ─── Constraint generators ──────────────────────────────────────────────

    /// <summary>Generates a single constraint string.</summary>
    private static Gen<string> GenConstraint()
    {
        return Gen.OneOf(
            Gen.Constant("class"),
            Gen.Constant("struct"),
            GenConstructorConstraint(),
            GenTypeRefConstraint());
    }

    /// <summary>Generates a constructor constraint: new(), new(int), new(int, string), etc.</summary>
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

    /// <summary>Generates a type reference constraint like IFoo, IBar.</summary>
    private static Gen<string> GenTypeRefConstraint()
    {
        return Gen.Elements(TypeRefConstraints);
    }

    /// <summary>Generates a constraint list (1-4 constraints, comma-separated).</summary>
    private static Gen<string> GenConstraintList()
    {
        return Gen.Choose(1, 4).SelectMany(count =>
            Gen.ArrayOf(GenConstraint(), count)
                .Select(constraints => string.Join(", ", constraints)));
    }

    /// <summary>
    /// Generates a single where clause for a given type parameter name.
    /// </summary>
    private static Gen<string> GenWhereClause(string typeParam)
    {
        return GenConstraintList().Select(cl => $"where {typeParam} : {cl}");
    }

    /// <summary>
    /// Generates 0-3 where clauses for a given set of type parameter names.
    /// Each where clause references a distinct type parameter.
    /// </summary>
    private static Gen<string> GenWhereClauses(string[] availableTypeParams)
    {
        var maxClauses = Math.Min(3, availableTypeParams.Length);

        return Gen.Choose(0, maxClauses).SelectMany(clauseCount =>
        {
            if(clauseCount == 0)
            {
                return Gen.Constant("");
            }

            // Build a generator that produces all clauses for the selected type params
            var selectedParams = availableTypeParams.Take(clauseCount).ToArray();

            // Chain generators for each type param
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

    // ─── Declaration generators ─────────────────────────────────────────────

    /// <summary>Generates a generic class declaration with constraint clauses.</summary>
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

    /// <summary>Generates a generic struct declaration with constraint clauses.</summary>
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

    /// <summary>Generates a generic abstract declaration with constraint clauses.</summary>
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

    /// <summary>Generates a class with a generic method that has constraint clauses.</summary>
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

    /// <summary>Generates a generic type alias declaration with constraint clauses.</summary>
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

    // ─── Combined generator ─────────────────────────────────────────────────

    private static Gen<ConstraintClauseRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassDeclaration(),
            GenStructDeclaration(),
            GenAbstractDeclaration(),
            GenMethodDeclaration(),
            GenTypeAliasDeclaration());
    }

    // ─── Arbitrary registration ─────────────────────────────────────────────

    public static Arbitrary<ConstraintClauseRoundTripScenario> ArbitraryConstraintClauseRoundTripScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

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
    // Property 1: Constraint Clause Round-Trip
    //
    // **Validates: Requirements 2.7, 9.2, 9.3**
    //
    // For any valid Mist declaration with constraint clauses, parsing the source
    // text then pretty-printing then re-parsing and pretty-printing again SHALL
    // produce identical output (idempotence).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConstraintClauseRoundTripPropertyTests) })]
    public Property RoundTrip_ConstraintClauses_PrintParseIsIdempotent(ConstraintClauseRoundTripScenario scenario)
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