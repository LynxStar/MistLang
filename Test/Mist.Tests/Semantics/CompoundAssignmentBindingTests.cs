using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class CompoundAssignmentBindingTests
{
    private readonly BindingFragments f = new();

    private ClassType Integer => f.Built.Type("Int32");

    private ClassType Wide => f.Built.Type("Int64");

    [Theory]
    [InlineData("+=", "Add")]
    [InlineData("-=", "Subtract")]
    [InlineData("*=", "Multiply")]
    [InlineData("/=", "Divide")]
    [InlineData("%=", "Modulo")]
    public void ACompoundOperatorSelectsTheBinaryOperatorAndYieldsTheStorageType(string spelling, string operation)
    {
        var kind = Enum.Parse<BinaryOperatorKind>(operation);
        _ = f.Local("total", Wide);

        var compound = Assert.IsType<CompoundAssignmentExpression>(Bind(Assign(Name("total"), Number(), spelling)));

        Assert.Equal(kind, compound.OperatorKind);
        Assert.Equal(IncrementForm.None, compound.IncrementForm);
        Assert.Same(Wide, compound.Type);
        Assert.Same(Wide, compound.ResultType);
        Assert.Same(Wide, Assert.IsType<LocalReferenceExpression>(compound.Target).Type);
        Assert.Same(Integer, compound.Value.Type);
        Assert.Null(compound.Operator);
        Assert.False(compound.GuardsRemainder);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("++", true, "Add")]
    [InlineData("--", true, "Subtract")]
    [InlineData("++", false, "Add")]
    [InlineData("--", false, "Subtract")]
    public void AnIncrementIsTheCompoundFormOverTheLiteralOne(string spelling, bool prefix, string operation)
    {
        var kind = Enum.Parse<BinaryOperatorKind>(operation);
        _ = f.Local("count", Wide);
        var operand = BindingFragments.Identifier("count");
        var written = prefix
            ? BindingFragments.Expression(new PrefixIncrementUnaryNode { Operator = BindingFragments.Token(spelling), Operand = MistAstGenerator.ExtractUnary(BindingFragments.Expression(operand)) })
            : BindingFragments.Expression(operand, new IncrementSuffixPostfixSuffixNode { Operator = BindingFragments.Token(spelling) });

        var compound = Assert.IsType<CompoundAssignmentExpression>(Bind(written));

        Assert.Equal(prefix ? IncrementForm.Prefix : IncrementForm.Postfix, compound.IncrementForm);
        Assert.Equal(kind, compound.OperatorKind);
        Assert.Equal(1L, Convert.ToInt64(Assert.IsType<LiteralExpression>(compound.Value).Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(Wide, compound.Type);
        Assert.Same(Wide, compound.ResultType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ADeclaredOperatorIsSelectedAndItsReturnIsTheResult()
    {
        var money = HandBuiltModel.Struct("Money");
        var plus = Declare(money, OperatorKind.Plus, money, money, Integer);
        _ = f.Local("wallet", money);

        var compound = Assert.IsType<CompoundAssignmentExpression>(Bind(Assign(Name("wallet"), Number(), "+=")));

        Assert.Same(plus, compound.Operator);
        Assert.Same(money, compound.ResultType);
        Assert.Equal(new IMistType[] { money, Integer }, compound.ParameterTypes);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AResultTheTargetCannotHoldIsKeptForTheAssignabilityOwner()
    {
        var money = HandBuiltModel.Struct("Money");
        var text = f.Built.Type("String");
        _ = Declare(money, OperatorKind.Slash, text, money, Integer);
        _ = f.Local("wallet", money);

        var compound = Assert.IsType<CompoundAssignmentExpression>(Bind(Assign(Name("wallet"), Number(), "/=")));

        Assert.Same(money, compound.Type);
        Assert.Same(text, compound.ResultType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AppendingToTextIsConcatenation()
    {
        var text = f.Built.Type("String");
        _ = f.Local("line", text);

        var compound = Assert.IsType<CompoundAssignmentExpression>(Bind(Assign(Name("line"), Number(), "+=")));

        Assert.Equal(BinaryOperatorKind.Add, compound.OperatorKind);
        Assert.Same(text, compound.ResultType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void IncrementingTextHasNoResult()
    {
        _ = f.Local("line", f.Built.Type("String"));

        var compound = Assert.IsType<CompoundAssignmentExpression>(Bind(BindingFragments.Expression(
            BindingFragments.Identifier("line"), new IncrementSuffixPostfixSuffixNode { Operator = BindingFragments.Token("++") })));

        _ = Assert.IsType<ErrorType>(compound.ResultType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("field")]
    [InlineData("property")]
    [InlineData("index")]
    public void EveryStorageShapeIsATarget(string shape)
    {
        var holder = HandBuiltModel.Class("Holder");
        holder.FieldsList.Add(new Field("Field", Wide, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = holder });
        holder.PropertiesList.Add(new Property("Property", Wide, DeclarationFragments.Public) { DeclaringType = holder });
        holder.IndexersList.Add(new Indexer(Wide, [new Parameter("position", Integer)], DeclarationFragments.Public) { DeclaringType = holder });
        _ = f.Local("holder", holder);
        var target = shape switch
        {
            "field" => BindingFragments.Expression(BindingFragments.Identifier("holder"), Member("Field")),
            "property" => BindingFragments.Expression(BindingFragments.Identifier("holder"), Member("Property")),
            _ => BindingFragments.Expression(BindingFragments.Identifier("holder"), new IndexSuffixPostfixSuffixNode { Index = Number() }),
        };

        var compound = Assert.IsType<CompoundAssignmentExpression>(Bind(Assign(target, Number(), "*=")));

        Assert.Same(Wide, compound.Target.Type);
        Assert.Same(Wide, compound.ResultType);
        if(shape == "index")
        {
            Assert.Same(holder.IndexersList[0], Assert.IsType<IndexExpression>(compound.Target).Indexer);
        }

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private Expression Bind(ExpressionNode expression)
    {
        return ExpressionBinder.BindExpression(expression, f.Binding, f.Builder);
    }

    private static ExpressionNode Name(string name)
    {
        return BindingFragments.Expression(BindingFragments.Identifier(name));
    }

    private static ExpressionNode Number()
    {
        return BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("2") });
    }

    private static AssignmentFormExpressionNode Assign(ExpressionNode target, ExpressionNode value, string operation)
    {
        var assignment = (AssignmentFormExpressionNode)target;
        assignment.Assignment.Group0 = new() { Operator = BindingFragments.Token(operation), Value = value };
        return assignment;
    }

    private static MemberAccessSuffixPostfixSuffixNode Member(string name)
    {
        return new MemberAccessSuffixPostfixSuffixNode { Name = BindingFragments.Token(name) };
    }

    private static Operator Declare(MemberType owner, OperatorKind kind, IMistType returns, params IMistType[] parameters)
    {
        var declared = new Operator(
            kind, returns, [.. parameters.Select((type, i) => new Parameter($"p{i}", type))], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
        };
        owner.OperatorsList.Add(declared);
        return declared;
    }
}