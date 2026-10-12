using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: AssignabilityAnalyzer over hand-built calls to a callee with a `params` parameter (variadics
// Requirement 13.7 and its value-pack sibling). The callee is `Take(Int32 head, params <slot>, Boolean
// tail)`; each argument sits at column 10 times its position plus one, the call itself at column 91.
// The `list` slot is a hand-built collection class, enumerable with an Add of its element.
[Trait("Tier", "2")]
public sealed class FlattenedArgumentSiteTests
{
    private readonly HandBuiltModel input = new();
    private readonly TypeParameter pack = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
    private readonly ClassType list;

    public FlattenedArgumentSiteTests()
    {
        list = BuildList();
    }

    [Theory]
    [InlineData("array", "Int32, Int32, String, Int32, Boolean", 21, "'string' to 'int'")]
    [InlineData("array", "String, Int32, Boolean", 1, "'string' to 'int'")]
    [InlineData("array", "Int32, Int32, String", 21, "'string' to 'bool'")]
    [InlineData("array", "Int32, String[], Boolean", 11, "'string[]' to 'int[]'")]
    [InlineData("list", "Int32, Int32, String, Int32, Boolean", 21, "'string' to 'int'")]
    [InlineData("list", "Int32, Int32[], Boolean", 11, "'int[]' to 'List<int>'")]
    [InlineData("closed-pack", "Int32, Int32, Int32, Boolean", 21, "'int' to 'string'")]
    [InlineData("closed-pack", "String, Int32, String, Boolean", 1, "'string' to 'int'")]
    [InlineData("closed-pack", "Int32, Int32, Boolean", 11, "'(int)' to '(int, string)'")]
    [InlineData("closed-pack", "Int32, Boolean", 91, "'()' to '(int, string)'")]
    [InlineData("open-pack", "Int32, Int32, Int32, Boolean", 11, "'(int, int)' to '(TArgs...)'")]
    [InlineData("open-pack", "Int32, Boolean", 91, "'()' to '(TArgs...)'")]
    public void AnArgumentTheSlotCannotTake_IsRefusedWhereItIsWritten(string slot, string arguments, int column, string conversion)
    {
        var diagnostic = Assert.Single(Analyze(Call(slot, arguments)));

        Assert.Equal("MIST_PA_NO_IMPLICIT_CONVERSION", diagnostic.Id.Value);
        Assert.Equal(column, diagnostic.Location.Start.Column);
        Assert.Contains(conversion, diagnostic.Message);
    }

    [Theory]
    [InlineData("array", "Int32, Boolean")]
    [InlineData("array", "Int32, Int32, Int32, Int32, Boolean")]
    [InlineData("array", "Int32, Int32[], Boolean")]
    [InlineData("list", "Int32, Boolean")]
    [InlineData("list", "Int32, Int32, Int32, Boolean")]
    [InlineData("list", "Int32, List<Int32>, Boolean")]
    [InlineData("closed-pack", "Int32, Int32, String, Boolean")]
    [InlineData("open-pack", "Int32, ..., Boolean")]
    public void ACallThatFitsItsSlots_IsAdmitted(string slot, string arguments)
    {
        Assert.Empty(Analyze(Call(slot, arguments)));
    }

    [Fact]
    public void AnAbsorbedArgumentThatAlreadyFailed_IsNotReportedAgain()
    {
        Assert.Empty(Analyze(Call("open-pack", "Int32, error, Int32, Boolean")));
    }

    [Fact]
    public void ASpreadTheBinderKept_IsNotJudgedAgainstTheSlotItFills()
    {
        var spread = new SpreadType(pack);
        var forwarded = Argument("...", offset: 0);
        var call = new InvocationExpression(
            HandBuiltModel.Node(90), HandBuiltModel.Literal(input.Interner.GetOrCreateFunctionType([spread], input.Type("Int32")), offset: 95),
            [forwarded], input.Type("Int32"), null, [spread]);

        Assert.Empty(Analyze(call));
    }

    [Fact]
    public void AClrProjectedCallee_IsJudgedAsASourceCalleeIs()
    {
        var diagnostic = Assert.Single(Analyze(Call("array", "Int32, String, Boolean", isClrProjected: true)));

        Assert.Equal(11, diagnostic.Location.Start.Column);
        Assert.Contains("'string' to 'int'", diagnostic.Message);
    }

    [Fact]
    public void AConstructionIsJudgedAsACallIs()
    {
        var (callee, closed) = Callee("array", isClrProjected: false);
        var constructor = new Constructor("Holder", HandBuiltModel.Class("Holder"), callee.ParametersList, default);
        var creation = new NewExpression(
            HandBuiltModel.Node(90), HandBuiltModel.Class("Holder"), Arguments("Int32, String, Boolean"), constructor, [], closed);

        Assert.Equal(11, Assert.Single(Analyze(creation)).Location.Start.Column);
    }

    [Fact]
    public void AConstructorChainIsJudgedAsACallIs()
    {
        var (callee, closed) = Callee("array", isClrProjected: false);
        var owner = HandBuiltModel.Class("Holder");
        var chained = new Constructor("Holder", owner, callee.ParametersList, default);
        var chaining = new Constructor("Holder", owner, [], default) { ChainedConstructor = chained, ChainParameterTypes = closed };
        chaining.ChainArgumentsList.AddRange(Arguments("Int32, String, Boolean"));
        owner.ConstructorsList.Add(chaining);

        Assert.Equal(11, Assert.Single(input.Analyze(new AssignabilityAnalyzer(), types: [owner])).Location.Start.Column);
    }

    private IReadOnlyList<Diagnostic> Analyze(Expression root)
    {
        return input.Analyze(new AssignabilityAnalyzer(), root);
    }

    private InvocationExpression Call(string slot, string arguments, bool isClrProjected = false)
    {
        var (callee, closed) = Callee(slot, isClrProjected);

        return new InvocationExpression(
            HandBuiltModel.Node(90), HandBuiltModel.Literal(input.Type("Int32"), offset: 95), Arguments(arguments),
            input.Type("Int32"), callee, closed);
    }

    private (Method Callee, List<IMistType> Closed) Callee(string slot, bool isClrProjected)
    {
        var integer = input.Type("Int32");
        var declared = new TupleType([new SpreadType(pack)]);

        var (written, closed) = slot switch
        {
            "array" => (Array(integer), Array(integer)),
            "list" => (List(integer), List(integer)),
            "closed-pack" => (declared, input.Interner.GetOrCreateTupleType([integer, input.Type("String")])),
            "open-pack" => (declared, declared),
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

        var callee = new Method(
            "Take", integer,
            [new Parameter("head", integer), new Parameter("slot", written) { IsFlattened = true }, new Parameter("tail", input.Type("Boolean"))],
            new MemberModifiers { IsClrProjected = isClrProjected });

        return (callee, [integer, closed, input.Type("Boolean")]);
    }

    private List<Expression> Arguments(string arguments)
    {
        return [.. arguments.Split(", ").Select((written, i) => Argument(written, offset: i * 10))];
    }

    private Expression Argument(string written, int offset)
    {
        var value = HandBuiltModel.Literal(
            written switch
            {
                "..." => new TupleType([new SpreadType(pack)]),
                "error" => ErrorType.Instance,
                _ when written.EndsWith("[]", StringComparison.Ordinal) => Array(input.Type(written[..^2])),
                _ when written.StartsWith("List<", StringComparison.Ordinal) => List(input.Type(written[5..^1])),
                _ => input.Type(written),
            },
            offset: offset);

        return written == "..." ? new SpreadExpression(value.Node, value, value.Type) : value;
    }

    private IMistType Array(IMistType element)
    {
        return input.Interner.GetOrCreateConstructedType(
            input.Type("Array<,>"), [element, input.Interner.GetOrCreateUnknownValue(input.Type("Int32"))]);
    }

    private IMistType List(IMistType element)
    {
        return input.Interner.GetOrCreateConstructedType(list, [element]);
    }

    private ClassType BuildList()
    {
        var declared = new ClassType("List<>", "Standard", new Scope(null), null, null);
        var element = new TypeParameter("T", 0, declared.Scope, declared);
        declared.TypeParametersList.Add(element);
        declared.BaseAbstractTypes.Add(input.Interner.GetOrCreateConstructedType(
            new AbstractType("IEnumerable<>", "Standard", new Scope(null), null, null), [element]));
        declared.MethodsList.Add(new Method("Add", input.Type("Void"), [new Parameter("item", element)], default) { DeclaringType = declared });

        return declared;
    }
}