using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class FailedOverloadRetentionBindingTests
{
    [Theory]
    [InlineData("call")]
    [InlineData("constructor")]
    [InlineData("index")]
    public void AnUnjudgedArgumentSurvivesFailedSelectionWithItsOwnSourceAndDeferredComplaint(string path)
    {
        var f = new BindingFragments();
        var owner = OverloadedOwner(f);
        _ = f.Local("owner", owner);
        _ = f.Local("unknown", UnknownType.Instance);
        var argument = UnknownMember("Length", 11);

        var error = Assert.IsType<ErrorExpression>(Bind(f, path, argument));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.False(error.IsReported);
        var inputs = path == "constructor"
            ? Assert.IsType<NewExpression>(Assert.Single(error.Children)).Arguments
            : error.Children;
        var access = Assert.Single(inputs.OfType<MemberAccessExpression>());
        Assert.Equal("Length", access.MemberName);
        Assert.Equal(11, access.Node.Span.Start);
        Assert.Equal(UnknownType.Instance, access.Receiver.Type);
        if(path != "constructor")
        {
            var receiver = path == "call"
                ? Assert.IsType<LocalReferenceExpression>(Assert.IsType<MethodGroupExpression>(error.Children[0]).Receiver)
                : Assert.IsType<LocalReferenceExpression>(error.Children[0]);
            Assert.Equal("owner", receiver.Local.Name);
            Assert.Same(access, error.Children[1]);
        }

        var diagnostic = Assert.IsType<Diagnostic>(error.DeferredDiagnostic);
        Assert.Equal("MIST0390", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 41), new LinePosition(1, 42)), diagnostic.Position());
    }

    [Fact]
    public void AFailedReceiverStillRetainsIndependentUnjudgedArguments()
    {
        var f = new BindingFragments();
        _ = f.Local("owner", OverloadedOwner(f), failed: true);
        _ = f.Local("unknown", UnknownType.Instance);

        var error = Assert.IsType<ErrorExpression>(Bind(f, "call", UnknownMember("Length", 11)));

        var receiver = Assert.IsType<LocalReferenceExpression>(Assert.IsType<MethodGroupExpression>(error.Children[0]).Receiver);
        Assert.True(receiver.Local.IsFailed);
        Assert.Equal("Length", Assert.IsType<MemberAccessExpression>(error.Children[1]).MemberName);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AFailedReceiverSuppressesTheConsequentFailureOverSoundArguments()
    {
        var f = new BindingFragments();
        _ = f.Local("owner", OverloadedOwner(f), failed: true);
        _ = f.Local("value", f.Built.Type("Double"));

        var error = Assert.IsType<ErrorExpression>(Bind(f, "call", BindingFragments.Expression(BindingFragments.Identifier("value"))));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.True(error.IsReported);
        Assert.Null(error.DeferredDiagnostic);
        Assert.Equal(2, error.Children.Count);
        Assert.True(Assert.IsType<LocalReferenceExpression>(Assert.IsType<MethodGroupExpression>(error.Children[0]).Receiver).Local.IsFailed);
        Assert.Same(f.Built.Type("Double"), error.Children[1].Type);
    }

    [Fact]
    public void ALocalArgumentRetainsItsUnjudgedInitializerWithoutClaimingAPriorRefusal()
    {
        var f = new BindingFragments();
        _ = f.Local("owner", OverloadedOwner(f));
        var source = new MemberAccessExpression(HandBuiltModel.Node(11), HandBuiltModel.Literal(UnknownType.Instance), "Length", ErrorType.Instance);
        var local = new LocalVariable("value", ErrorType.Instance) { FailureSource = source };
        f.Scope.DeclareLocal(local, source.Node, f.Diagnostics);

        var error = Assert.IsType<ErrorExpression>(Bind(f, "call", BindingFragments.Expression(BindingFragments.Identifier("value"))));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.False(error.IsReported);
        Assert.Equal("MIST0390", Assert.IsType<Diagnostic>(error.DeferredDiagnostic).Number.Value);
        Assert.Same(local, Assert.IsType<LocalReferenceExpression>(error.Children[1]).Local);
        Assert.Same(source, local.FailureSource);
        Assert.False(local.IsFailed);
    }

    [Fact]
    public void AFailedConstructorChainRetainsItsArgumentsAndDeferredDiagnostic()
    {
        var f = new BindingFragments();
        var owner = OverloadedOwner(f);
        _ = f.Local("unknown", UnknownType.Instance);
        var chain = BindingFragments.At<ThisChainConstructorChainNode>(40);
        chain.Arguments = BindingFragments.Arguments(UnknownMember("Length", 11));
        var constructor = new Constructor("Owner", owner, [], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Scope = f.Scope,
            Body = HandBuiltModel.Block(),
            ChainKind = ConstructorChainKind.This,
            ChainNode = chain,
        };

        BodyBinder.BindDelegate(constructor, f.Builder);

        Assert.Null(constructor.ChainedConstructor);
        var error = Assert.IsType<ErrorExpression>(Assert.Single(constructor.ChainArgumentsList));
        Assert.Equal("Length", Assert.IsType<MemberAccessExpression>(Assert.Single(error.Children)).MemberName);
        Assert.Equal("MIST0390", Assert.IsType<Diagnostic>(error.DeferredDiagnostic).Number.Value);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AFailedCollectionAddRetainsItsElementAndDeferredDiagnostic()
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Collection");
        _ = BindingFragments.Member(owner, "Add", f.Built.Type("Void"), f.Built.Type("Int32"));
        _ = BindingFragments.Member(owner, "Add", f.Built.Type("Void"), f.Built.Type("String"));
        _ = f.Local("unknown", UnknownType.Instance);
        var element = BindingFragments.At<CollectionElementAssignmentPropertyAssignmentNode>(40);
        element.Value = UnknownMember("Length", 11);
        var block = new PropertyInitializerBlockNode
        {
            Assignments = new PropertyAssignmentListNode { PropertyAssignment = element },
        };

        var assignment = Assert.Single(ObjectInitializerBinder.Bind(block, owner, f.Binding, f.Builder));

        Assert.Null(assignment.Add);
        var error = Assert.IsType<ErrorExpression>(assignment.Value);
        Assert.Equal("Length", Assert.IsType<MemberAccessExpression>(Assert.Single(error.Children)).MemberName);
        var diagnostic = Assert.IsType<Diagnostic>(error.DeferredDiagnostic);
        Assert.Equal("MIST0390", diagnostic.Number.Value);
        Assert.Equal(41, diagnostic.Position().Start.Column);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AReportedArgumentDoesNotDiscardAnIndependentUnknownMemberArgument()
    {
        var f = new BindingFragments();
        var owner = OverloadedOwner(f, arity: 2);
        _ = f.Local("owner", owner);
        _ = f.Local("unknown", UnknownType.Instance);
        var missing = BindingFragments.Identifier("missing");

        var error = Assert.IsType<ErrorExpression>(Bind(f, "call", BindingFragments.Expression(missing), UnknownMember("Length", 11)));

        _ = Assert.IsType<ErrorExpression>(error.Children[1]);
        Assert.Equal("Length", Assert.IsType<MemberAccessExpression>(error.Children[2]).MemberName);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0296", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("arity", "MIST0385")]
    [InlineData("ambiguity", "MIST0386")]
    public void SoundSelectionFailuresAreReportedImmediately(string failure, string diagnosticId)
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Owner");
        if(failure == "arity")
        {
            _ = BindingFragments.Member(owner, "Take", f.Built.Type("Void"), f.Built.Type("Int32"), f.Built.Type("Int32"));
            _ = f.Local("value", f.Built.Type("Int32"));
        }
        else
        {
            _ = BindingFragments.Member(owner, "Take", f.Built.Type("Void"), f.Built.Type("UInt32"));
            _ = BindingFragments.Member(owner, "Take", f.Built.Type("Void"), f.Built.Type("Int16"));
            _ = f.Local("value", f.Built.Type("Byte"));
        }

        _ = f.Local("owner", owner);
        var error = Assert.IsType<ErrorExpression>(Bind(f, "call", BindingFragments.Expression(BindingFragments.Identifier("value"))));

        Assert.True(error.IsReported);
        Assert.Null(error.DeferredDiagnostic);
        Assert.Equal(2, error.Children.Count);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal(41, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AnUnfinishedErrorTypedArgumentHasNoAssumedFailureOwner()
    {
        var f = new BindingFragments();
        _ = f.Local("owner", OverloadedOwner(f));
        _ = f.Local("value", ErrorType.Instance);

        var error = Assert.IsType<ErrorExpression>(Bind(f, "call", BindingFragments.Expression(BindingFragments.Identifier("value"))));

        Assert.True(error.IsReported);
        Assert.Equal("MIST0390", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
        Assert.False(Assert.IsType<LocalReferenceExpression>(error.Children[1]).Local.IsFailed);
    }

    [Theory]
    [InlineData("function-arity", "MIST0362")]
    [InlineData("non-callable", "MIST0363")]
    public void IndependentValueCallFailuresRetainTheReceiverAndUnjudgedArgument(string failure, string expected)
    {
        var f = new BindingFragments();
        var integer = f.Built.Type("Int32");
        IMistType receiverType = failure == "function-arity"
            ? f.Built.Interner.GetOrCreateFunctionType([integer, integer], f.Built.Type("Void"))
            : integer;
        _ = f.Local("callback", receiverType);
        _ = f.Local("unknown", UnknownType.Instance);
        var suffix = BindingFragments.At<InvocationSuffixPostfixSuffixNode>(40);
        suffix.Arguments = BindingFragments.Arguments(UnknownMember("Length", 11));

        var error = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.Identifier("callback"), suffix));

        Assert.True(error.IsReported);
        Assert.Null(error.DeferredDiagnostic);
        Assert.Same(suffix, error.Node);
        Assert.Equal(2, error.Children.Count);
        var receiver = Assert.IsType<LocalReferenceExpression>(error.Children[0]);
        Assert.Equal("callback", receiver.Local.Name);
        Assert.Same(receiverType, receiver.Type);
        var argument = Assert.IsType<MemberAccessExpression>(error.Children[1]);
        Assert.Equal("Length", argument.MemberName);
        Assert.Equal(11, argument.Node.Span.Start);
        Assert.Same(UnknownType.Instance, argument.Receiver.Type);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(expected, diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 41), new LinePosition(1, 42)), diagnostic.Position());
    }

    [Fact]
    public void AFailedConstructorDoesNotSettleAnUnboundLambdaAsAPriorDiagnostic()
    {
        var f = new BindingFragments();
        _ = OverloadedOwner(f, arity: 2);
        _ = f.Local("unfinished", ErrorType.Instance);
        _ = f.Local("number", f.Built.Type("Int32"));
        var binary = MistAstGenerator.MakeAdditive(
            BindingFragments.Expression(BindingFragments.Identifier("unfinished")),
            BindingFragments.Expression(BindingFragments.Identifier("number")));
        var lambda = new LambdaFormExpressionNode
        {
            Lambda = BindingFragments.Lambda(BindingFragments.Expression(BindingFragments.Identifier("number"))),
        };

        var error = Assert.IsType<ErrorExpression>(Bind(f, "constructor", binary, lambda));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.NotNull(error.DeferredDiagnostic);
        var creation = Assert.IsType<NewExpression>(Assert.Single(error.Children));
        _ = Assert.IsType<BinaryExpression>(creation.Arguments[0]);
        _ = Assert.IsType<UnboundLambdaExpression>(creation.Arguments[1]);
        Assert.All(creation.Arguments, argument => Assert.False(PriorFailure.Of(argument)));
    }

    private static ClassType OverloadedOwner(BindingFragments f, int arity = 1)
    {
        var owner = HandBuiltModel.Class("Owner");
        foreach(var type in new[] { f.Built.Type("Int32"), f.Built.Type("String") })
        {
            _ = BindingFragments.Member(owner, "Take", f.Built.Type("Void"), Enumerable.Repeat(type, arity).ToArray());
            owner.ConstructorsList.Add(new Constructor("Owner", owner,
                Enumerable.Range(0, arity).Select(i => new Parameter($"p{i}", type)).ToList(), DeclarationFragments.Public)
            {
                DeclaringType = owner,
                Scope = owner.Scope,
            });
            owner.IndexersList.Add(new Indexer(f.Built.Type("Int32"), [new Parameter("index", type)], DeclarationFragments.Public)
            {
                DeclaringType = owner,
            });
        }

        f.Register(owner);
        return owner;
    }

    private static ExpressionNode UnknownMember(string member, int offset)
    {
        var suffix = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>(offset);
        suffix.Name = BindingFragments.Token(member);
        return BindingFragments.Expression(BindingFragments.Identifier("unknown"), suffix);
    }

    private static Expression Bind(BindingFragments f, string path, params ExpressionNode[] arguments)
    {
        switch(path)
        {
            case "constructor":
                var creation = BindingFragments.At<ObjectCreationExpressionPrimaryNode>(40);
                creation.Type = BindingFragments.Name("Owner");
                creation.Arguments = BindingFragments.Arguments(arguments);
                return f.Bind(creation);
            case "index":
                var index = BindingFragments.At<IndexSuffixPostfixSuffixNode>(40);
                index.Index = Assert.Single(arguments);
                return f.Bind(BindingFragments.Identifier("owner"), index);
            default:
                var call = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>(40);
                call.Name = BindingFragments.Token("Take");
                call.Arguments = BindingFragments.Arguments(arguments);
                return f.Bind(BindingFragments.Identifier("owner"), call);
        }
    }
}