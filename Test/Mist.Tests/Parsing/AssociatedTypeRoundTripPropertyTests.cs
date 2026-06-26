// Feature: associated-types, Property 1: Pretty-print round-trip preserves structure
// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.7, 7.1, 7.2, 7.3, 7.4**

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
/// Represents a generated Mist abstract type body containing associated type slot
/// declarations for round-trip testing.
/// </summary>
public sealed class AssociatedTypeRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public AssociatedTypeRoundTripScenario(string source, string description)
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
/// Property-based tests for associated type slot declaration pretty-print round-trip (idempotence).
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.7, 7.1, 7.2, 7.3, 7.4**
///
/// For any valid associated type slot declaration (constrained or unconstrained, with or
/// without generic type parameters, with or without where clauses) embedded in an abstract type
/// or abstract type body, parsing the source text, pretty-printing the resulting syntax tree, and
/// parsing the pretty-printed output SHALL produce a syntax tree that is structurally
/// identical to the original (verified via idempotent pretty-print output).
/// </summary>
public class AssociatedTypeRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Known names for generation ─────────────────────────────────────────

    private static readonly string[] AbstractNames = ["IIterable", "IRepository", "ICollection", "IMapper"];
    private static readonly string[] SlotNames = ["Enumerator", "Element", "Key", "Value", "Iterator", "Builder"];
    private static readonly string[] TypeParamNames = ["TState", "TKey", "TValue"];
    private static readonly string[] TypeRefConstraints = ["IEnumerator", "IComparable", "IDisposable", "ICloneable"];
    private static readonly string[] PrimitiveTypes = ["int", "string", "bool", "float"];

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
        return Gen.Choose(0, 2).SelectMany(paramCount =>
        {
            if(paramCount == 0)
            {
                return Gen.Constant("new()");
            }

            return Gen.ArrayOf(Gen.Elements(PrimitiveTypes), paramCount)
                .Select(types => $"new({string.Join(", ", types)})");
        });
    }

    /// <summary>Generates a type reference constraint like IEnumerator, IComparable.</summary>
    private static Gen<string> GenTypeRefConstraint()
    {
        return Gen.Elements(TypeRefConstraints);
    }

    /// <summary>Generates a constraint list (1-3 constraints, comma-separated).</summary>
    private static Gen<string> GenConstraintList()
    {
        return Gen.Choose(1, 3).SelectMany(count =>
            Gen.ArrayOf(GenConstraint(), count)
                .Select(constraints => string.Join(", ", constraints)));
    }

    /// <summary>
    /// Generates a where clause for a given type parameter name (generic constraint style).
    /// Example: where TState : struct
    /// </summary>
    private static Gen<string> GenWhereClause(string typeParam)
    {
        return GenConstraintList().Select(cl => $"where {typeParam} : {cl}");
    }

    /// <summary>
    /// Generates 0-2 where clauses for a given set of type parameter names.
    /// </summary>
    private static Gen<string> GenWhereClauses(string[] availableTypeParams)
    {
        var maxClauses = Math.Min(2, availableTypeParams.Length);

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

    // ─── Slot declaration generators ────────────────────────────────────────

    /// <summary>
    /// Generates a constrained associated type slot without type parameters.
    /// Example: type Enumerator : IEnumerator, struct;
    /// </summary>
    private static Gen<string> GenConstrainedSlot()
    {
        return from name in Gen.Elements(SlotNames)
               from constraints in GenConstraintList()
               select $"    type {name} : {constraints};";
    }

    /// <summary>
    /// Generates an unconstrained associated type slot without type parameters.
    /// Example: type Element;
    /// </summary>
    private static Gen<string> GenUnconstrainedSlot()
    {
        return from name in Gen.Elements(SlotNames)
               select $"    type {name};";
    }

    /// <summary>
    /// Generates a constrained associated type slot with type parameters.
    /// Example: type Iterator&lt;TState&gt; : IEnumerator;
    /// </summary>
    private static Gen<string> GenConstrainedSlotWithTypeParams()
    {
        return from name in Gen.Elements(SlotNames)
               from paramCount in Gen.Choose(1, 2)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from constraints in GenConstraintList()
               select $"    type {name}<{typeParamStr}> : {constraints};";
    }

    /// <summary>
    /// Generates an unconstrained associated type slot with type parameters.
    /// Example: type Iterator&lt;TState&gt;;
    /// </summary>
    private static Gen<string> GenUnconstrainedSlotWithTypeParams()
    {
        return from name in Gen.Elements(SlotNames)
               from paramCount in Gen.Choose(1, 2)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               select $"    type {name}<{typeParamStr}>;";
    }

    /// <summary>
    /// Generates a constrained associated type slot with type parameters and where clauses.
    /// Example: type Iterator&lt;TState&gt; : IEnumerator where TState : struct;
    /// </summary>
    private static Gen<string> GenConstrainedSlotWithWhereClauses()
    {
        return from name in Gen.Elements(SlotNames)
               from paramCount in Gen.Choose(1, 2)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamStr = string.Join(", ", typeParams)
               from constraints in GenConstraintList()
               from whereClauses in GenWhereClauses(typeParams)
               where whereClauses.Length > 0
               select $"    type {name}<{typeParamStr}> : {constraints}{whereClauses};";
    }

    /// <summary>Generates any valid slot declaration variant.</summary>
    private static Gen<string> GenSlotDeclaration()
    {
        return Gen.OneOf(
            GenConstrainedSlot(),
            GenUnconstrainedSlot(),
            GenConstrainedSlotWithTypeParams(),
            GenUnconstrainedSlotWithTypeParams(),
            GenConstrainedSlotWithWhereClauses());
    }

    // ─── Abstract type body generators ────────────────────────────────────

    /// <summary>
    /// Generates an abstract type containing 1-3 associated type slot declarations.
    /// </summary>
    private static Gen<AssociatedTypeRoundTripScenario> GenAbstractWithSlots()
    {
        return from name in Gen.Elements(AbstractNames)
               from slotCount in Gen.Choose(1, 3)
               from slots in Gen.ArrayOf(GenSlotDeclaration(), slotCount)
               let body = string.Join("\n", slots)
               let source = $"abstract {name} {{\n{body}\n}}"
               select new AssociatedTypeRoundTripScenario(source,
                   $"abstract {name} with {slotCount} slot(s)");
    }

    /// <summary>
    /// Generates a generic abstract type containing associated type slot declarations.
    /// </summary>
    private static Gen<AssociatedTypeRoundTripScenario> GenGenericAbstractWithSlots()
    {
        return from name in Gen.Elements(AbstractNames)
               from slotCount in Gen.Choose(1, 2)
               from slots in Gen.ArrayOf(GenSlotDeclaration(), slotCount)
               let body = string.Join("\n", slots)
               let source = $"abstract {name}<T> {{\n{body}\n}}"
               select new AssociatedTypeRoundTripScenario(source,
                   $"generic abstract type {name}<T> with {slotCount} slot(s)");
    }

    // ─── Combined generator ─────────────────────────────────────────────────

    private static Gen<AssociatedTypeRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenAbstractWithSlots(),
            GenGenericAbstractWithSlots());
    }

    // ─── Arbitrary registration ─────────────────────────────────────────────

    public static Arbitrary<AssociatedTypeRoundTripScenario> ArbitraryAssociatedTypeRoundTripScenario()
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
    // Property 1: Pretty-print round-trip preserves structure
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.7, 7.1, 7.2, 7.3, 7.4**
    //
    // For any valid associated type slot declaration (constrained or unconstrained,
    // with or without generic type parameters, with or without where clauses)
    // embedded in an abstract type body, parsing the source text,
    // pretty-printing the resulting syntax tree, and parsing the pretty-printed
    // output SHALL produce a syntax tree that is structurally identical to the
    // original (verified via idempotent pretty-print output).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeRoundTripPropertyTests) })]
    public Property RoundTrip_AssociatedTypeSlots_PrintParseIsIdempotent(AssociatedTypeRoundTripScenario scenario)
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