using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ImplicitConstructorArgumentBindingTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("static-constructor")]
    public void ArgumentsToASourceTypeDeclaringNoInstanceConstructor_AreRefusedAtTheSource(string shape)
    {
        var f = new BindingFragments();
        _ = Bag(f, shape);
        _ = f.Local("value", f.Built.Type("Int32"));

        var error = Assert.IsType<ErrorExpression>(Construct(f, Value("value")));

        Assert.True(error.IsReported);
        Assert.Null(Assert.IsType<NewExpression>(Assert.Single(error.Children)).Constructor);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_NO_APPLICABLE_OVERLOAD", diagnostic.Number.Value);
        Assert.Equal("There is no overload of constructor 'Bag' accepting 1 argument(s). Declared arities: 0.", diagnostic.Message);
        Assert.Equal(41, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("static-constructor")]
    public void AnEmptyArgumentList_SelectsTheImplicitParameterlessConstructor(string shape)
    {
        var f = new BindingFragments();
        _ = Bag(f, shape);

        var creation = Assert.IsType<NewExpression>(Construct(f));

        Assert.Null(creation.Constructor);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("ambient")]
    [InlineData("intrinsic")]
    [InlineData("clone")]
    public void ArgumentsToATypeWhoseConstructorsTheModelMayNotList_AreLeftToTheTarget(string shape)
    {
        var f = new BindingFragments();
        _ = Bag(f, shape);
        _ = f.Local("value", f.Built.Type("Int32"));

        var creation = Assert.IsType<NewExpression>(Construct(f, Value("value")));

        Assert.Null(creation.Constructor);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnArgumentThatAlreadyFailed_OwnsTheOnlyComplaint()
    {
        var f = new BindingFragments();
        _ = Bag(f, "class");
        _ = f.Local("broken", f.Built.Type("Int32"), failed: true);

        _ = Assert.IsType<ErrorExpression>(Construct(f, Value("broken")));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnObjectInitializerBesideTheArguments_AddsNoSecondComplaint()
    {
        var f = new BindingFragments();
        var bag = Bag(f, "class");
        bag.FieldsList.Add(new Field("Size", f.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = bag });
        _ = f.Local("value", f.Built.Type("Int32"));
        var creation = BindingFragments.At<ObjectCreationExpressionPrimaryNode>(40);
        creation.Type = BindingFragments.Name("Bag");
        creation.Arguments = BindingFragments.Arguments(Value("value"));
        var assignment = BindingFragments.At<NamedPropertyAssignmentPropertyAssignmentNode>(50);
        assignment.Name = BindingFragments.Token("Size");
        assignment.Value = Value("value");
        creation.Initializer = new PropertyInitializerBlockNode { Assignments = new PropertyAssignmentListNode { PropertyAssignment = assignment } };

        _ = Assert.IsType<ErrorExpression>(f.Bind(creation));

        Assert.Equal("MIST_PM_NO_APPLICABLE_OVERLOAD", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void ATargetTypedConstruction_IsRefusedTheSameWay()
    {
        var f = new BindingFragments();
        var bag = Bag(f, "class");
        _ = f.Local("value", f.Built.Type("Int32"));
        var creation = BindingFragments.At<TargetTypedNewExpressionPrimaryNode>(40);
        creation.Arguments = BindingFragments.Arguments(Value("value"));

        ErrorExpression error;
        using(f.Binding.PushTargetType(bag))
        {
            error = Assert.IsType<ErrorExpression>(f.Bind(creation));
        }

        Assert.True(error.IsReported);
        Assert.Equal("MIST_PM_NO_APPLICABLE_OVERLOAD", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    private static ConcreteType Bag(BindingFragments f, string shape)
    {
        ConcreteType bag = shape == "struct" ? HandBuiltModel.Struct("Bag") : HandBuiltModel.Class("Bag");

        switch(shape)
        {
            case "static-constructor":
                bag.ConstructorsList.Add(new Constructor("Bag", bag, [], DeclarationFragments.Public with { IsStatic = true }) { DeclaringType = bag });
                break;

            case "ambient":
                bag.Assembly = MistAssembly.Ambient;
                break;

            case "intrinsic":
                bag.Modifiers = DeclarationFragments.Public with { IsIntrinsic = true };
                break;

            case "clone":
                bag.CloneSource = HandBuiltModel.Class("Source");
                break;
        }

        f.Register(bag);
        return bag;
    }

    private static ExpressionNode Value(string name)
    {
        return BindingFragments.Expression(BindingFragments.Identifier(name));
    }

    private static Expression Construct(BindingFragments f, params ExpressionNode[] arguments)
    {
        var creation = BindingFragments.At<ObjectCreationExpressionPrimaryNode>(40);
        creation.Type = BindingFragments.Name("Bag");
        creation.Arguments = BindingFragments.Arguments(arguments);
        return f.Bind(creation);
    }
}