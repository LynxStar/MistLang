using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OptionalLambdaGenericCallTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void InferredAndExplicitCallsBindTheSameClosedOptionalSignature(bool block, bool alias, bool pure)
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var method = Apply(f, api, seed: true, alias, pure);
        var syntax = OptionalLambdaFragments.Lambda(block, BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode()));
        var seed = BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"seed\"") });

        var inferred = Assert.IsType<InvocationExpression>(Call(f, null, seed, syntax));
        var explicitCall = Assert.IsType<InvocationExpression>(Call(f, BindingFragments.TypeArguments("String", "Boolean"), seed, syntax));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(method, inferred.Method);
        Assert.Same(f.Built.Type("Boolean"), inferred.Type);
        Assert.Same(explicitCall.Type, inferred.Type);
        Assert.Same(explicitCall.ParameterTypes![1], inferred.ParameterTypes![1]);
        var lambda = Assert.IsType<LambdaExpression>(inferred.Arguments[1]);
        var signature = Assert.IsType<FunctionType>(lambda.Type);
        Assert.Same(f.Built.Type("String"), Assert.Single(lambda.Parameters).Type);
        Assert.Same(f.Built.Type("Boolean"), signature.ReturnType);
        Assert.Equal(pure, signature.IsPure);
        Assert.Same(Assert.IsType<LambdaExpression>(explicitCall.Arguments[1]).Type, signature);
        Assert.Equal(2, f.Built.Model.GenericCalls.All().Count());
        Assert.All(f.Built.Model.GenericCalls.All(), entry => Assert.Equal([f.Built.Type("String"), f.Built.Type("Boolean")], entry.TypeArguments));
        Assert.Null(f.Binding.TargetType);
        Assert.Same(f.Scope, f.Binding.Scope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheContextuallyTypedParameterTeachesTheResult(bool block)
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        _ = Apply(f, api, seed: true);

        var call = Assert.IsType<InvocationExpression>(Call(f, null,
            BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"seed\"") }),
            OptionalLambdaFragments.Lambda(block)));

        Assert.Same(f.Built.Type("String"), call.Type);
        Assert.Equal([f.Built.Type("String"), f.Built.Type("String")], Assert.Single(f.Built.Model.GenericCalls.All()).TypeArguments);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void InferenceClosesEachOverloadBeforeItsOptionalLambdaTrial()
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var chosen = Apply(f, api, seed: true);
        var t = new TypeParameter("T", 0, api.Scope, null);
        var other = BindingFragments.Member(api, "Apply", f.Built.Type("String"), t,
            f.Built.Interner.GetOrCreateUnionType([new FunctionType([t], f.Built.Type("String")), f.Built.Type("Void")]));
        other.TypeParametersList.Add(t);

        var call = Assert.IsType<InvocationExpression>(Call(f, null,
            BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"seed\"") }),
            OptionalLambdaFragments.Lambda(false, BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode()))));

        Assert.Same(chosen, call.Method);
        var recorded = Assert.Single(f.Built.Model.GenericCalls.All());
        Assert.Same(chosen, recorded.Method);
        Assert.Equal([f.Built.Type("String"), f.Built.Type("Boolean")], recorded.TypeArguments);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatedLambdaParameterSuppliesTheOnlyInputBound(bool block)
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var method = Apply(f, api, seed: false);
        var body = BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode());
        var syntax = OptionalLambdaFragments.Lambda(block, body);
        var parameters = BindingFragments.Lambda(body, "String").Params;
        if(block)
        {
            ((BlockLambdaLambdaExpressionNode)syntax.Lambda).Params = parameters;
        }
        else
        {
            ((ExpressionLambdaLambdaExpressionNode)syntax.Lambda).Params = parameters;
        }

        var call = Assert.IsType<InvocationExpression>(Call(f, null, syntax));

        Assert.Same(method, call.Method);
        Assert.Same(f.Built.Type("Boolean"), call.Type);
        Assert.Same(f.Built.Type("String"), Assert.Single(Assert.IsType<LambdaExpression>(Assert.Single(call.Arguments)).Parameters).Type);
        Assert.Equal([f.Built.Type("String"), f.Built.Type("Boolean")], Assert.Single(f.Built.Model.GenericCalls.All()).TypeArguments);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("no-input", "MIST_PM_TYPE_ARGUMENTS_NOT_INFERRED")]
    [InlineData("conflicting-input", "MIST_PM_TYPE_ARGUMENTS_CONFLICT")]
    [InlineData("stated-conflict", "MIST_PM_TYPE_ARGUMENTS_CONFLICT")]
    [InlineData("body-error", "MIST_PM_UNRESOLVED_MEMBER")]
    public void InferenceRefusalsPreserveTheCauseWithoutTrialDiagnostics(string refusal, string diagnosticId)
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var method = Apply(f, api, seed: refusal != "no-input");
        var body = refusal == "body-error"
            ? BindingFragments.Expression(BindingFragments.Identifier("x"), new MemberAccessSuffixPostfixSuffixNode { Name = BindingFragments.Token("Missing") })
            : BindingFragments.Expression(BindingFragments.Identifier("x"));
        var syntax = new LambdaFormExpressionNode { Lambda = BindingFragments.Lambda(body, refusal == "stated-conflict" ? "Boolean" : null) };
        var arguments = new List<ExpressionNode>();
        if(refusal != "no-input")
        {
            arguments.Add(BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"seed\"") }));
        }

        if(refusal == "conflicting-input")
        {
            method.ParametersList.Insert(1, new Parameter("other", method.TypeParametersList[0]));
            arguments.Add(BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode()));
        }

        arguments.Add(syntax);

        _ = Call(f, null, [.. arguments]);

        Assert.Equal(diagnosticId, Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
        Assert.Null(f.Binding.TargetType);
        Assert.Same(f.Scope, f.Binding.Scope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsyncBodyInfersTheTaskResultInsideTheOptionalSignature(bool block)
    {
        var f = new BindingFragments();
        f.Binding.IsAsyncContext = true;
        var task = f.Built.Type("Task<>");
        task.TypeParametersList.Add(new TypeParameter("T", 0, task.Scope, task));
        f.Register(task);
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        _ = Apply(f, api, seed: true);
        var syntax = OptionalLambdaFragments.Lambda(block, BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode()));
        if(block)
        {
            ((BlockLambdaLambdaExpressionNode)syntax.Lambda).Modifiers = [new AsyncLambdaModifierLambdaModifierNode()];
        }
        else
        {
            ((ExpressionLambdaLambdaExpressionNode)syntax.Lambda).Modifiers = [new AsyncLambdaModifierLambdaModifierNode()];
        }

        var call = Assert.IsType<InvocationExpression>(Call(f, null,
            BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"seed\"") }), syntax));

        var lambda = Assert.IsType<LambdaExpression>(call.Arguments[1]);
        Assert.True(lambda.IsAsync);
        var signature = Assert.IsType<FunctionType>(lambda.Type);
        var result = Assert.IsType<ConstructedType>(signature.ReturnType);
        Assert.Same(task, result.Definition);
        Assert.Same(f.Built.Type("Boolean"), Assert.Single(result.TypeArguments));
        Assert.Same(result, Assert.Single(f.Built.Model.GenericCalls.All()).TypeArguments[1]);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.True(f.Binding.IsAsyncContext);
    }

    private static Method Apply(BindingFragments f, ClassType api, bool seed, bool alias = false, bool pure = false)
    {
        var t = new TypeParameter("T", 0, api.Scope, null);
        var r = new TypeParameter("R", 1, api.Scope, null);
        var method = BindingFragments.Member(api, "Apply", r);
        method.TypeParametersList.Add(t);
        method.TypeParametersList.Add(r);
        if(seed)
        {
            method.ParametersList.Add(new Parameter("seed", t));
        }

        var target = f.Built.Interner.GetOrCreateUnionType([new FunctionType([t], r, pure), f.Built.Type("Void")]);
        if(alias)
        {
            var definition = new TypeAlias("Callback<,>", "Probe", f.Types, null, new TypeAliasDeclarationNode());
            var input = new TypeParameter("Input", 0, definition.Scope, null);
            var output = new TypeParameter("Output", 1, definition.Scope, null);
            definition.TypeParametersList.Add(input);
            definition.TypeParametersList.Add(output);
            definition.AliasedType = new UnionType([new FunctionType([input], output, pure), f.Built.Type("Void")]);
            target = f.Built.Interner.GetOrCreateConstructedType(definition, [t, r]);
        }

        method.ParametersList.Add(new Parameter("callback", target));
        return method;
    }

    private static Expression Call(BindingFragments f, TypeArgumentListNode? typeArguments, params ExpressionNode[] arguments)
    {
        return f.Bind(BindingFragments.Identifier("api"), new MethodCallSuffixPostfixSuffixNode
        {
            Name = BindingFragments.Token("Apply"),
            TypeArgs = typeArguments,
            Arguments = BindingFragments.Arguments(arguments),
        });
    }
}