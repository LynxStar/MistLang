using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OptionalLambdaOverloadTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CandidateMustAdmitTheActualBodyResult(bool block, bool alias)
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var signature = f.Built.Interner.GetOrCreateFunctionType([f.Built.Type("String")], f.Built.Type("String"));
        var target = Optional(f, signature);
        if(alias)
        {
            var definition = new TypeAlias("Callback", "Probe", f.Types, null, new TypeAliasDeclarationNode()) { AliasedType = target };
            target = f.Built.Interner.GetOrCreateConstructedType(definition, []);
        }

        var chosen = BindingFragments.Member(api, "Apply", f.Built.Type("String"), target);
        _ = BindingFragments.Member(api, "Apply", f.Built.Type("Boolean"), Optional(f, new FunctionType([f.Built.Type("String")], f.Built.Type("Boolean"))));

        var call = Assert.IsType<InvocationExpression>(Call(f, OptionalLambdaFragments.Lambda(block)));

        Assert.Same(chosen, call.Method);
        Assert.Same(signature, Assert.IsType<LambdaExpression>(Assert.Single(call.Arguments)).Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("result", false)]
    [InlineData("result", true)]
    [InlineData("stated-parameter", false)]
    [InlineData("arity", false)]
    [InlineData("local-pure", false)]
    [InlineData("body-error", false)]
    public void TrialRefusalsAreWithheld(string refusal, bool block)
    {
        var f = new OptionalLambdaFragments(pure: refusal == "local-pure");
        var body = refusal == "result"
            ? BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode())
            : BindingFragments.Expression(BindingFragments.Identifier(refusal == "body-error" ? "missing" : "x"));
        var syntax = OptionalLambdaFragments.Lambda(block, body, local: refusal == "local-pure");
        if(refusal == "stated-parameter")
        {
            syntax.Lambda = BindingFragments.Lambda(body, "Boolean");
        }
        else if(refusal == "arity")
        {
            ((ExpressionLambdaLambdaExpressionNode)syntax.Lambda).Params = new ParenthesizedLambdaParamsLambdaParameterListNode();
        }

        var trials = new LambdaTrials(f.Binding.Binding, f.Binding.Builder);
        var lambda = new UnboundLambdaExpression(syntax.Lambda);

        Assert.False(trials.Fits(lambda, f.Target));
        Assert.False(trials.Fits(lambda, f.Target));
        Assert.Empty(f.Binding.Diagnostics.ToImmutableArray());
        Assert.Same(f.Binding.Scope, f.Binding.Binding.Scope);
        Assert.Null(f.Binding.Binding.TargetType);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SameInputsCompareUsingTheInferredPresentResult(bool plainRight, bool pure)
    {
        var f = new BindingFragments();
        var input = f.Built.Type("String");
        var left = Optional(f, new FunctionType([input], input, pure));
        IMistType right = new FunctionType([input], f.Built.Type("Boolean"));
        if(!plainRight)
        {
            right = Optional(f, right);
        }

        var trials = new LambdaTrials(f.Binding, f.Builder);
        var lambda = new UnboundLambdaExpression(BindingFragments.Lambda(BindingFragments.Expression(BindingFragments.Identifier("x"))));

        var compared = Assert.IsType<FunctionType>(trials.ComparedType(lambda, left, right));

        Assert.Same(input, Assert.Single(compared.ParameterTypes));
        Assert.Same(input, compared.ReturnType);
        Assert.Equal(pure, compared.IsPure);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void PresentTargetKeepsTheExistingPreferenceOverOptionalWidening()
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var signature = new FunctionType([f.Built.Type("String")], f.Built.Type("String"));
        _ = BindingFragments.Member(api, "Apply", f.Built.Type("String"), Optional(f, signature));
        var chosen = BindingFragments.Member(api, "Apply", f.Built.Type("String"), signature);

        var call = Assert.IsType<InvocationExpression>(Call(f, OptionalLambdaFragments.Lambda(false)));

        Assert.Same(chosen, call.Method);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void DifferentInputsRemainAmbiguous()
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        _ = BindingFragments.Member(api, "Apply", f.Built.Type("Boolean"), Optional(f, new FunctionType([f.Built.Type("String")], f.Built.Type("Boolean"))));
        _ = BindingFragments.Member(api, "Apply", f.Built.Type("Boolean"), Optional(f, new FunctionType([f.Built.Type("Int32")], f.Built.Type("Boolean"))));

        _ = Call(f, OptionalLambdaFragments.Lambda(false, BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode())));

        Assert.Equal("MIST_PM_AMBIGUOUS_OVERLOAD", Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Fact]
    public void UniqueOtherArgumentCandidateReportsTheFinalLambdaDiagnostic()
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var target = Optional(f, new FunctionType([f.Built.Type("String")], f.Built.Type("String")));
        var chosen = BindingFragments.Member(api, "Apply", f.Built.Type("String"), f.Built.Type("String"), target);
        _ = BindingFragments.Member(api, "Apply", f.Built.Type("String"), f.Built.Type("Boolean"), target);

        var call = Assert.IsType<InvocationExpression>(Call(f,
            BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"text\"") }),
            OptionalLambdaFragments.Lambda(false, BindingFragments.Expression(BindingFragments.Identifier("missing")))));

        Assert.Same(chosen, call.Method);
        Assert.Equal("MIST_PM_UNRESOLVED_IDENTIFIER", Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Fact]
    public void CandidateTrialsDiscardCallsAndTheSelectedLambdaIsBoundOnce()
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var t = new TypeParameter("T", 0, api.Scope, null);
        var echo = BindingFragments.Member(api, "Echo", t);
        echo.TypeParametersList.Add(t);
        echo.ParametersList.Add(new Parameter("value", t));
        var target = Optional(f, new FunctionType([f.Built.Type("String")], f.Built.Type("String")));
        var chosen = BindingFragments.Member(api, "Apply", f.Built.Type("String"), target);
        _ = BindingFragments.Member(api, "Apply", f.Built.Type("Boolean"), Optional(f, new FunctionType([f.Built.Type("String")], f.Built.Type("Boolean"))));
        var body = BindingFragments.Expression(BindingFragments.Identifier("api"), new MethodCallSuffixPostfixSuffixNode
        {
            Name = BindingFragments.Token("Echo"),
            TypeArgs = BindingFragments.TypeArguments("String"),
            Arguments = BindingFragments.Arguments(BindingFragments.Expression(BindingFragments.Identifier("x"))),
        });
        var syntax = OptionalLambdaFragments.Lambda(false, body);
        var trials = new LambdaTrials(f.Binding, f.Builder);
        var lambda = new UnboundLambdaExpression(syntax.Lambda);

        Assert.True(trials.Fits(lambda, target));
        Assert.Same(f.Built.Type("String"), trials.ResultType(lambda, [f.Built.Type("String")]));
        Assert.Empty(f.Built.Model.GenericCalls.All());

        var call = Assert.IsType<InvocationExpression>(Call(f, syntax));

        Assert.Same(chosen, call.Method);
        var recorded = Assert.Single(f.Built.Model.GenericCalls.All());
        Assert.Same(echo, recorded.Method);
        Assert.Same(f.Built.Type("String"), Assert.Single(recorded.TypeArguments));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static IMistType Optional(BindingFragments f, IMistType signature)
    {
        return f.Built.Interner.GetOrCreateUnionType([signature, f.Built.Type("Void")]);
    }

    private static Expression Call(BindingFragments f, params ExpressionNode[] arguments)
    {
        return f.Bind(BindingFragments.Identifier("api"), new MethodCallSuffixPostfixSuffixNode
        {
            Name = BindingFragments.Token("Apply"),
            Arguments = BindingFragments.Arguments(arguments),
        });
    }
}