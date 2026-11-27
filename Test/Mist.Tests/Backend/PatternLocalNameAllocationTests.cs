using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class PatternLocalNameAllocationTests
{
    [Theory]
    [InlineData(false, "__discard_1")]
    [InlineData(true, "__discard_2")]
    public void OnlyBindingListElementsReserveTheDiscardCandidate(bool binds, string expected)
    {
        var integer = SizedSequenceTypes.IntegerType;
        var discard = new LocalVariable("_", integer) { IsDiscard = true };
        var binding = binds ? new LocalVariable("__discard_1", integer) : null;
        var body = HandBuiltModel.Block(
            new LocalDeclarationStatement(discard, HandBuiltModel.Literal(integer, 0L)),
            new IfStatement(MatchListElement(binding), HandBuiltModel.Block(), null, conditionBinds: binds));

        using var names = LocalNames.Enter(body, ["values"]);

        Assert.Equal(expected, LocalNames.Of(discard));
    }

    [Fact]
    public void ShadowingListBindingUsesTheSameAllocatedNameInItsDeclarationAndRead()
    {
        var integer = SizedSequenceTypes.IntegerType;
        var binding = new LocalVariable("item", integer);
        var condition = MatchListElement(binding);
        var read = new LocalReferenceExpression(HandBuiltModel.Node(), binding);
        var body = HandBuiltModel.Block(new IfStatement(condition,
            HandBuiltModel.Block(new ReturnStatement(read, integer)), null, conditionBinds: true));

        using var names = LocalNames.Enter(body, ["values", "item"]);
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        Assert.Equal("values is [var item_1]", BodyEmitter.Render(condition, diagnostics));
        Assert.Equal("item_1", BodyEmitter.Render(read, diagnostics));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private static IsPatternExpression MatchListElement(LocalVariable? binding)
    {
        var integer = SizedSequenceTypes.IntegerType;
        var array = SizedSequenceTypes.ArrayOf(integer, null);
        var values = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("values", array));
        var element = new CatchAllPattern(HandBuiltModel.Node(), integer, binding);
        var list = new ListPattern(HandBuiltModel.Node(), [new ListPatternElement(element, IsSlice: false, integer)], array);
        return new IsPatternExpression(HandBuiltModel.Node(), values, list, SizedSequenceTypes.BooleanType);
    }
}