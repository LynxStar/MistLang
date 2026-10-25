using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ContextualExpressionContractAssignmentTests
{
    [Theory]
    [InlineData("local")]
    [InlineData("field")]
    [InlineData("property")]
    [InlineData("index")]
    public void NumericAssignmentProducesTheStorageTypeForEveryStorageShape(string shape)
    {
        var f = new BindingFragments();
        var storageType = f.Built.Type("Int64");
        var holder = HandBuiltModel.Class("Holder");
        holder.FieldsList.Add(new Field("Field", storageType, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = holder });
        holder.PropertiesList.Add(new Property("Property", storageType, DeclarationFragments.Public) { DeclaringType = holder });
        holder.IndexersList.Add(new Indexer(storageType, [new Parameter("position", f.Built.Type("Int32"))], DeclarationFragments.Public) { DeclaringType = holder });
        _ = f.Local("holder", holder);
        _ = f.Local("stored", storageType);
        var target = shape switch
        {
            "field" => BindingFragments.Expression(BindingFragments.Identifier("holder"), Member("Field")),
            "property" => BindingFragments.Expression(BindingFragments.Identifier("holder"), Member("Property")),
            "index" => BindingFragments.Expression(BindingFragments.Identifier("holder"), new IndexSuffixPostfixSuffixNode { Index = Number() }),
            _ => BindingFragments.Expression(BindingFragments.Identifier("stored")),
        };

        var assignment = Assert.IsType<AssignmentExpression>(ExpressionBinder.BindExpression(Assign(target, Number()), f.Binding, f.Builder));

        Assert.Same(storageType, assignment.Type);
        Assert.Same(storageType, assignment.Target.Type);
        Assert.Same(f.Built.Type("Int32"), assignment.Value.Type);
        if(shape == "index")
        {
            Assert.Same(holder.IndexersList[0], Assert.IsType<IndexExpression>(assignment.Target).Indexer);
        }

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Null(f.Binding.TargetType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeclaredImplicitConversionIsRetainedInsideTheStorageTypedAssignment(bool returnsDerived)
    {
        var f = new BindingFragments();
        var source = HandBuiltModel.Class("Source");
        var destination = HandBuiltModel.Class("Destination");
        var produced = returnsDerived ? HandBuiltModel.Class("Derived", destination) : destination;
        var conversion = HandBuiltModel.Conversion(source, source, produced);
        _ = f.Local("source", source);
        _ = f.Local("stored", destination);

        var assignment = Assert.IsType<AssignmentExpression>(ExpressionBinder.BindExpression(
            Assign(BindingFragments.Expression(BindingFragments.Identifier("stored")), BindingFragments.Expression(BindingFragments.Identifier("source"))),
            f.Binding, f.Builder));

        Assert.Same(destination, assignment.Type);
        var converted = Assert.IsType<ImplicitConversionExpression>(assignment.Value);
        Assert.Same(conversion, converted.Operator);
        Assert.Same(source, Assert.IsType<LocalReferenceExpression>(converted.Operand).Type);
        Assert.Same(produced, converted.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnIncompatibleValueKeepsItsTypeAndSourceForTheAssignabilityOwner()
    {
        var f = new BindingFragments();
        _ = f.Local("stored", f.Built.Type("Int64"));
        var value = BindingFragments.At<StringLiteralExpressionPrimaryNode>(47);
        value.StringLiteral = BindingFragments.Token("\"bad\"");

        var assignment = Assert.IsType<AssignmentExpression>(ExpressionBinder.BindExpression(
            Assign(BindingFragments.Expression(BindingFragments.Identifier("stored")), BindingFragments.Expression(value)), f.Binding, f.Builder));

        Assert.Same(f.Built.Type("Int64"), assignment.Type);
        Assert.Same(f.Built.Type("String"), assignment.Value.Type);
        Assert.Same(value, assignment.Value.Node);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void CoalescingAssignmentProducesItsPresentValueAndNarrowsLaterReads()
    {
        var f = new BindingFragments();
        var text = f.Built.Type("String");
        var optional = f.Built.Interner.GetOrCreateUnionType([text, f.Built.Type("Void")]);
        _ = f.Local("stored", optional);
        var value = BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"present\"") });
        var statement = Assert.IsType<ExpressionStatement>(StatementBinder.BindStatement(new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = Assign(BindingFragments.Expression(BindingFragments.Identifier("stored")), value, "??="),
            },
        }, f.Binding, f.Builder));

        var assignment = Assert.IsType<AssignmentExpression>(statement.Expression);
        Assert.True(assignment.IsCoalescing);
        Assert.Same(optional, assignment.Target.Type);
        Assert.Same(text, assignment.Type);
        var read = Assert.IsType<LocalReferenceExpression>(f.Bind(BindingFragments.Identifier("stored")));
        Assert.True(read.IsNarrowed);
        Assert.Same(text, read.Type);

        var reset = Assert.IsType<AssignmentExpression>(ExpressionBinder.BindExpression(
            Assign(BindingFragments.Expression(BindingFragments.Identifier("stored")), value), f.Binding, f.Builder));
        Assert.Same(optional, reset.Target.Type);
        Assert.Same(optional, reset.Type);
        Assert.False(Assert.IsType<LocalReferenceExpression>(reset.Target).IsNarrowed);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void APositivelyFailedAssignmentValueDoesNotAcquireAnOverloadComplaint()
    {
        var f = new BindingFragments();
        _ = f.Local("stored", f.Built.Type("Int64"));
        var take = f.Function("Take", f.Built.Type("Void"), f.Built.Type("Boolean"));
        f.Scope.DeclareLocalFunction(new LocalFunctionDeclarationNode(), take, f.Diagnostics);
        var assignment = Assign(BindingFragments.Expression(BindingFragments.Identifier("stored")), BindingFragments.Expression(BindingFragments.Identifier("missing")));

        var call = f.Bind(BindingFragments.Identifier("Take"), new InvocationSuffixPostfixSuffixNode { Arguments = BindingFragments.Arguments(assignment) });

        Assert.True(PriorFailure.Of(call));
        Assert.Equal("MIST_PM_UNRESOLVED_IDENTIFIER", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    private static ExpressionNode Number()
    {
        return BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") });
    }

    private static AssignmentFormExpressionNode Assign(ExpressionNode target, ExpressionNode value, string operation = "=")
    {
        var assignment = (AssignmentFormExpressionNode)target;
        assignment.Assignment.Group0 = new() { Operator = BindingFragments.Token(operation), Value = value };
        return assignment;
    }

    private static MemberAccessSuffixPostfixSuffixNode Member(string name)
    {
        return new MemberAccessSuffixPostfixSuffixNode { Name = BindingFragments.Token(name) };
    }
}