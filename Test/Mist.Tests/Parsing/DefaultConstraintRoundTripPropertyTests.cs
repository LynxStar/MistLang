// Feature: optional-defaultability, Property 3: Default constraint round-trip
// **Validates: Requirements 5.8**

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
/// Represents a generated Mist declaration with <c>where T : default</c> constraint clauses
/// for round-trip testing.
/// </summary>
public sealed class DefaultConstraintRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public DefaultConstraintRoundTripScenario(string source, string description)
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
/// Property-based tests for default constraint round-trip (idempotence).
///
/// **Validates: Requirements 5.8**
///
/// For any valid Mist AST containing a <c>where T : default</c> constraint clause,
/// pretty-printing then re-parsing SHALL produce an equivalent AST. Verified via
/// idempotent pretty-print output (print → parse → print must be identical).
/// </summary>
public class DefaultConstraintRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Known names for generation ─────────────────────────────────────────

    private static readonly string[] TypeParamNames = ["T", "U", "V"];
    private static readonly string[] ClassNames = ["Factory", "Container", "Pool", "Cache"];
    private static readonly string[] StructNames = ["Slot", "Entry", "Cell"];
    private static readonly string[] AbstractNames = ["IDefaultable", "IPool", "IAllocator"];
    private static readonly string[] MethodNames = ["Create", "Allocate", "Reset"];
    private static readonly string[] AliasNames = ["DefaultList", "DefaultMap", "DefaultSet"];
    private static readonly string[] TypeRefConstraints = ["IFoo", "IBar", "IComparable"];
    private static readonly string[] PrimitiveTypes = ["int", "string", "bool", "float", "double"];

    // ─── Constraint generators ──────────────────────────────────────────────

    /// <summary>Generates a single non-default constraint string.</summary>
    private static Gen<string> GenNonDefaultConstraint()
    {
        return Gen.OneOf(
            Gen.Constant("class"),
            Gen.Constant("struct"),
            Gen.Constant("new()"),
            Gen.Elements(TypeRefConstraints));
    }

    /// <summary>
    /// Generates a constraint list that always contains <c>default</c>,
    /// optionally combined with other constraints.
    /// </summary>
    private static Gen<string> GenConstraintListWithDefault()
    {
        return Gen.Choose(0, 2).SelectMany(extraCount =>
        {
            if(extraCount == 0)
            {
                return Gen.Constant("default");
            }

            return Gen.ArrayOf(GenNonDefaultConstraint(), extraCount)
                .SelectMany(others =>
                {
                    // Randomly place 'default' at the beginning, middle, or end
                    return Gen.Choose(0, others.Length).Select(insertPos =>
                    {
                        var allConstraints = new List<string>(others);
                        allConstraints.Insert(insertPos, "default");
                        return string.Join(", ", allConstraints);
                    });
                });
        });
    }

    /// <summary>
    /// Generates where clauses where at least one type parameter has a <c>default</c> constraint.
    /// Supports single and multiple type parameters.
    /// </summary>
    private static Gen<string> GenWhereClausesWithDefault(string[] availableTypeParams)
    {
        var maxClauses = Math.Min(3, availableTypeParams.Length);

        return Gen.Choose(1, maxClauses).SelectMany(clauseCount =>
        {
            var selectedParams = availableTypeParams.Take(clauseCount).ToArray();

            // At least one clause must have 'default'; pick which one
            return Gen.Choose(0, selectedParams.Length - 1).SelectMany(defaultClauseIdx =>
            {
                var result = Gen.Constant("");

                for(var i = 0; i < selectedParams.Length; i++)
                {
                    var tp = selectedParams[i];
                    var constraintGen = (i == defaultClauseIdx)
                        ? GenConstraintListWithDefault()
                        : Gen.OneOf(GenConstraintListWithDefault(), GenNonDefaultConstraint());

                    if(i == 0)
                    {
                        result = constraintGen.Select(cl => $" where {tp} : {cl}");
                    }
                    else
                    {
                        var idx = i;
                        result = result.SelectMany(prev =>
                            constraintGen.Select(cl => prev + $" where {selectedParams[idx]} : {cl}"));
                    }
                }

                return result;
            });
        });
    }

    // ─── Declaration generators ─────────────────────────────────────────────

    /// <summary>Generates a generic class declaration with a default constraint.</summary>
    private static Gen<DefaultConstraintRoundTripScenario> GenClassDeclaration()
    {
        return from name in Gen.Elements(ClassNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from whereClauses in GenWhereClausesWithDefault(typeParams)
               let source = $"class {name}<{typeParamStr}>{whereClauses} {{\n}}"
               select new DefaultConstraintRoundTripScenario(source,
                   $"class: {name}<{typeParamStr}>");
    }

    /// <summary>Generates a generic struct declaration with a default constraint.</summary>
    private static Gen<DefaultConstraintRoundTripScenario> GenStructDeclaration()
    {
        return from name in Gen.Elements(StructNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from whereClauses in GenWhereClausesWithDefault(typeParams)
               let source = $"struct {name}<{typeParamStr}>{whereClauses} {{\n}}"
               select new DefaultConstraintRoundTripScenario(source,
                   $"struct: {name}<{typeParamStr}>");
    }

    /// <summary>Generates a generic abstract declaration with a default constraint.</summary>
    private static Gen<DefaultConstraintRoundTripScenario> GenAbstractDeclaration()
    {
        return from name in Gen.Elements(AbstractNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from whereClauses in GenWhereClausesWithDefault(typeParams)
               let source = $"abstract {name}<{typeParamStr}>{whereClauses} {{\n}}"
               select new DefaultConstraintRoundTripScenario(source,
                   $"abstract: {name}<{typeParamStr}>");
    }

    /// <summary>Generates a class with a generic method that has a default constraint.</summary>
    private static Gen<DefaultConstraintRoundTripScenario> GenMethodDeclaration()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from whereClauses in GenWhereClausesWithDefault(typeParams)
               let source = $"class {className} {{\n    public void {methodName}<{typeParamStr}>(){whereClauses} {{\n    }}\n}}"
               select new DefaultConstraintRoundTripScenario(source,
                   $"method: {className}.{methodName}<{typeParamStr}>");
    }

    /// <summary>Generates a generic type alias declaration with a default constraint.</summary>
    private static Gen<DefaultConstraintRoundTripScenario> GenTypeAliasDeclaration()
    {
        return from name in Gen.Elements(AliasNames)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from targetType in Gen.Elements(PrimitiveTypes)
               from whereClauses in GenWhereClausesWithDefault(typeParams)
               let source = $"type {name}<{typeParamStr}> = {targetType}{whereClauses};"
               select new DefaultConstraintRoundTripScenario(source,
                   $"type alias: {name}<{typeParamStr}>");
    }

    // ─── Combined generator ─────────────────────────────────────────────────

    private static Gen<DefaultConstraintRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassDeclaration(),
            GenStructDeclaration(),
            GenAbstractDeclaration(),
            GenMethodDeclaration(),
            GenTypeAliasDeclaration());
    }

    // ─── Arbitrary registration ─────────────────────────────────────────────

    public static Arbitrary<DefaultConstraintRoundTripScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

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
    // Property 3: Default constraint round-trip
    //
    // **Validates: Requirements 5.8**
    //
    // For any valid Mist AST containing a `where T : default` constraint clause,
    // pretty-printing then re-parsing SHALL produce an equivalent AST. Verified
    // via idempotent pretty-print output (print → parse → print must be identical).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DefaultConstraintRoundTripPropertyTests) })]
    public Property RoundTrip_DefaultConstraint_PrintParseIsIdempotent(DefaultConstraintRoundTripScenario scenario)
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DefaultConstraintRoundTripPropertyTests) })]
    public Property RoundTrip_DefaultConstraint_OutputContainsDefaultKeyword(DefaultConstraintRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        // The pretty-printed output must contain "default" as a constraint keyword
        var containsDefault = printed1.Contains("default");

        return containsDefault.ToProperty()
            .Label($"Pretty-printed output does not contain 'default' keyword.\nSource: {scenario.Source}\nPrinted:\n{printed1}");
    }
}