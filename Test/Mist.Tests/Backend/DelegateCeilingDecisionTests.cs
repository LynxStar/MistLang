using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Backend;

// Tier 1: the two decisions behind MIST0028 (variadics Requirement 16), over directly built types
// and closings: which written function type is past the sixteen inputs a C# delegate holds, and which
// use site closed a pack to a width.
[Trait("Tier", "1")]
public sealed class DelegateCeilingDecisionTests
{
    private readonly HandBuiltModel built = new();
    private readonly TypeParameter pack = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
    private readonly TypeParameter inner = new("U", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    [Theory]
    [InlineData(17, 0, "function")]
    [InlineData(20, 0, "function")]
    [InlineData(17, 1, "function")]
    [InlineData(17, 0, "action")]
    [InlineData(17, 0, "optional")]
    [InlineData(17, 0, "tuple-element")]
    [InlineData(17, 0, "type-argument")]
    [InlineData(17, 0, "pack-element")]
    [InlineData(17, 0, "result-of-a-function")]
    [InlineData(17, 0, "parameter-of-a-function")]
    public void AFunctionWrittenWithMoreThanSixteenInputs_IsFoundWhereverItSits(int inputs, int spreads, string position)
    {
        var function = Function(inputs, spreads, position == "action" ? built.Type("Void") : built.Type("String"));

        Assert.Same(function, DelegateCeiling.Oversized(Placed(function, position)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(16, 0)]
    [InlineData(16, 1)]
    [InlineData(0, 3)]
    public void AFunctionOfSixteenWrittenInputsOrFewer_IsNot_HoweverManyAPackMayAdd(int inputs, int spreads)
    {
        Assert.Null(DelegateCeiling.Oversized(Function(inputs, spreads, built.Type("String"))));
    }

    [Fact]
    public void ATypeHoldingNoFunction_IsNot()
    {
        var wide = built.Interner.GetOrCreateTupleType([.. Enumerable.Repeat<IMistType>(built.Type("Int32"), 20)]);

        Assert.Null(DelegateCeiling.Oversized(wide));
    }

    [Fact]
    public void TheSiteIsTheFirstUseThatClosedThePackToThatWidth()
    {
        var (narrow, first, second) = (HandBuiltModel.Node(10), HandBuiltModel.Node(20), HandBuiltModel.Node(30));
        TypeArgumentClosing[] closings = [Closing(pack, narrow, 3), Closing(pack, first, 17), Closing(pack, second, 17)];

        Assert.Same(first, DelegateCeiling.InstantiationSite(closings, [pack], [17]));
        Assert.Same(narrow, DelegateCeiling.InstantiationSite(closings, [pack], [3]));
        Assert.Null(DelegateCeiling.InstantiationSite(closings, [pack], [16]));
    }

    [Fact]
    public void AWidthReachedThroughAnEnclosingPack_IsTracedToTheUseThatClosedThatOne()
    {
        var (forwarding, use) = (HandBuiltModel.Node(10), HandBuiltModel.Node(20));
        var forwarded = new LiftedType(built.Type("Type"), new TypePackValue([built.Type("String"), new SpreadType(pack)]));
        TypeArgumentClosing[] closings =
        [
            new(inner, forwarded, forwarding, TypeSubstitution.Empty),
            Closing(pack, use, 16),
        ];

        Assert.Same(use, DelegateCeiling.InstantiationSite(closings, [inner], [17]));
        Assert.Null(DelegateCeiling.InstantiationSite(closings, [inner], [16]));
    }

    [Fact]
    public void TwoPacksAreClosedAtOneSite()
    {
        var (together, apart) = (HandBuiltModel.Node(10), HandBuiltModel.Node(20));
        TypeArgumentClosing[] closings = [Closing(pack, apart, 9), Closing(pack, together, 9), Closing(inner, together, 8)];

        Assert.Same(together, DelegateCeiling.InstantiationSite(closings, [pack, inner], [9, 8]));
        Assert.Null(DelegateCeiling.InstantiationSite(closings, [pack, inner], [9, 9]));
    }

    private TypeArgumentClosing Closing(TypeParameter closed, SyntaxNode at, int width)
    {
        var value = new TypePackValue([.. Enumerable.Repeat<IMistType>(built.Type("Int32"), width)]);

        return new TypeArgumentClosing(closed, new LiftedType(built.Type("Type"), value), at, TypeSubstitution.Empty);
    }

    private FunctionType Function(int inputs, int spreads, IMistType result)
    {
        var parameters = Enumerable.Repeat<IMistType>(built.Type("Int32"), inputs)
            .Concat(Enumerable.Range(0, spreads).Select(_ => (IMistType)new SpreadType(pack)));

        return new FunctionType([.. parameters], result);
    }

    private IMistType Placed(FunctionType function, string position)
    {
        var text = built.Type("String");

        return position switch
        {
            "function" or "action" => function,
            "optional" => built.Interner.GetOrCreateUnionType([function, built.Type("Void")]),
            "tuple-element" => built.Interner.GetOrCreateTupleType([text, function]),
            "type-argument" => built.Interner.GetOrCreateConstructedType(built.Type("List<>"), [function]),
            "pack-element" => built.Interner.GetOrCreateConstructedType(
                built.Type("Tup<>"), [new LiftedType(built.Type("Type"), new TypePackValue([text, function]))]),
            "result-of-a-function" => new FunctionType([text], function),
            "parameter-of-a-function" => new FunctionType([function], text),
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };
    }
}