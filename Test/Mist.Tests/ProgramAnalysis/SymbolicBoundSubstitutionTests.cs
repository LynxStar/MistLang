using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: RefinementAnalyzer over hand-built calls whose parameter `where` names something other than a
// constant -- the receiver's Count, a sibling parameter, a dependent parameter the construction closes.
// Each argument sits at column 10 times its position plus one.
[Trait("Tier", "2")]
public sealed class SymbolicBoundSubstitutionTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly ClassType tally = HandBuiltModel.Class("Tally");

    [Theory]
    [InlineData("extension-receiver", "fixed", 2L, null)]
    [InlineData("extension-receiver", "fixed", 7L, "MIST0276")]
    [InlineData("extension-receiver", "open", 2L, "MIST0277")]
    [InlineData("this", "fixed", 2L, null)]
    [InlineData("this", "fixed", 7L, "MIST0276")]
    [InlineData("this", "open", 2L, "MIST0277")]
    [InlineData("captured-receiver", "fixed", 2L, "MIST0277")]
    [InlineData("parameter-named-like-the-receiver", "fixed", 2L, "MIST0277")]
    public void AReceiverMemberBound_IsReadOnlyThroughTheCalleesOwnReceiver(string receiver, string count, long argument, string? expected)
    {
        var block = new ExtensionBlock("self", "Probe", new ExtensionScope(null), null)
        {
            ResolvedReceiverType = tally,
            Receiver = new LocalVariable("self", tally),
        };
        var countProperty = new Property("Count", count == "fixed" ? h.Refine(h.Int, ComparisonKind.Equal, 3L, "Three") : h.Int, new MemberModifiers())
        {
            DeclaringType = tally,
            Getter = new PropertyAccessor(),
        };
        var other = new Parameter("self", tally);

        Expression through = receiver switch
        {
            "this" => new ThisExpression(HandBuiltModel.Node(), tally),
            "parameter-named-like-the-receiver" => HandBuiltRefinements.Read(new LocalVariable("self", tally) { Parameter = other }),
            _ => HandBuiltRefinements.Read(block.Receiver),
        };
        var bound = new MemberAccessExpression(HandBuiltModel.Node(), through, "Count", h.Int, member: new PropertyResult(countProperty, tally, []));
        var slot = Anonymous(h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L), h.Compare(h.Int, ComparisonKind.LessThan, bound));
        List<Parameter> parameters = receiver == "parameter-named-like-the-receiver" ? [other, new Parameter("n", slot)] : [new Parameter("n", slot)];

        var pick = new Method("Pick", h.Int, parameters, new MemberModifiers())
        {
            DeclaringType = receiver == "this" ? tally : null,
            DeclaringBlock = receiver is "this" or "captured-receiver" ? null : block,
        };
        List<Expression> arguments = receiver == "parameter-named-like-the-receiver"
            ? [HandBuiltRefinements.Local(tally, "t", offset: 0), h.Literal(argument, offset: 10)]
            : [h.Literal(argument, offset: 0)];

        Assert.Equal(expected, Single(Call(pick, arguments, [.. parameters.Select(parameter => parameter.Type)])));
    }

    [Theory]
    [InlineData(2L, 3L, null, 0)]
    [InlineData(5L, 3L, "MIST0276", 1)]
    [InlineData(3L, 3L, "MIST0276", 1)]
    [InlineData(1L, null, "MIST0277", 1)]
    public void ASiblingParameterBound_ReadsTheArgumentPassedForIt(long lo, long? hi, string? expected, int column)
    {
        var upper = new Parameter("hi", h.Int);
        var take = Take(upper, upper);
        Expression passed = hi is { } known ? h.Literal(known, offset: 10) : HandBuiltRefinements.Local(h.Int, "unknown", offset: 10);

        var findings = Call(take, [h.Literal(lo, offset: 0), passed], [.. take.ParametersList.Select(parameter => parameter.Type)]);

        Assert.Equal(expected, Single(findings));
        Assert.All(findings, finding => Assert.Equal(column, finding.Position().Start.Column));
    }

    [Fact]
    public void ASiblingIsMatchedByIdentityNotByName()
    {
        var take = Take(new Parameter("hi", h.Int), new Parameter("hi", h.Int));

        Assert.Equal(
            "MIST0277",
            Single(Call(take, [h.Literal(2L, offset: 0), h.Literal(3L, offset: 10)], [.. take.ParametersList.Select(parameter => parameter.Type)])));
    }

    [Theory]
    [InlineData("closed-three", 2L, null)]
    [InlineData("closed-three", 7L, "MIST0276")]
    [InlineData("closed-unknown", 2L, "MIST0277")]
    [InlineData("forwarded-at-least-five", 2L, null)]
    [InlineData("forwarded-at-least-five", 6L, "MIST0277")]
    [InlineData("forwarded-unconstrained", 2L, "MIST0277")]
    [InlineData("unclosed", 2L, "MIST0277")]
    public void ADependentBound_ReadsWhatTheConstructionClosesItWith(string closing, long argument, string? expected)
    {
        var buffer = HandBuiltModel.Class("Buf<>");
        var size = new TypeParameter("N", 0, buffer.Scope, buffer) { IsDependent = true, Domain = h.Built.Type("Int32") };
        var forwarded = new TypeParameter("K", 0, new Scope(null), null) { IsDependent = true, Domain = h.Built.Type("Int32") };

        if(closing == "forwarded-at-least-five")
        {
            forwarded.Refinement = h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 5L);
        }

        var declared = Anonymous(
            h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L),
            h.Compare(h.Int, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), size)));
        IMistType? value = closing switch
        {
            "closed-three" => new LiftedType(h.Int, 3L),
            "closed-unknown" => new UnknownValueType(h.Int),
            "unclosed" => null,
            _ => forwarded,
        };
        var target = value is null ? declared : new RefinedType(declared, declared.BaseType, new Dictionary<TypeParameter, IMistType> { [size] = value });
        var at = new Method("At", h.Int, [new Parameter("i", declared)], new MemberModifiers()) { DeclaringType = buffer };

        Assert.Equal(expected, Single(Call(at, [h.Literal(argument, offset: 0)], [target])));
    }

    private Method Take(Parameter upper, Parameter named)
    {
        var bound = HandBuiltRefinements.Read(new LocalVariable(named.Name, h.Int) { Parameter = named });
        return new Method(
            "Take", h.Int, [new Parameter("lo", Anonymous(h.Compare(h.Int, ComparisonKind.LessThan, bound))), upper], new MemberModifiers());
    }

    private RefinedType Anonymous(params Pattern[] operands)
    {
        var predicate = operands.Length == 1 ? operands[0] : new ConjunctionPattern(HandBuiltModel.Node(), h.Int, [.. operands]);
        return HandBuiltRefinements.RefinePattern(h.Int, predicate, name: null);
    }

    private IReadOnlyList<Diagnostic> Call(Method callee, List<Expression> arguments, IReadOnlyList<IMistType> parameterTypes)
    {
        var receiver = new MemberAccessExpression(HandBuiltModel.Node(90), HandBuiltRefinements.Local(tally, "t", offset: 90), callee.Name, h.Int);
        var call = new InvocationExpression(HandBuiltModel.Node(80), receiver, arguments, h.Int, callee, parameterTypes);

        return h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(HandBuiltRefinements.Evaluate(call)));
    }

    private static string? Single(IReadOnlyList<Diagnostic> findings)
    {
        return findings.Count == 0 ? null : Assert.Single(findings).Number.Value;
    }
}