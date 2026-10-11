using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ContextualExpressionIndexContractTests
{
    [Fact]
    public void ARefusedElementAccessOwnsItsDiagnosticAndRetainsBothInputs()
    {
        var f = new BindingFragments();
        var receiver = HandBuiltModel.Literal(f.Built.Type("Int32"), 5L);
        var node = Index();

        var bound = Assert.IsType<ErrorExpression>(IndexBinder.Bind(node, receiver, f.Binding, f.Builder));

        Assert.True(bound.IsReported);
        Assert.Same(receiver, bound.Children[0]);
        _ = Assert.IsType<LiteralExpression>(bound.Children[1]);
        Assert.True(PriorFailure.Of(bound));
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_RECEIVER_NOT_INDEXABLE", diagnostic.Id.Value);
        Assert.Contains("'int'", diagnostic.Message);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 46), new LinePosition(1, 47)), diagnostic.Location);
    }

    [Fact]
    public void ARefusedElementAccessDoesNotAddADerivedCallRefusal()
    {
        var f = new BindingFragments();
        var printer = HandBuiltModel.Class("Printer");
        _ = BindingFragments.Member(printer, "Take", f.Built.Type("Void"), f.Built.Type("Int32"));
        _ = BindingFragments.Member(printer, "Take", f.Built.Type("Void"), f.Built.Type("String"));
        _ = f.Local("printer", printer);
        var call = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>();
        call.Name = BindingFragments.Token("Take");
        call.Arguments = BindingFragments.Arguments(BindingFragments.Expression(Integer("5"), Index()));

        var bound = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.Identifier("printer"), call));

        Assert.True(bound.IsReported);
        Assert.Equal("MIST_PM_RECEIVER_NOT_INDEXABLE", Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
        Assert.Contains(bound.Children, child => child is ErrorExpression { Children.Count: 2 });
    }

    [Theory]
    [InlineData("receiver")]
    [InlineData("index")]
    public void APositivelyFailedInputKeepsItsOwnerAndBothChildren(string failed)
    {
        var f = new BindingFragments();
        var receiver = failed == "receiver"
            ? (Expression)new ErrorExpression(HandBuiltModel.Node())
            : HandBuiltModel.Literal(f.Built.Type("Int32"), 5L);
        _ = f.Local("broken", ErrorType.Instance, failed: true);
        var node = Index(failed == "index" ? BindingFragments.Expression(BindingFragments.Identifier("broken")) : null);

        var bound = Assert.IsType<ErrorExpression>(IndexBinder.Bind(node, receiver, f.Binding, f.Builder));

        Assert.Same(receiver, bound.Children[0]);
        Assert.Equal(2, bound.Children.Count);
        Assert.True(PriorFailure.Of(bound));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnUnfinishedReceiverStillNeedsTheExistingCallFallback()
    {
        var f = new BindingFragments();
        var receiver = new UnresolvedExpression(HandBuiltModel.Node());

        var bound = Assert.IsType<IndexExpression>(IndexBinder.Bind(Index(), receiver, f.Binding, f.Builder));

        _ = Assert.IsType<ErrorType>(bound.Type);
        Assert.False(PriorFailure.Of(bound));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var first = f.Function("Take", f.Built.Type("Void"), f.Built.Type("Int32"));
        var second = f.Function("Take", f.Built.Type("Void"), f.Built.Type("String"));
        var choice = OverloadResolution.Select(new[] { first, second }, [bound], null, null, f.Built.Interner);
        _ = OverloadFailure.Retain(choice, new[] { first, second }, [bound], "method 'Take'", HandBuiltModel.Node(), f.Diagnostics);
        Assert.Equal("MIST_PM_UNRESOLVABLE_OVERLOAD", Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("string")]
    [InlineData("user")]
    public void SupportedElementContractsKeepTheirSelectionAndConversion(string shape)
    {
        var f = new BindingFragments();
        var integer = f.Built.Type("Int32");
        var receiverType = shape == "string" ? f.Built.Type("String") : HandBuiltModel.Class("Widget");
        var indexer = new Indexer(integer, [new Parameter("position", integer)], DeclarationFragments.Public with { IsIntrinsic = shape == "string" })
        { DeclaringType = receiverType };
        receiverType.IndexersList.Add(indexer);
        var receiver = shape == "array"
            ? HandBuiltModel.Literal(f.Built.Interner.GetOrCreateConstructedType(f.Built.Type("Array<,>"), [integer, new UnknownValueType(integer)]))
            : HandBuiltModel.Literal(receiverType);
        _ = f.Local("position", f.Built.Type("Int16"));

        var bound = Assert.IsType<IndexExpression>(IndexBinder.Bind(
            Index(BindingFragments.Expression(BindingFragments.Identifier("position"))), receiver, f.Binding, f.Builder));

        Assert.Same(integer, bound.Type);
        Assert.Same(shape == "user" ? indexer : null, bound.Indexer);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        if(shape != "array")
        {
            Assert.Same(integer, bound.ParameterType);
            Assert.Same(f.Built.Type("Int16"), bound.Index.Type);
        }
    }

    private static IndexSuffixPostfixSuffixNode Index(ExpressionNode? value = null)
    {
        var node = BindingFragments.At<IndexSuffixPostfixSuffixNode>(45);
        node.Index = value ?? BindingFragments.Expression(Integer("0"));
        return node;
    }

    private static IntegerLiteralExpressionPrimaryNode Integer(string value)
    {
        var node = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        node.IntegerLiteral = BindingFragments.Token(value);
        return node;
    }
}