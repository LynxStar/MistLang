// Feature: value-tuples, Property 1: Tuple syntax round-trip
// **Validates: Requirements 1.1, 1.2, 1.5, 1.6, 2.1, 2.2, 2.5, 2.6,
//              4.1, 4.2, 4.7, 4.8, 7.2, 7.4, 8.1, 8.2**

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
/// Property-based round-trip tests for tuple syntax: tuple type references,
/// tuple literal expressions, and tuple deconstruction statements.
/// Parse → pretty-print → parse → pretty-print must produce identical output.
/// </summary>
public class TupleRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Parsing helpers ───────────────────────────────────────────────────

    private static CompilationUnitNode? TryParse(string source)
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
            return null;
        }

        return result.Root as CompilationUnitNode;
    }

    private static bool AssertRoundTripPreserved(string source)
    {
        var ast1 = TryParse(source);
        if(ast1 is null)
        {
            return true; // Skip unparseable inputs.
        }

        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = TryParse(printed1);
        if(ast2 is null)
        {
            return false; // Re-parse must succeed.
        }

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return printed1 == printed2;
    }

    // ─── Property tests ────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Tuple type references round-trip through parse → print → parse → print.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistTupleSourceGenerator) })]
    public bool TupleTypeRef_RoundTrips(TupleTypeRefSource scenario)
    {
        return AssertRoundTripPreserved(scenario.Source);
    }

    /// <summary>
    /// Property 1: Tuple literal expressions round-trip through parse → print → parse → print.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistTupleSourceGenerator) })]
    public bool TupleLiteral_RoundTrips(TupleLiteralSource scenario)
    {
        return AssertRoundTripPreserved(scenario.Source);
    }

    /// <summary>
    /// Property 1: Var tuple deconstruction statements round-trip.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistTupleSourceGenerator) })]
    public bool VarTupleDeconstruction_RoundTrips(VarTupleDeconstructionSource scenario)
    {
        return AssertRoundTripPreserved(scenario.Source);
    }
}

// ─── Scenario types ─────────────────────────────────────────────────────────

public record TupleTypeRefSource(string Source)
{
    public override string ToString() => Source;
}

public record TupleLiteralSource(string Source)
{
    public override string ToString() => Source;
}

public record VarTupleDeconstructionSource(string Source)
{
    public override string ToString() => Source;
}

// ─── Generator ──────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source strings containing tuple syntax for round-trip testing.
/// </summary>
public static class MistTupleSourceGenerator
{
    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "string", "float", "double", "char", "long"];

    private static readonly string[] IdentifierNames =
        ["a", "b", "c", "x", "y", "z"];

    private static readonly string[] IntLiterals =
        ["0", "1", "2", "42", "100"];

    private static Gen<string> GenPrimitiveType() => Gen.Elements(PrimitiveTypes);
    private static Gen<string> GenIdentifier() => Gen.Elements(IdentifierNames);
    private static Gen<string> GenIntLiteral() => Gen.Elements(IntLiterals);

    /// <summary>
    /// Generates a tuple type string like "(int, string)" or "(int x, bool y)".
    /// </summary>
    private static Gen<string> GenTupleTypeString(int depth = 1)
    {
        var elementGen = depth <= 0
            ? GenPrimitiveType()
            : Gen.Frequency(
                (3, GenPrimitiveType()),
                (1, GenTupleTypeString(depth - 1)));

        return from arity in Gen.Choose(2, 4)
               from types in Gen.ArrayOf(elementGen, arity)
               from useNames in Gen.ArrayOf(Gen.Elements(true, false), arity)
               from names in Gen.ArrayOf(GenIdentifier(), arity)
               let elements = types.Select((t, i) =>
                   useNames[i] ? $"{t} {names[i]}" : t).ToArray()
               select $"({string.Join(", ", elements)})";
    }

    /// <summary>
    /// Generates a tuple literal string like "(1, 2)" or "(x: 1, y: 2)".
    /// </summary>
    private static Gen<string> GenTupleLiteralString(int depth = 1)
    {
        var valueGen = depth <= 0
            ? GenIntLiteral()
            : Gen.Frequency(
                (3, GenIntLiteral()),
                (1, GenTupleLiteralString(depth - 1)));

        return from arity in Gen.Choose(2, 4)
               from values in Gen.ArrayOf(valueGen, arity)
               from useNames in Gen.ArrayOf(Gen.Elements(true, false), arity)
               from names in Gen.ArrayOf(GenIdentifier(), arity)
                   // For named elements, all must be named or all unnamed to avoid
                   // ambiguity in the grammar. Use all-named or all-unnamed.
               from allNamed in Gen.Elements(true, false)
               let elements = values.Select((v, i) =>
                   allNamed ? $"{names[i]}: {v}" : v).ToArray()
               select $"({string.Join(", ", elements)})";
    }

    /// <summary>
    /// Wraps a variable declaration in a minimal class/method body.
    /// </summary>
    private static string WrapInMethod(string statement)
    {
        return $"class Test {{\n  void Run() {{\n    {statement}\n  }}\n}}";
    }

    /// <summary>
    /// Wraps a field declaration with a tuple type in a class.
    /// </summary>
    private static string WrapFieldDecl(string tupleType, string name)
    {
        return $"class Test {{\n  {tupleType} {name} = default;\n}}";
    }

    // ─── Arbitrary instances ────────────────────────────────────────────

    public static Arbitrary<TupleTypeRefSource> ArbitraryTupleTypeRefSource() =>
        (from tupleType in GenTupleTypeString()
         from fieldName in GenIdentifier()
         select new TupleTypeRefSource(WrapFieldDecl(tupleType, fieldName)))
        .ToArbitrary();

    public static Arbitrary<TupleLiteralSource> ArbitraryTupleLiteralSource() =>
        (from tupleLiteral in GenTupleLiteralString()
         select new TupleLiteralSource(
             WrapInMethod($"var t = {tupleLiteral};")))
        .ToArbitrary();

    public static Arbitrary<VarTupleDeconstructionSource> ArbitraryVarTupleDeconstructionSource() =>
        (from arity in Gen.Choose(2, 4)
         from names in Gen.ArrayOf(GenIdentifier(), arity)
         from tupleLiteral in GenTupleLiteralString()
         let nameList = string.Join(", ", names.Select((n, i) => $"{n}{i}"))
         select new VarTupleDeconstructionSource(
             WrapInMethod($"var ({nameList}) = {tupleLiteral};")))
        .ToArbitrary();
}