using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// A tuple pattern over `(TArgs...)`, built as the binder builds it, emitted inside the specialization of
// each width (Tier 2); an element's spelling there is one function (Tier 1).
public sealed class PackTuplePatternEmissionTests
{
    private readonly HandBuiltModel built = new();
    private readonly TypeParameter pack = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(2, "args is (int a, int b)")]
    [InlineData(1, "args is (object and System.ValueTuple<T1, object>(int a, int b))")]
    [InlineData(3, "args is (object and System.ValueTuple<T1, T2>(int a, int b))")]
    [InlineData(0, "args is (object and (int a, int b))")]
    public void ATestAtAnotherWidth_StillDeclaresItsBindings(int width, string expected)
    {
        var integer = built.Type("Int32");
        var pattern = Over(Typed(integer, "a"), Typed(integer, "b"));

        Assert.Equal(expected, Emit(width, new IsPatternExpression(HandBuiltModel.Node(), Args(), pattern, built.Type("Boolean"))));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(3, "args is (var a, var b, var c)")]
    [InlineData(2, "args is (object and System.ValueTuple<T1, T2, object>(var a, var b, var c))")]
    public void AVarAtAnotherWidth_TakesTheTypeItsElementSpellsThere(int width, string expected)
    {
        var pattern = Over(Var(0, "a"), Var(1, "b"), Var(2, "c"));

        Assert.Equal(expected, Emit(width, new IsPatternExpression(HandBuiltModel.Node(), Args(), pattern, built.Type("Boolean"))));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(2, "args switch { (var _, var _) => 2 }")]
    [InlineData(1, "args switch { (object and System.ValueTuple<T1, object>(var _, var _)) => 2, var _ => 0 }")]
    public void ArmsAfterAnArmEveryValueMatches_AreLeftOutAtThatWidthOnly(int width, string expected)
    {
        var integer = built.Type("Int32");
        var arms = new[]
        {
            new SwitchArm(HandBuiltModel.Node(), Over(Discard(0), Discard(1)), null, HandBuiltModel.Literal(integer, 2)),
            new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), Args().Type, null), null, HandBuiltModel.Literal(integer, 0)),
        };

        Assert.Equal(expected, Emit(width, new SwitchExpression(HandBuiltModel.Node(), Args(), arms, integer)));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(2, 1, "T2")]
    [InlineData(1, 1, "object")]
    [InlineData(null, 1, "TArgs")]
    public void AnElementPastTheWidth_IsSpelledObject(int? width, int index, string expected)
    {
        using(width is { } fixedWidth ? Monomorph.For([pack], [fixedWidth])!.Enter() : null)
        {
            Assert.Equal(expected, TypeNameEmitter.Emit(pack.ElementAt(index)));
        }
    }

    private string Emit(int width, Expression expression)
    {
        using var specialization = Monomorph.For([pack], [width])!.Enter();
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitExpression(expression, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }

    private LocalReferenceExpression Args()
    {
        return HandBuiltRefinements.Local(built.Interner.GetOrCreateTupleType([new SpreadType(pack)]), "args");
    }

    private TuplePattern Over(params Pattern[] elements)
    {
        var tested = built.Interner.GetOrCreateTupleType([.. elements.Select((_, i) => (IMistType)pack.ElementAt(i))]);
        return new TuplePattern(HandBuiltModel.Node(), elements, tested, tested, binding: null) { OpenPack = pack };
    }

    private static TypeTestPattern Typed(IMistType tested, string name)
    {
        return new TypeTestPattern(HandBuiltModel.Node(), tested, new LocalVariable(name, tested));
    }

    private CatchAllPattern Var(int index, string name)
    {
        return new CatchAllPattern(HandBuiltModel.Node(), pack.ElementAt(index), new LocalVariable(name, pack.ElementAt(index)));
    }

    private CatchAllPattern Discard(int index)
    {
        return new CatchAllPattern(HandBuiltModel.Node(), pack.ElementAt(index), binding: null);
    }
}