using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ContextualExpressionContractReceiverTests
{
    [Theory]
    [InlineData("new")]
    [InlineData("default")]
    [InlineData("array")]
    public void APrimaryWithoutSuffixesRetainsTheDeclaredTargetFailure(string shape)
    {
        var f = new BindingFragments();
        PrimaryNode primary = shape switch
        {
            "new" => new TargetTypedNewExpressionPrimaryNode(),
            "default" => new DefaultLiteralExpressionPrimaryNode(),
            _ => new ArrayLiteralExpressionPrimaryNode(),
        };

        using(f.Binding.PushTargetType(ErrorType.Instance, isFailed: true))
        {
            _ = Assert.IsType<ErrorExpression>(f.Bind(primary));
            Assert.True(f.Binding.TargetTypeIsFailed);
            Assert.Empty(f.Diagnostics.ToImmutableArray());
        }

        Assert.Null(f.Binding.TargetType);
        Assert.False(f.Binding.TargetTypeIsFailed);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("return")]
    [InlineData("argument")]
    [InlineData("inferred")]
    public void AStringSwitchReceiverKeepsItsOwnTypeAtEachResultTarget(string site)
    {
        var f = Receiver();
        var expression = Length();
        Expression result;
        switch(site)
        {
            case "local":
                result = Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(new VariableDeclarationStatementStatementNode
                {
                    VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                    {
                        Type = BindingFragments.TypeName("Int32"),
                        Name = BindingFragments.Token("length"),
                        Initializer = expression,
                    },
                }, f.Binding, f.Builder)).Initializer!;
                break;
            case "return":
                f.Binding.ReturnType = f.Built.Type("Int32");
                result = Assert.IsType<ReturnStatement>(StatementBinder.BindStatement(new ReturnStatementStatementNode
                {
                    ReturnStatement = new ReturnStatementNode { Value = expression },
                }, f.Binding, f.Builder)).Value!;
                break;
            case "argument":
                var take = f.Function("Take", f.Built.Type("Int32"), f.Built.Type("Int32"));
                f.Scope.DeclareLocalFunction(new LocalFunctionDeclarationNode(), take, f.Diagnostics);
                result = Assert.Single(Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("Take"),
                    new InvocationSuffixPostfixSuffixNode { Arguments = BindingFragments.Arguments(expression) })).Arguments);
                break;
            default:
                result = ExpressionBinder.BindExpression(expression, f.Binding, f.Builder);
                break;
        }

        var member = Assert.IsType<MemberAccessExpression>(result);
        Assert.Same(f.Built.Type("Int32"), member.Type);
        var receiver = Assert.IsType<SwitchExpression>(member.Receiver);
        Assert.Same(f.Built.Type("String"), receiver.Type);
        Assert.All(receiver.Arms, arm => Assert.Same(f.Built.Type("String"), arm.Result.Type));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Null(f.Binding.TargetType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIntermediateSwitchDoesNotConsumeTheFinalMemberTarget(bool parenthesized)
    {
        var f = Receiver();
        f.Binding.TargetType = f.Built.Type("Int32");

        var result = parenthesized
            ? ExpressionBinder.BindExpression(Length(), f.Binding, f.Builder)
            : f.Bind(BindingFragments.Identifier("source"), Selection(Text("five"), Text("six")), Member("Length"));

        var member = Assert.IsType<MemberAccessExpression>(result);
        Assert.Same(f.Built.Type("String"), Assert.IsType<SwitchExpression>(member.Receiver).Type);
        Assert.Same(f.Built.Type("Int32"), member.Type);
        Assert.Same(f.Built.Type("Int32"), f.Binding.TargetType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ADirectlyTargetedSwitchKeepsItsDeclaredTargetThroughParentheses()
    {
        var f = Receiver();
        f.Binding.TargetType = f.Built.Type("Int64");
        var selection = BindingFragments.Expression(BindingFragments.Identifier("source"), Selection(Number("1"), Number("2")));

        var result = Assert.IsType<SwitchExpression>(f.Bind(Parenthesized(selection)));

        Assert.Same(f.Built.Type("Int64"), result.Type);
        Assert.Same(f.Built.Type("Int64"), f.Binding.TargetType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void NestedPostfixChainsResolveEachReceiverBeforeTheOuterTarget()
    {
        var f = Receiver();
        _ = BindingFragments.Member(f.Built.Type("Int32"), "Describe", f.Built.Type("String"));
        f.Binding.TargetType = f.Built.Type("String");

        var result = Assert.IsType<InvocationExpression>(f.Bind(Parenthesized(Length()),
            new MethodCallSuffixPostfixSuffixNode { Name = BindingFragments.Token("Describe") }));

        var callee = Assert.IsType<MemberAccessExpression>(result.Receiver);
        var length = Assert.IsType<MemberAccessExpression>(callee.Receiver);
        Assert.Same(f.Built.Type("Int32"), length.Type);
        Assert.Same(f.Built.Type("String"), Assert.IsType<SwitchExpression>(length.Receiver).Type);
        Assert.Same(f.Built.Type("String"), result.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("lambda")]
    [InlineData("method-group")]
    public void AParenthesizedCallableWithoutAnotherSuffixKeepsItsContextualSignature(string shape)
    {
        var f = new BindingFragments();
        var signature = f.Built.Interner.GetOrCreateFunctionType([f.Built.Type("String")], f.Built.Type("String"));
        f.Binding.TargetType = signature;
        ExpressionNode expression;
        if(shape == "lambda")
        {
            expression = new LambdaFormExpressionNode { Lambda = BindingFragments.Lambda(BindingFragments.Expression(BindingFragments.Identifier("x"))) };
        }
        else
        {
            var same = f.Function("Same", f.Built.Type("String"), f.Built.Type("String"));
            f.Scope.DeclareLocalFunction(new LocalFunctionDeclarationNode(), same, f.Diagnostics);
            expression = BindingFragments.Expression(BindingFragments.Identifier("Same"));
        }

        var result = f.Bind(Parenthesized(expression));

        Assert.Same(signature, result.Type);
        if(shape == "lambda")
        {
            _ = Assert.IsType<LambdaExpression>(result);
        }
        else
        {
            _ = Assert.IsType<FunctionReferenceExpression>(result);
        }

        Assert.Same(signature, f.Binding.TargetType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static BindingFragments Receiver()
    {
        var f = new BindingFragments();
        _ = f.Local("source", f.Built.Type("Int32"));
        f.Built.Type("String").PropertiesList.Add(new Property("Length", f.Built.Type("Int32"), DeclarationFragments.Public)
        {
            DeclaringType = f.Built.Type("String"),
        });
        return f;
    }

    private static ExpressionNode Length()
    {
        return BindingFragments.Expression(Parenthesized(BindingFragments.Expression(BindingFragments.Identifier("source"),
            Selection(Text("five"), Text("six")))), Member("Length"));
    }

    private static ParenthesizedExpressionPrimaryNode Parenthesized(ExpressionNode inner)
    {
        return new ParenthesizedExpressionPrimaryNode { Inner = inner };
    }

    private static MemberAccessSuffixPostfixSuffixNode Member(string name)
    {
        return new MemberAccessSuffixPostfixSuffixNode { Name = BindingFragments.Token(name) };
    }

    private static ExpressionNode Text(string text)
    {
        return BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token($"\"{text}\"") });
    }

    private static ExpressionNode Number(string value)
    {
        return BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token(value) });
    }

    private static SwitchExpressionSuffixPostfixSuffixNode Selection(params ExpressionNode[] results)
    {
        var arms = results.Select(result => new SwitchArmNode
        {
            Pattern = new PatternNode
            {
                OrPattern = new OrPatternNode
                {
                    Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = new DiscardPatternPrimaryPatternNode() } },
                },
            },
            Result = result,
        }).ToArray();
        return new SwitchExpressionSuffixPostfixSuffixNode
        {
            Arms = new SwitchArmListNode { SwitchArm = arms[0], SwitchArms = [.. arms.Skip(1)] },
        };
    }
}