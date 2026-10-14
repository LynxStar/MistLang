using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FormattingCallbackDecisionTests
{
    private readonly FormattingFragments f = new();

    [Fact]
    public void AnInterpolationFormatsEachHole()
    {
        var hole = FormattingFragments.Value(f.Box);

        Assert.Same(hole, Assert.Single(FormattingCallbacks.Sites(f.Interpolate(hole))));
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public void AnAdditionFormatsBothOperandsOnlyWhenItBuildsAString(bool text, int formatted)
    {
        var type = text ? f.Text : f.Integer;
        var addition = new BinaryExpression(HandBuiltModel.Node(), FormattingFragments.Value(f.Box), BinaryOperatorKind.Add, FormattingFragments.Value(f.Box), type);

        Assert.Equal(formatted, FormattingCallbacks.Sites(addition).Count());
    }

    [Fact]
    public void ACallClosesTheReceiversParametersAndItsOwn()
    {
        var (holder, held, take, taken, _) = Holder();
        var receiver = FormattingFragments.Value(f.Constructed(holder, f.Box));
        var call = new InvocationExpression(
            HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Take", f.Text, [f.Calm]), [], f.Text, take, []);

        var use = Assert.Single(FormattingCallbacks.Uses(call, enclosing: null));

        Assert.Same(take, use.Callee);
        Assert.Same(f.Box, use.Closing.Apply(held, interner: null));
        Assert.Same(f.Calm, use.Closing.Apply(taken, interner: null));
    }

    [Fact]
    public void AGetterReadThroughAConstructedReceiverIsClosedByIt()
    {
        var (holder, held, _, _, shown) = Holder();
        var access = new MemberAccessExpression(
            HandBuiltModel.Node(), FormattingFragments.Value(f.Constructed(holder, f.Box)), "Shown", f.Box, member: new PropertyResult(shown, holder, []));

        var use = Assert.Single(FormattingCallbacks.Uses(access, enclosing: null));

        Assert.Same(shown.Getter, use.Callee);
        Assert.Same(f.Box, use.Closing.Apply(held, interner: null));
    }

    [Fact]
    public void AnIndexerReadThroughAConstructedReceiverIsClosedByIt()
    {
        var (shelf, slot) = f.GenericIndexed("Shelf");
        var read = new IndexExpression(
            HandBuiltModel.Node(), FormattingFragments.Value(f.Constructed(shelf, f.Box)), HandBuiltModel.Literal(f.Integer, 0L), f.Text)
        {
            Indexer = slot,
        };

        var use = Assert.Single(FormattingCallbacks.Uses(read, enclosing: null));

        Assert.Same(slot.Getter, use.Callee);
        Assert.Same(f.Box, use.Closing.Apply(shelf.TypeParametersList[0], interner: null));
    }

    [Fact]
    public void AnOperatorIsClosedByTheClosingItsSelectionRecorded()
    {
        var (shelf, _) = f.GenericIndexed("Shelf");
        var closed = f.Constructed(shelf, f.Box);
        var plus = new Operator(OperatorKind.Plus, closed, [new Parameter("a", closed), new Parameter("b", f.Integer)], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = shelf,
        };
        var addition = new BinaryExpression(HandBuiltModel.Node(), FormattingFragments.Value(f.Integer), BinaryOperatorKind.Add, FormattingFragments.Value(closed), closed)
        {
            Operator = plus,
            OperatorClosing = TypeSubstitution.ForReceiver(closed),
        };

        var use = Assert.Single(FormattingCallbacks.Uses(addition, enclosing: null));

        Assert.Same(plus, use.Callee);
        Assert.Same(f.Box, use.Closing.Apply(shelf.TypeParametersList[0], interner: null));
    }

    [Fact]
    public void AnInheritedOperatorIsClosedThroughTheBaseClauseItsSelectionRecorded()
    {
        var (shown, plus) = f.GenericOperator("Shown");
        var derived = f.Derived("Boxes", shown, f.Box);
        var addition = new BinaryExpression(HandBuiltModel.Node(), FormattingFragments.Value(derived), BinaryOperatorKind.Add, FormattingFragments.Value(derived), f.Text)
        {
            Operator = plus,
            OperatorClosing = TypeSubstitution.ForPath(null, [f.Constructed(shown, f.Box)], interner: null),
        };

        var use = Assert.Single(FormattingCallbacks.Uses(addition, enclosing: null));

        Assert.Same(plus, use.Callee);
        Assert.Same(f.Box, use.Closing.Apply(shown.TypeParametersList[0], interner: null));
    }

    [Fact]
    public void ACallThroughADerivedReceiverClosesTheBaseDeclaringIt()
    {
        var (shown, show) = f.GenericBase("Shown");
        var derived = f.Derived("Boxes", shown, f.Box);
        var call = new InvocationExpression(
            HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), FormattingFragments.Value(derived), "Show", f.Text), [], f.Text, show, []);

        var use = Assert.Single(FormattingCallbacks.Uses(call, enclosing: null));

        Assert.Same(f.Box, use.Closing.Apply(shown.TypeParametersList[0], interner: null));
    }

    [Fact]
    public void AnImplicitCallInsideADerivedTypeClosesTheBaseDeclaringIt()
    {
        var (shown, show) = f.GenericBase("Shown");
        var derived = f.Derived("Boxes", shown, f.Box);
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), show, []), [], f.Text, show, []);

        var use = Assert.Single(FormattingCallbacks.Uses(call, derived));

        Assert.Same(f.Box, use.Closing.Apply(shown.TypeParametersList[0], interner: null));
    }

    [Fact]
    public void ATypeParameterIsInScopeOfItsMethodAndOfMembersOfItsTypeOnly()
    {
        var (shown, show) = f.GenericBase("Shown");
        var read = f.Formats("Read", isReadonly: true);
        var own = read.TypeParametersList[0];
        var declared = shown.TypeParametersList[0];

        Assert.True(FormattingCallbacks.InScope(own, read, read.DeclaringType));
        Assert.True(FormattingCallbacks.InScope(declared, show, shown));
        Assert.False(FormattingCallbacks.InScope(declared, read, read.DeclaringType));
        Assert.False(FormattingCallbacks.InScope(own, show, shown));
    }

    [Fact]
    public void AFormattedTypeParameterIsOpen()
    {
        var parameter = new TypeParameter("T", 0, f.Program.Scope, null);

        var step = Assert.Single(new FormattingCallbacks().Reach(parameter));

        Assert.Same(parameter, step.Open);
    }

    [Fact]
    public void ANonGenericFormatterIsItsOwnToString()
    {
        var step = Assert.Single(new FormattingCallbacks().Reach(f.Box));

        Assert.Null(step.Open);
        Assert.Same(f.Box.MethodsList.Single(method => method.Name == "ToString"), step.Formatter);
    }

    [Fact]
    public void ADeclaredReadonlyGenericFormatterIsNotFollowed()
    {
        var step = Assert.Single(new FormattingCallbacks().Reach(f.Constructed(f.SealedWrapper, f.Box)));

        Assert.Same(f.SealedWrapper, DefinitionOf(step.Formatted));
    }

    [Theory]
    [InlineData("optional")]
    [InlineData("tuple")]
    public void AnOptionalsValueAndATuplesElementsAreFormattedInTurn(string shape)
    {
        IMistType formatted = shape == "optional" ? new UnionType([f.Box, f.Void]) : new TupleType([f.Integer, f.Box]);

        var step = Assert.Single(new FormattingCallbacks().Reach(formatted));

        Assert.Same(f.Box, step.Formatted);
    }

    [Fact]
    public void AnOptionalTypeParameterIsOpen()
    {
        var parameter = new TypeParameter("T", 0, f.Program.Scope, null);

        Assert.Same(parameter, Assert.Single(new FormattingCallbacks().Reach(new UnionType([parameter, f.Void]))).Open);
    }

    [Fact]
    public void AWiderUnionIsFormattedAsItsCarrierAndCallsNothing()
    {
        Assert.Empty(new FormattingCallbacks().Reach(new UnionType([f.Box, f.Integer])));
    }

    [Theory]
    [InlineData("int")]
    [InlineData("program")]
    public void ATypeThatIsNoMistFormatterFormatsNothingHere(string type)
    {
        Assert.Empty(new FormattingCallbacks().Reach(type == "int" ? f.Integer : f.Program));
    }

    private (ClassType Holder, TypeParameter Held, Method Take, TypeParameter Taken, Property Shown) Holder()
    {
        var holder = HandBuiltModel.Class("Holder");
        var held = new TypeParameter("H", 0, holder.Scope, holder);
        holder.TypeParametersList.Add(held);
        var take = new Method("Take", f.Text, [], default) { DeclaringType = holder };
        var taken = new TypeParameter("M", 0, holder.Scope, null);
        take.TypeParametersList.Add(taken);
        holder.MethodsList.Add(take);
        var shown = new Property("Shown", held, default) { Getter = new PropertyAccessor { IsAuto = true }, DeclaringType = holder };
        holder.PropertiesList.Add(shown);
        return (holder, held, take, taken, shown);
    }

    private static IMistType DefinitionOf(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }
}