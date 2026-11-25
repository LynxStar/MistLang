using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class LambdaParameterDeclarationBindingTests
{
    [Theory]
    [InlineData("empty", 0)]
    [InlineData("pair", 2)]
    public void AContextualTargetOfAnotherArityIsRefusedWithBothCounts(string shape, int declared)
    {
        var f = new BindingFragments();
        var text = f.Built.Type("String");
        f.Binding.TargetType = f.Built.Interner.GetOrCreateFunctionType([text], text);
        var parameters = shape == "empty" ? Parenthesized() : Parenthesized(Untyped("a", 40), Untyped("b", 50));

        _ = Assert.IsType<ErrorExpression>(LambdaBinder.Bind(Lambda(parameters, Read("x")), f.Binding, f.Builder));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0359", diagnostic.Number.Value);
        Assert.Equal(new QuantityValue(1), diagnostic.Arguments[1]);
        Assert.Equal(new QuantityValue(declared), diagnostic.Arguments[2]);
        Assert.Same(f.Scope, f.Binding.Scope);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuppliedParameterTypesOfAnotherCountAreAnUnreportedError(int supplied)
    {
        var f = new BindingFragments();
        var types = Enumerable.Repeat(f.Built.Type("String"), supplied).ToList();

        _ = Assert.IsType<ErrorExpression>(LambdaBinder.BindWithParameters(Lambda(Implicit("x", 40), Read("x")), types, f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("supplied")]
    [InlineData("target")]
    public void AStatedTypeWinsAndAnUnstatedParameterTakesTheTypeAtItsOwnPosition(string source)
    {
        var f = new BindingFragments();
        var (text, flag, integer) = (f.Built.Type("String"), f.Built.Type("Boolean"), f.Built.Type("Int32"));
        var syntax = Lambda(Parenthesized(Typed("a", "Int32", 40), Untyped("b", 50)), Read("b"));

        Expression bound;
        if(source == "supplied")
        {
            bound = LambdaBinder.BindWithParameters(syntax, [text, flag], f.Binding, f.Builder);
        }
        else
        {
            f.Binding.TargetType = f.Built.Interner.GetOrCreateFunctionType([text, flag], flag);
            bound = LambdaBinder.Bind(syntax, f.Binding, f.Builder);
        }

        var lambda = Assert.IsType<LambdaExpression>(bound);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(["a", "b"], lambda.Parameters.Select(parameter => parameter.Name));
        Assert.Same(integer, lambda.Parameters[0].Type);
        Assert.Same(flag, lambda.Parameters[1].Type);
        Assert.Same(f.Built.Interner.GetOrCreateFunctionType([integer, flag], flag), lambda.Type);
    }

    [Theory]
    [InlineData("implicit", "x", 40)]
    [InlineData("list", "b", 50)]
    public void AnUnstatedParameterNothingTypesIsRefusedByNameAtThatParameter(string shape, string name, int offset)
    {
        var f = new BindingFragments();
        var parameters = shape == "implicit"
            ? Implicit("x", 40)
            : Parenthesized(Typed("a", "Int32", 40), Untyped("b", 50), Untyped("c", 60));

        _ = Assert.IsType<ErrorExpression>(LambdaBinder.Bind(Lambda(parameters, Read(name)), f.Binding, f.Builder));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0360", diagnostic.Number.Value);
        Assert.Equal(new NameValue(name), Assert.Single(diagnostic.Arguments));
        Assert.Equal(offset + 1, diagnostic.Position().Start.Column);
        Assert.Same(f.Scope, f.Binding.Scope);
    }

    [Fact]
    public void AParameterMayShadowAnEnclosingLocalAndTheBodyReadsTheParameter()
    {
        var f = new BindingFragments();
        var text = f.Built.Type("String");
        var outer = Assert.IsType<LocalReferenceExpression>(f.Local("x", f.Built.Type("Int32"))).Local;
        f.Binding.TargetType = f.Built.Interner.GetOrCreateFunctionType([text], text);

        var lambda = Assert.IsType<LambdaExpression>(LambdaBinder.Bind(Lambda(Implicit("x", 40), Read("x")), f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var parameter = Assert.Single(lambda.Parameters);
        Assert.NotSame(outer, parameter);
        Assert.Same(text, parameter.Type);
        Assert.Same(parameter, Assert.IsType<LocalReferenceExpression>(lambda.Result).Local);
    }

    [Theory]
    [InlineData("implicit")]
    [InlineData("mixed")]
    [InlineData("empty")]
    public void StatedParameterTypesResolveOnlyWhatIsWrittenInDeclarationOrder(string shape)
    {
        var f = new BindingFragments();
        var (parameters, expected) = shape switch
        {
            "implicit" => (Implicit("x", 40), new IMistType?[] { null }),
            "mixed" => (Parenthesized(Untyped("a", 40), Typed("b", "Int32", 50), Typed("c", "String", 60)),
                [null, f.Built.Type("Int32"), f.Built.Type("String")]),
            _ => (Parenthesized(), []),
        };

        var stated = LambdaBinder.StatedParameterTypes(Lambda(parameters, Read("x")), f.Binding, f.Builder);

        Assert.Equal(expected.Length, stated.Count);
        Assert.All(expected.Zip(stated), pair => Assert.Same(pair.First, pair.Second));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false, true, "valid")]
    [InlineData(false, true, "unbound-name")]
    [InlineData(false, true, "untyped-parameter")]
    [InlineData(true, false, "valid")]
    public void TheEnclosingAsyncAndLambdaContextIsRestoredAfterBinding(bool enclosing, bool asyncLambda, string body)
    {
        var f = new BindingFragments();
        var integer = f.Built.Type("Int32");
        f.Binding.IsAsyncContext = enclosing;
        f.Binding.InLambda = enclosing;
        if(body != "untyped-parameter")
        {
            f.Binding.TargetType = f.Built.Interner.GetOrCreateFunctionType([integer], integer);
        }

        var syntax = Lambda(Implicit("x", 40), Read(body == "unbound-name" ? "missing" : "x"));
        if(asyncLambda)
        {
            syntax.Modifiers = [new AsyncLambdaModifierLambdaModifierNode()];
        }

        var bound = LambdaBinder.Bind(syntax, f.Binding, f.Builder);

        Assert.Equal(body == "untyped-parameter", bound is ErrorExpression);
        Assert.Equal(body == "valid", f.Diagnostics.ToImmutableArray().IsEmpty);
        Assert.Equal(enclosing, f.Binding.IsAsyncContext);
        Assert.Equal(enclosing, f.Binding.InLambda);
        Assert.Same(f.Scope, f.Binding.Scope);
    }

    private static ExpressionLambdaLambdaExpressionNode Lambda(LambdaParameterListNode parameters, ExpressionNode body)
    {
        var lambda = BindingFragments.At<ExpressionLambdaLambdaExpressionNode>();
        lambda.Params = parameters;
        lambda.Body = body;
        return lambda;
    }

    private static ExpressionNode Read(string name)
    {
        return BindingFragments.Expression(BindingFragments.Identifier(name));
    }

    private static LambdaParameterListNode Implicit(string name, int offset)
    {
        var node = BindingFragments.At<SingleImplicitParamLambdaParameterListNode>(offset);
        node.Name = BindingFragments.Token(name);
        return node;
    }

    private static LambdaParameterListNode Parenthesized(params LambdaParamNode[] parameters)
    {
        return new ParenthesizedLambdaParamsLambdaParameterListNode
        {
            Params = parameters.Length == 0 ? null : new LambdaParamListNode { LambdaParam = parameters[0], LambdaParams = [.. parameters.Skip(1)] },
        };
    }

    private static UntypedLambdaParamLambdaParamNode Untyped(string name, int offset)
    {
        var node = BindingFragments.At<UntypedLambdaParamLambdaParamNode>(offset);
        node.Name = BindingFragments.Token(name);
        return node;
    }

    private static TypedLambdaParamLambdaParamNode Typed(string name, string type, int offset)
    {
        var node = BindingFragments.At<TypedLambdaParamLambdaParamNode>(offset);
        node.Name = BindingFragments.Token(name);
        node.Type = BindingFragments.TypeName(type);
        return node;
    }
}