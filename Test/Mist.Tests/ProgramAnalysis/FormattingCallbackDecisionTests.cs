using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
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

        var use = Assert.Single(FormattingCallbacks.Uses(call));

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

        var use = Assert.Single(FormattingCallbacks.Uses(access));

        Assert.Same(shown.Getter, use.Callee);
        Assert.Same(f.Box, use.Closing.Apply(held, interner: null));
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