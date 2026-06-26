// Feature: unknown-type, Property 1: Round-trip parity for `unknown` references
//
// Validates: Requirement 14.4
//
// For any valid Mist AST containing UnknownSingleTypeRefSingleTypeRefNode in any
// permitted position (bare local, method return, method parameter, and as a
// type argument at arbitrary depth), parse → pretty-print → parse SHALL
// produce an AST that is structurally equivalent to the original.
//
// Following the convention established by GenericRoundTripPropertyTests, the
// round-trip is asserted by comparing the pretty-printed source text of the
// original parse against the pretty-printed source text of the re-parse:
//
//     text₁    ─parse─►  AST₁  ─print─►  text₂  ─parse─►  AST₂  ─print─►  text₃
//
// and asserting text₂ == text₃. Because the pretty printer is deterministic
// and one-to-one on structure, equality of the two printed outputs is a
// strong proxy for structural AST equivalence. We additionally assert that
// the parsed AST contains a UnknownSingleTypeRefSingleTypeRefNode at the expected
// positions, before and after the round-trip, so that the test cannot pass
// accidentally by collapsing `unknown` to a different node type.
//
// All work runs in-process — no `dotnet` process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class UnknownTypeRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Parsing helpers ───────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        var cu = Assert.IsType<CompilationUnitNode>(result.Root);
        return cu;
    }

    /// <summary>
    /// Runs the full parse → pretty-print → parse → pretty-print round-trip
    /// and asserts the two printed outputs are identical. Returns the parsed
    /// AST after the first parse so the caller can make additional
    /// position-specific assertions (e.g., "the local's type is unknown").
    ///
    /// Each pretty-print call passes the source text that produced the AST,
    /// so that <c>ExtractQualifiedName</c> can recover dotted / named
    /// identifiers via their <c>Span</c> — the generated parser leaves
    /// <c>QualifiedIdentifierNode.IDENTIFIER.Lexeme</c> at its default, so
    /// the source-text overload is the only one that round-trips named
    /// type references produced by the parser.
    /// </summary>
    private static CompilationUnitNode AssertRoundTripPreserved(string source)
    {
        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);

        // Sanity: both printed outputs mention the `unknown` keyword (otherwise
        // the test would be vacuous — the keyword could have been silently
        // dropped and we'd still get trivial equality).
        Assert.Contains("unknown", printed1);
        Assert.Contains("unknown", printed2);

        return ast1;
    }

    /// <summary>
    /// Unfurls a <see cref="TypeArgumentListNode"/> into source order:
    /// the grammar stores the first argument in the singular property and the
    /// rest in the array. This mirrors the helper used by UnknownTypeParserTests.
    /// </summary>
    private static IReadOnlyList<TypeReferenceNode> GetTypeArgs(TypeArgumentListNode list)
    {
        var result = new List<TypeReferenceNode> { list.GetTypeReference() };
        result.AddRange(list.GetTypeReferences());
        return result;
    }

    /// <summary>
    /// Returns true iff the given type reference contains an
    /// <see cref="UnknownSingleTypeRefSingleTypeRefNode"/> at any depth (bare or
    /// nested inside a type-argument list).
    /// </summary>
    private static bool ContainsUnknown(TypeReferenceNode typeRef)
    {
        return typeRef.First switch
        {
            UnknownSingleTypeRefSingleTypeRefNode => true,
            NamedSingleTypeRefSingleTypeRefNode named when named.TypeArgs is not null => GetTypeArgs(named.TypeArgs).Any(ContainsUnknown),
            _ => false,
        };
    }

    private static TypedVariableDeclarationVariableDeclarationStatementNode GetFirstLocalDecl(CompilationUnitNode cu)
    {
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        return Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
    }

    private static MethodDeclarationNode GetFirstMethod(CompilationUnitNode cu)
    {
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        return methodMember.Method;
    }

    // ─── Fact-based tests — one per legal position ─────────────────────────
    //
    // The task spec enumerates six essential positions that must round-trip.
    // Each fact parses a minimal program, runs the full round-trip, and also
    // verifies that the AST after the first parse carries a
    // UnknownSingleTypeRefSingleTypeRefNode at the expected spot. This guards
    // against a regression in which `unknown` is silently dropped or
    // collapsed into another node type while still printing back to itself.

    [Fact]
    public void RoundTrip_BareUnknown_InLocalVariable()
    {
        // `unknown x = 0;` — UnknownType as the declared type of a local.
        const string source = "class T { public void M() { unknown x = 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var local = GetFirstLocalDecl(ast);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(local.Type.First);
    }

    [Fact]
    public void RoundTrip_Unknown_InListTypeArg()
    {
        // `List<unknown> xs = 0;` — UnknownType at a single top-level
        // type-argument position.
        const string source = "class T { public void M() { List<unknown> xs = 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var local = GetFirstLocalDecl(ast);
        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(local.Type.First);
        Assert.NotNull(named.TypeArgs);
        var args = GetTypeArgs(named.TypeArgs!);
        _ = Assert.Single(args);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(args[0].First);
    }

    [Fact]
    public void RoundTrip_Unknown_InMapSecondTypeArg()
    {
        // `Map<string, unknown> m = 0;` — UnknownType at the second
        // type-argument position; the first argument is a primitive.
        const string source = "class T { public void M() { Map<string, unknown> m = 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var local = GetFirstLocalDecl(ast);
        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(local.Type.First);
        Assert.NotNull(named.TypeArgs);
        var args = GetTypeArgs(named.TypeArgs!);
        Assert.Equal(2, args.Count);
        _ = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(args[0].First);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(args[1].First);
    }

    [Fact]
    public void RoundTrip_Unknown_InOptionalListNested()
    {
        // `Optional<List<unknown>> o = 0;` — UnknownType at a nested
        // type-argument position two levels deep.
        const string source = "class T { public void M() { Optional<List<unknown>> o = 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var local = GetFirstLocalDecl(ast);
        var outer = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(local.Type.First);
        Assert.NotNull(outer.TypeArgs);
        var outerArgs = GetTypeArgs(outer.TypeArgs!);
        _ = Assert.Single(outerArgs);
        var inner = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(outerArgs[0].First);
        Assert.NotNull(inner.TypeArgs);
        var innerArgs = GetTypeArgs(inner.TypeArgs!);
        _ = Assert.Single(innerArgs);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(innerArgs[0].First);
    }

    [Fact]
    public void RoundTrip_Unknown_AsMethodReturnType()
    {
        // `public unknown M() { return 0; }` — UnknownType as a method
        // return type. Verifies the parser produces and the pretty printer
        // re-emits `unknown` at the return-type position.
        const string source = "class T { public unknown M() { return 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var method = GetFirstMethod(ast);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(method.ReturnType.First);
    }

    [Fact]
    public void RoundTrip_Unknown_AsMethodParameter()
    {
        // `public void M(unknown x) { }` — UnknownType as a method
        // parameter type.
        const string source = "class T { public void M(unknown x) { } }";

        var ast = AssertRoundTripPreserved(source);

        var method = GetFirstMethod(ast);
        Assert.NotNull(method.Parameters);
        var firstParam = method.Parameters!.Parameter;
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(firstParam.Type.First);
    }

    [Fact]
    public void RoundTrip_Unknown_AsMethodParameterAndReturn_WithNestedTypeArg()
    {
        // `public List<unknown> M(Map<string, unknown> x) { return 0; }` —
        // UnknownType at both a parameter position and a return position,
        // and nested inside type-argument lists. Exercises the interaction
        // between multiple legal positions in a single declaration.
        const string source = "class T { public List<unknown> M(Map<string, unknown> x) { return 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var method = GetFirstMethod(ast);

        // Return type: List<unknown>
        var retNamed = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(method.ReturnType.First);
        Assert.NotNull(retNamed.TypeArgs);
        var retArgs = GetTypeArgs(retNamed.TypeArgs!);
        _ = Assert.Single(retArgs);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(retArgs[0].First);

        // First parameter: Map<string, unknown>
        Assert.NotNull(method.Parameters);
        var paramNamed = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(method.Parameters!.Parameter.Type.First);
        Assert.NotNull(paramNamed.TypeArgs);
        var paramArgs = GetTypeArgs(paramNamed.TypeArgs!);
        Assert.Equal(2, paramArgs.Count);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(paramArgs[1].First);
    }

    // ─── Property-based test — random combinations of legal positions ──────
    //
    // Generates a source program by choosing, for each of three positions
    // (method return type, method parameter type, method local-variable
    // type), one of a pool of type-reference strings. The pool is closed
    // under nesting and always contains at least one occurrence of
    // `unknown` at some depth, chosen deterministically by the scenario
    // shape so that every generated program exercises Requirement 14.4 at
    // at least one position.
    //
    // All work happens in-process; no `dotnet` process spawns.

#pragma warning disable CA1034 // nested type is the FsCheck-friendly way to surface a scenario record
    public record UnknownPositionScenario(
        string ReturnType,
        string ParamType,
        string LocalType)
    {
        public string ToSource()
        {
            return $"class T {{ public {ReturnType} M({ParamType} p) {{ {LocalType} x = 0; return 0; }} }}";
        }

        public bool MentionsUnknown =>
            ReturnType.Contains("unknown") ||
            ParamType.Contains("unknown") ||
            LocalType.Contains("unknown");

        public override string ToString()
        {
            return $"return={ReturnType}, param={ParamType}, local={LocalType}";
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Type-reference spellings that contain `unknown` at some depth.
    /// Every non-empty subset is a legal type-reference at each position.
    /// </summary>
    private static readonly string[] UnknownBearingTypes =
    [
        "unknown",
        "List<unknown>",
        "Map<string, unknown>",
        "Map<unknown, int>",
        "Optional<unknown>",
        "Optional<List<unknown>>",
        "List<Optional<unknown>>",
        "Map<string, List<unknown>>",
    ];

    /// <summary>
    /// Type-reference spellings that do not mention `unknown`. Used as
    /// "filler" at positions where the scenario shape doesn't require
    /// `unknown`, so the test still exercises mixed declarations.
    /// </summary>
    private static readonly string[] NonUnknownTypes =
    [
        "int",
        "string",
        "List<int>",
        "Map<string, int>",
    ];

    public static Gen<UnknownPositionScenario> GenScenario()
    {
        // Pick a 3-bit mask (1..7) indicating which of the three positions
        // carry an `unknown`-bearing type. The all-zero mask is excluded so
        // every generated scenario exercises the feature at ≥ 1 position.
        var genMask = Gen.Choose(1, 7);
        var genUnknown = Gen.Elements(UnknownBearingTypes);
        var genNonUnknown = Gen.Elements(NonUnknownTypes);

        return from mask in genMask
               from ret in (mask & 0b100) != 0 ? genUnknown : genNonUnknown
               from prm in (mask & 0b010) != 0 ? genUnknown : genNonUnknown
               from loc in (mask & 0b001) != 0 ? genUnknown : genNonUnknown
               select new UnknownPositionScenario(ret, prm, loc);
    }

    public static Arbitrary<UnknownPositionScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    /// <summary>
    /// Property 1: Round-trip parity for `unknown` references.
    ///
    /// For any legal placement of `unknown` (bare, in type arguments, at
    /// any depth, in method return / parameter / local positions), the
    /// parse → pretty-print → parse → pretty-print pipeline SHALL produce
    /// identical source text on the second and third iterations.
    ///
    /// Validates: Requirement 14.4
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(UnknownTypeRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePrintPreservesUnknownInAllPositions(UnknownPositionScenario scenario)
    {
        // Precondition: the scenario mentions `unknown` somewhere. (Guaranteed
        // by GenScenario's mask >= 1 choice, but asserted explicitly so a
        // future change to the generator can't silently make the property
        // vacuous.)
        if(!scenario.MentionsUnknown)
        {
            return false.ToProperty().Label($"scenario did not mention unknown: {scenario}");
        }

        var source = scenario.ToSource();

        // Parse₁
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag1 = new DiagnosticBag();
        var tokens1 = Lexer.Lex(sourceText, bag1);
        var result1 = Parser.Parse(tokens1, bag1);
        var errors1 = bag1.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if(errors1.Count > 0)
        {
            return false.ToProperty()
                .Label($"first-parse errors for {scenario}: {string.Join("; ", errors1.Select(e => e.Message))}\nSource:\n{source}");
        }

        if(result1.Root is not CompilationUnitNode ast1)
        {
            return false.ToProperty().Label($"first-parse root was null for {scenario}\nSource:\n{source}");
        }

        // Print₁
        var printed1 = MistPrettyPrinter.Print(ast1, source);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label($"printed1 was empty for {scenario}");
        }

        if(!printed1.Contains("unknown"))
        {
            return false.ToProperty()
                .Label($"printed1 lost the `unknown` keyword for {scenario}\nSource:\n{source}\nPrinted:\n{printed1}");
        }

        // Parse₂
        var sourceText2 = new Mist.Common.Source.SourceText("test.mist", printed1);
        var bag2 = new DiagnosticBag();
        var tokens2 = Lexer.Lex(sourceText2, bag2);
        var result2 = Parser.Parse(tokens2, bag2);
        var errors2 = bag2.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if(errors2.Count > 0)
        {
            return false.ToProperty()
                .Label($"reparse errors for {scenario}: {string.Join("; ", errors2.Select(e => e.Message))}\nPrinted:\n{printed1}");
        }

        if(result2.Root is not CompilationUnitNode ast2)
        {
            return false.ToProperty().Label($"reparse root was null for {scenario}\nPrinted:\n{printed1}");
        }

        // Print₂
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"mismatch for {scenario}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}