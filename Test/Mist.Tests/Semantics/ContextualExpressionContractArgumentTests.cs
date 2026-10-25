using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ContextualExpressionContractArgumentTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 3)]
    public void AClosedValuePackWithTheWrongWidthKeepsItsCallArityDiagnostic(int width, int count)
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Api");
        var pack = new TypeParameter("TArgs", 0, owner.Scope, owner) { PackKind = TypePackKind.Unbracketed };
        owner.TypeParametersList.Add(pack);
        var integer = f.Built.Type("Int32");
        var method = BindingFragments.Member(owner, "Pick", integer);
        method.ParametersList.Add(new Parameter("args", new TupleType([new SpreadType(pack)])) { IsFlattened = true });
        var lifted = new LiftedType(f.Built.Type("Type"), new TypePackValue(Enumerable.Repeat<IMistType>(integer, width).ToList()));
        var receiver = f.Built.Interner.GetOrCreateConstructedType(owner, [lifted]);
        _ = f.Local("api", receiver);

        _ = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.Identifier("api"), Call(Enumerable.Range(0, count).Select(_ => Integer()).ToArray())));

        Assert.Equal("MIST_PM_NO_APPLICABLE_OVERLOAD", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("default")]
    [InlineData("array")]
    [InlineData("parenthesized")]
    public void KnownArgumentChoosesTheContextAndExactDeclaration(string form)
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = f.Built.Type("Int32");
        var first = form == "array" ? f.Builder.Resolver.ConstructArray(thing, null) : thing;
        var second = form == "array" ? f.Builder.Resolver.ConstructArray(other, null) : other;
        var chosen = BindingFragments.Member(owner, "Pick", intType, first, intType);
        _ = BindingFragments.Member(owner, "Pick", intType, second, f.Built.Type("String"));
        _ = f.Local("api", owner);
        var call = Call(Contextual(form), Integer());

        var bound = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("api"), call));

        Assert.Same(chosen, bound.Method);
        if(form == "array")
        {
            Assert.Same(thing, Assert.IsType<ArrayLiteralExpression>(bound.Arguments[0]).ElementType);
        }
        else
        {
            Assert.Same(first, bound.Arguments[0].Type);
        }

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("new")]
    [InlineData("default")]
    [InlineData("array")]
    public void GenuineContextualTieHasOneCallDiagnostic(string form)
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = f.Built.Type("Int32");
        var first = form == "array" ? f.Builder.Resolver.ConstructArray(thing, null) : thing;
        var second = form == "array" ? f.Builder.Resolver.ConstructArray(other, null) : other;
        _ = BindingFragments.Member(owner, "Pick", intType, first, intType);
        _ = BindingFragments.Member(owner, "Pick", intType, second, intType);
        _ = f.Local("api", owner);

        _ = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.Identifier("api"), Call(Contextual(form), Integer())));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_AMBIGUOUS_OVERLOAD", diagnostic.Number.Value);
        Assert.Equal(51, diagnostic.Location.Start.Column);
    }

    [Fact]
    public void RejectedConstructorTrialDoesNotReportOrPoisonChosenNew()
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = f.Built.Type("Int32");
        var chosen = BindingFragments.Member(owner, "Pick", intType, thing);
        _ = BindingFragments.Member(owner, "Pick", intType, other);
        var constructor = Constructor(other, intType, intType);
        other.ConstructorsList.Add(constructor);
        _ = f.Local("api", owner);

        var bound = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("api"), Call(Contextual("new"))));

        Assert.Same(chosen, bound.Method);
        Assert.Same(thing, Assert.IsType<NewExpression>(bound.Arguments[0]).Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ConstructorConversionsParticipateInTheTrial()
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = f.Built.Type("Int32");
        thing.ConstructorsList.Add(Constructor(thing, intType));
        other.ConstructorsList.Add(Constructor(other, f.Built.Type("String")));
        var chosen = BindingFragments.Member(owner, "Pick", intType, thing);
        _ = BindingFragments.Member(owner, "Pick", intType, other);
        _ = f.Local("api", owner);
        var creation = BindingFragments.At<TargetTypedNewExpressionPrimaryNode>();
        creation.Arguments = BindingFragments.Arguments(Integer());

        var bound = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("api"), Call(BindingFragments.Expression(creation))));

        Assert.Same(chosen, bound.Method);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void UniqueKnownCandidateReportsItsOwnFailedConstruction()
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = f.Built.Type("Int32");
        thing.ConstructorsList.Add(Constructor(thing, intType));
        var chosen = BindingFragments.Member(owner, "Pick", intType, thing, intType);
        _ = BindingFragments.Member(owner, "Pick", intType, other, f.Built.Type("String"));
        _ = f.Local("api", owner);

        var bound = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("api"), Call(Contextual("new"), Integer())));

        Assert.Same(chosen, bound.Method);
        _ = Assert.IsType<ErrorExpression>(bound.Arguments[0]);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_NO_APPLICABLE_OVERLOAD", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Location.Start.Column);
    }

    [Fact]
    public void CandidateTrialRegistrationsAreDiscardedBeforeSettlement()
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = f.Built.Type("Int32");
        thing.ConstructorsList.Add(Constructor(thing, intType));
        other.ConstructorsList.Add(Constructor(other, intType));
        var chosen = BindingFragments.Member(owner, "Pick", intType, thing, intType);
        _ = BindingFragments.Member(owner, "Pick", intType, other, f.Built.Type("String"));
        var nested = BindingFragments.Member(owner, "Value", intType);
        _ = BindingFragments.Parameter(nested, "T");
        _ = f.Local("api", owner);
        var getValue = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>();
        getValue.Name = BindingFragments.Token("Value");
        getValue.TypeArgs = BindingFragments.TypeArguments("Int32");
        var creation = BindingFragments.At<TargetTypedNewExpressionPrimaryNode>();
        creation.Arguments = BindingFragments.Arguments(BindingFragments.Expression(BindingFragments.Identifier("api"), getValue));

        var bound = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("api"), Call(BindingFragments.Expression(creation), Integer())));

        Assert.Same(chosen, bound.Method);
        Assert.Same(nested, Assert.Single(f.Built.Model.GenericCalls.All()).Method);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void NewAloneDoesNotInferAnUnknownGenericParameter()
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        var owner = HandBuiltModel.Class("Api");
        var method = BindingFragments.Member(owner, "Pick", f.Built.Type("Int32"));
        var parameter = BindingFragments.Parameter(method, "T");
        method.ParametersList.Add(new Parameter("value", parameter));
        _ = f.Local("api", owner);

        _ = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.Identifier("api"), Call(Contextual("new"))));

        Assert.Equal("MIST_PM_TYPE_ARGUMENTS_NOT_INFERRED", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefFlagsStillDistinguishCandidatesWithContextualArguments(bool byRef)
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var intType = f.Built.Type("Int32");
        var valueMethod = BindingFragments.Member(owner, "Pick", intType, thing, intType);
        var refMethod = BindingFragments.Member(owner, "Pick", intType, thing);
        refMethod.ParametersList.Add(new Parameter("key", intType, new ParameterModifier { IsRef = true }));
        _ = f.Local("api", owner);
        _ = f.Local("key", intType);
        var call = Call(Contextual("new"), BindingFragments.Expression(BindingFragments.Identifier("key")));
        if(byRef)
        {
            call.Arguments!.Arguments = [new RefArgumentArgumentNode { Expression = BindingFragments.Expression(BindingFragments.Identifier("key")) }];
        }

        var bound = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("api"), call));

        Assert.Same(byRef ? refMethod : valueMethod, bound.Method);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ExpandedParamsStillTargetAndSelectEachArgument()
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = f.Built.Type("Int32");
        var chosen = BindingFragments.Member(owner, "Pick", intType, thing);
        chosen.ParametersList.Add(new Parameter("keys", f.Builder.Resolver.ConstructArray(intType, null)) { IsFlattened = true });
        var rejected = BindingFragments.Member(owner, "Pick", intType, other);
        rejected.ParametersList.Add(new Parameter("keys", f.Builder.Resolver.ConstructArray(f.Built.Type("String"), null)) { IsFlattened = true });
        _ = f.Local("api", owner);

        var bound = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("api"), Call(Contextual("new"), Integer(), Integer())));

        Assert.Same(chosen, bound.Method);
        Assert.Same(thing, bound.Arguments[0].Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorChainAdapterPreservesTheSelectedTarget(bool isBase)
    {
        var f = new BindingFragments();
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var owner = HandBuiltModel.Class("Container");
        var derived = HandBuiltModel.Class("Derived", owner);
        var intType = f.Built.Type("Int32");
        var chosen = Constructor(owner, thing, intType);
        owner.ConstructorsList.Add(chosen);
        owner.ConstructorsList.Add(Constructor(owner, other, f.Built.Type("String")));
        var args = BindingFragments.Arguments(Contextual("new"), Integer());
        var current = new Constructor("Current", isBase ? derived : owner, [], DeclarationFragments.Public)
        {
            DeclaringType = isBase ? derived : owner,
            Scope = f.Scope,
            Body = HandBuiltModel.Block(),
            ChainNode = isBase
                ? new BaseChainConstructorChainNode { Arguments = args }
                : new ThisChainConstructorChainNode { Arguments = args },
        };

        BodyBinder.BindDelegate(current, f.Builder);

        Assert.Same(chosen, current.ChainedConstructor);
        Assert.Same(thing, Assert.IsType<NewExpression>(current.ChainArgumentsList[0]).Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static Constructor Constructor(ClassType owner, params IMistType[] types)
    {
        return new Constructor(owner.Name, owner, types.Select((type, i) => new Parameter($"p{i}", type)).ToList(), DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Scope = owner.Scope,
        };
    }

    private static MethodCallSuffixPostfixSuffixNode Call(params ExpressionNode[] arguments)
    {
        var call = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>(50);
        call.Name = BindingFragments.Token("Pick");
        call.Arguments = BindingFragments.Arguments(arguments);
        return call;
    }

    private static ExpressionNode Integer()
    {
        return BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") });
    }

    private static ExpressionNode Contextual(string form)
    {
        return BindingFragments.Expression(form switch
        {
            "default" => BindingFragments.At<DefaultLiteralExpressionPrimaryNode>(),
            "array" => BindingFragments.At<ArrayLiteralExpressionPrimaryNode>(),
            "parenthesized" => new ParenthesizedExpressionPrimaryNode { Inner = Contextual("new") },
            _ => BindingFragments.At<TargetTypedNewExpressionPrimaryNode>(),
        });
    }
}

[Trait("Tier", "1")]
public sealed class ContextualExpressionContractSelectionTests
{
    [Fact]
    public void KnownArgumentSelectsExactCandidateWithUntypedFirstArgument()
    {
        var built = new HandBuiltModel();
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = built.Type("Int32");
        var chosen = new Method("Pick", intType, [new Parameter("x", thing), new Parameter("k", intType)], new MemberModifiers());
        var rejected = new Method("Pick", intType, [new Parameter("x", other), new Parameter("k", built.Type("String"))], new MemberModifiers());
        var deferred = new UnboundContextualExpression(BindingFragments.Expression(new TargetTypedNewExpressionPrimaryNode()));

        var choice = OverloadResolution.Select([chosen, rejected], [deferred, HandBuiltModel.Literal(intType, 1)], null, null, built.Interner,
            contextual: new AcceptsContexts());

        Assert.Same(chosen, choice.Selected);
    }

    private sealed class AcceptsContexts : IContextualExpressionTrials
    {
        public bool Fits(UnboundContextualExpression expression, IMistType target)
        {
            return true;
        }
    }
}

[Trait("Tier", "1")]
public sealed class ContextualExpressionContractRetainedTrialTests
{
    [Fact]
    public void AnUnsettledContextualArgumentIsAnAnalysisLeaf()
    {
        var expression = new UnboundContextualExpression(BindingFragments.Expression(new TargetTypedNewExpressionPrimaryNode()));

        Assert.Empty(Mist.ProgramAnalysis.BodyWalker.ChildrenOf(expression));
    }
}