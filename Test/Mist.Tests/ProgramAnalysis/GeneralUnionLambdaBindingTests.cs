using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class GeneralUnionLambdaBindingTests
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
    public void ADeclaredGeneralUnionTypesItsSoleCallableArm(bool block, bool includesVoid, bool pure)
    {
        var f = new BindingFragments();
        var (syntax, target, signature) = Target(f, includesVoid, pure);
        var statement = StatementBinder.BindStatement(new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = syntax,
                Name = BindingFragments.Token("transform"),
                Initializer = OptionalLambdaFragments.Lambda(block),
            },
        }, f.Binding, f.Builder);

        var local = Assert.IsType<LocalDeclarationStatement>(statement);
        var lambda = Assert.IsType<LambdaExpression>(local.Initializer);
        Assert.Same(target, local.Local.Type);
        Assert.Same(signature, lambda.Type);
        Assert.Same(signature, lambda.UnionCarrierDelegateType);
        Assert.Same(f.Built.Type("String"), Assert.Single(lambda.Parameters).Type);
        Assert.Equal(pure, ((IFunctionType)lambda.Type).IsPure);
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(lambda.Type, target, f.Built.Interner));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Null(f.Binding.TargetType);
    }

    [Fact]
    public void AConstructedAliasToTheGeneralUnionSuppliesTheSameSignature()
    {
        var f = new BindingFragments();
        var (_, target, signature) = Target(f, includesVoid: false, pure: false);
        var alias = new TypeAlias("MixedTransform", "Probe", f.Types, null, new TypeAliasDeclarationNode()) { AliasedType = target };
        f.Register(alias);
        var statement = StatementBinder.BindStatement(new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = BindingFragments.TypeName("MixedTransform"),
                Name = BindingFragments.Token("transform"),
                Initializer = OptionalLambdaFragments.Lambda(false),
            },
        }, f.Binding, f.Builder);

        var local = Assert.IsType<LocalDeclarationStatement>(statement);
        var lambda = Assert.IsType<LambdaExpression>(local.Initializer);
        Assert.Same(signature, lambda.Type);
        Assert.Same(signature, lambda.UnionCarrierDelegateType);
        Assert.Same(target, local.Local.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void MultipleCallableArmsSupplyNoSignatureEvenIfTheBodyWouldFitOnlyOne()
    {
        var f = new BindingFragments();
        var first = new FunctionType([f.Built.Type("String")], f.Built.Type("Boolean"));
        var second = new FunctionType([f.Built.Type("Int32")], f.Built.Type("String"));
        f.Binding.TargetType = new UnionType([first, second]);

        _ = Assert.IsType<ErrorExpression>(ExpressionBinder.BindExpression(
            OptionalLambdaFragments.Lambda(false, BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode())),
            f.Binding, f.Builder));

        Assert.Equal("MIST_PM_LAMBDA_PARAMETER_NO_TYPE", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void AStatedLambdaKeepsItsOwnDelegateTypeInAMultipleCallableUnion()
    {
        var f = new BindingFragments();
        var text = f.Built.Type("String");
        var target = new UnionType([
            new FunctionType([text], text),
            new FunctionType([f.Built.Type("Int32")], f.Built.Type("Int32")),
        ]);
        f.Binding.TargetType = target;
        var syntax = new LambdaFormExpressionNode
        {
            Lambda = BindingFragments.Lambda(BindingFragments.Expression(BindingFragments.Identifier("x")), "String"),
        };

        var lambda = Assert.IsType<LambdaExpression>(ExpressionBinder.BindExpression(syntax, f.Binding, f.Builder));

        Assert.Same(lambda.Type, lambda.UnionCarrierDelegateType);
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(lambda.Type, target, f.Built.Interner));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ALocalLambdaStillConflictsWithThePureArm()
    {
        var f = new BindingFragments();
        var (_, target, signature) = Target(f, includesVoid: false, pure: true);
        f.Binding.TargetType = target;

        var lambda = Assert.IsType<LambdaExpression>(ExpressionBinder.BindExpression(
            OptionalLambdaFragments.Lambda(false, local: true), f.Binding, f.Builder));

        Assert.Same(signature, lambda.Type);
        Assert.True(lambda.IsLocal);
        Assert.Equal("MIST_PM_PURE_AND_LOCAL", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void OverloadTrialsRejectTheIncompatibleCallableArm()
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var input = f.Built.Type("String");
        var chosen = BindingFragments.Member(api, "Apply", input,
            new UnionType([new FunctionType([input], input), f.Built.Type("Boolean")]));
        _ = BindingFragments.Member(api, "Apply", f.Built.Type("Boolean"),
            new UnionType([new FunctionType([input], f.Built.Type("Boolean")), f.Built.Type("Int32")]));

        var call = Assert.IsType<InvocationExpression>(Call(f, OptionalLambdaFragments.Lambda(false)));

        Assert.Same(chosen, call.Method);
        var lambda = Assert.IsType<LambdaExpression>(Assert.Single(call.Arguments));
        Assert.Same(input, ((IFunctionType)lambda.Type).ReturnType);
        _ = Assert.IsType<FunctionType>(lambda.UnionCarrierDelegateType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void GenericCallInfersFromTheOnlyCallableArm()
    {
        var f = new BindingFragments();
        var api = HandBuiltModel.Class("Api");
        _ = f.Local("api", api);
        var t = new TypeParameter("T", 0, api.Scope, null);
        var r = new TypeParameter("R", 1, api.Scope, null);
        var method = BindingFragments.Member(api, "Apply", r, t,
            new UnionType([new FunctionType([t], r), f.Built.Type("Boolean")]));
        method.TypeParametersList.Add(t);
        method.TypeParametersList.Add(r);

        var call = Assert.IsType<InvocationExpression>(Call(f,
            BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"seed\"") }),
            OptionalLambdaFragments.Lambda(false)));

        Assert.Same(method, call.Method);
        Assert.Same(f.Built.Type("String"), call.Type);
        var lambda = Assert.IsType<LambdaExpression>(call.Arguments[1]);
        Assert.Same(f.Built.Type("String"), ((IFunctionType)lambda.Type).ReturnType);
        _ = Assert.IsType<FunctionType>(lambda.UnionCarrierDelegateType);
        Assert.Equal([f.Built.Type("String"), f.Built.Type("String")], Assert.Single(f.Built.Model.GenericCalls.All()).TypeArguments);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AGeneralUnionValueIsNotCalledThroughItsCallableArm()
    {
        var f = new BindingFragments();
        var (_, target, _) = Target(f, includesVoid: false, pure: false);
        _ = f.Local("transform", target);

        _ = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.Identifier("transform"),
            new InvocationSuffixPostfixSuffixNode
            {
                Arguments = BindingFragments.Arguments(BindingFragments.Expression(new StringLiteralExpressionPrimaryNode
                {
                    StringLiteral = BindingFragments.Token("\"seed\""),
                })),
            }));

        Assert.Equal("MIST_PM_NOT_CALLABLE", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    private static (TypeReferenceNode Syntax, IMistType Target, FunctionType Signature) Target(BindingFragments f, bool includesVoid, bool pure)
    {
        var signature = f.Built.Interner.GetOrCreateFunctionType([f.Built.Type("String")], f.Built.Type("String"), pure);
        var syntax = new TypeReferenceNode
        {
            First = new FuncSingleTypeRefSingleTypeRefNode
            {
                TypeArgs = BindingFragments.TypeArguments("String", "String"),
                Pure = pure ? BindingFragments.Token("pure") : null,
            },
            SingleTypeRefs = includesVoid
                ? [BindingFragments.TypeName("Boolean").First, new VoidSingleTypeRefSingleTypeRefNode()]
                : [BindingFragments.TypeName("Boolean").First],
        };
        var target = f.Builder.Resolver.Resolve(syntax, f.Scope);
        return (syntax, target, signature);
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