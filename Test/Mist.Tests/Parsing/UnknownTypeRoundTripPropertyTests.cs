// Feature: unknown-type, Property 1: Round-trip parity for `unknown` references
//
// Validates: Requirement 14.4
//
// Printed-text equality stands in for structural AST equality because the printer is deterministic
// and one-to-one on structure. The node-type assertions catch `unknown` collapsing into another node type.

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

    private static CompilationUnitNode AssertRoundTripPreserved(string source)
    {
        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);

        Assert.Contains("unknown", printed1);
        Assert.Contains("unknown", printed2);

        return ast1;
    }

    private static IReadOnlyList<TypeReferenceNode> GetTypeArgs(TypeArgumentListNode list)
    {
        var result = new List<TypeReferenceNode> { list.GetTypeReference() };
        result.AddRange(list.GetTypeReferences());
        return result;
    }

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
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        return Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
    }

    private static MethodDeclarationNode GetFirstMethod(CompilationUnitNode cu)
    {
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        return methodMember.Method;
    }

    [Fact]
    public void RoundTrip_BareUnknown_InLocalVariable()
    {
        const string source = "class T { public void M() { unknown x = 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var local = GetFirstLocalDecl(ast);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(local.Type.First);
    }

    [Fact]
    public void RoundTrip_Unknown_InListTypeArg()
    {
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
        const string source = "class T { public unknown M() { return 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var method = GetFirstMethod(ast);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(method.ReturnType.First);
    }

    [Fact]
    public void RoundTrip_Unknown_AsMethodParameter()
    {
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
        const string source = "class T { public List<unknown> M(Map<string, unknown> x) { return 0; } }";

        var ast = AssertRoundTripPreserved(source);

        var method = GetFirstMethod(ast);

        var retNamed = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(method.ReturnType.First);
        Assert.NotNull(retNamed.TypeArgs);
        var retArgs = GetTypeArgs(retNamed.TypeArgs!);
        _ = Assert.Single(retArgs);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(retArgs[0].First);

        Assert.NotNull(method.Parameters);
        var paramNamed = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(method.Parameters!.Parameter.Type.First);
        Assert.NotNull(paramNamed.TypeArgs);
        var paramArgs = GetTypeArgs(paramNamed.TypeArgs!);
        Assert.Equal(2, paramArgs.Count);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(paramArgs[1].First);
    }

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

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"mismatch for {scenario}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}