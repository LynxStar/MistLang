using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class DeclaredTypeFailureBindingTests
{
    [Theory]
    [InlineData("initialized")]
    [InlineData("uninitialized")]
    [InlineData("for")]
    [InlineData("with")]
    [InlineData("foreach")]
    [InlineData("function")]
    [InlineData("lambda")]
    public void DeclaredTypeRefusalSurvivesEveryParameterAndLocalConstruction(string site)
    {
        var f = new BindingFragments();
        var (local, _) = BindLocal(f, site, "Missing");

        Assert.Equal("r", local.Name);
        Assert.True(local.IsFailed);
        Assert.Same(ErrorType.Instance, local.Type);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 19), new LinePosition(1, 20)), diagnostic.Position());

        ReportOverload(f, local);
        _ = Assert.Single(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("initialized")]
    [InlineData("uninitialized")]
    [InlineData("for")]
    [InlineData("with")]
    [InlineData("foreach")]
    [InlineData("function")]
    [InlineData("lambda")]
    public void AnUnfinishedDeclaredTypeStillReportsItsOverloadRefusal(string site)
    {
        var f = new BindingFragments();
        f.Types.DeclareTopLevelType("Unfinished", ErrorType.Instance);
        var (local, _) = BindLocal(f, site, "Unfinished");

        Assert.False(local.IsFailed);
        Assert.Same(ErrorType.Instance, local.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        ReportOverload(f, local);
        Assert.Equal("MIST_PM_UNRESOLVABLE_OVERLOAD", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Theory]
    [InlineData("initialized")]
    [InlineData("uninitialized")]
    [InlineData("for")]
    [InlineData("with")]
    [InlineData("foreach")]
    [InlineData("function")]
    [InlineData("lambda")]
    public void AValidDeclaredTypeKeepsItsTypeWithoutFailure(string site)
    {
        var f = new BindingFragments();
        var (local, _) = BindLocal(f, site, "Int32");

        Assert.False(local.IsFailed);
        Assert.Same(f.Built.Type("Int32"), local.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void UnrelatedEarlierDiagnosticsDoNotMarkTheNextResolutionFailed()
    {
        var f = new BindingFragments();
        var failed = f.Builder.Resolver.ResolveWithFailure(WrittenType("Missing"), f.Scope);
        var valid = f.Builder.Resolver.ResolveWithFailure(WrittenType("Int32"), f.Scope);
        f.Types.DeclareTopLevelType("Unfinished", ErrorType.Instance);
        var unfinished = f.Builder.Resolver.ResolveWithFailure(WrittenType("Unfinished"), f.Scope);

        Assert.True(failed.IsFailed);
        Assert.False(valid.IsFailed);
        Assert.False(unfinished.IsFailed);
        _ = Assert.Single(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AFailedTypeInsideAConstructedFunctionRetainsItsPositiveEvidence()
    {
        var f = new BindingFragments();
        var arguments = BindingFragments.TypeArguments("Missing", "Int32");
        ((TypeReferenceTypeArgumentTypeArgumentNode)arguments.TypeArgument).Type = WrittenType("Missing");
        var node = new TypeReferenceNode
        {
            First = new FuncSingleTypeRefSingleTypeRefNode { TypeArgs = arguments },
        };

        var resolution = f.Builder.Resolver.ResolveWithFailure(node, f.Scope);

        Assert.True(resolution.IsFailed);
        var function = Assert.IsType<FunctionType>(resolution.Type);
        Assert.Same(ErrorType.Instance, Assert.Single(function.ParameterTypes));
        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void AReportedAliasTargetSurvivesLaterReferencesWithoutAnotherDiagnostic()
    {
        var f = new BindingFragments();
        var target = BindingFragments.At<RefinableSingleTypeExprNode>();
        target.Type = WrittenType("Missing").First;
        var declaration = BindingFragments.At<TypeAliasDeclarationNode>();
        declaration.Type = new RefinableTypeExpressionNode { First = target };
        var alias = new TypeAlias("Broken", "Probe", f.Types, null, declaration);
        new TypeAliasBuilder(f.Builder, f.Builder.Resolver).Build(alias);
        f.Register(alias);

        var resolution = f.Builder.Resolver.ResolveWithFailure(WrittenType("Broken"), f.Scope);

        Assert.True(resolution.IsFailed);
        Assert.Same(ErrorType.Instance, resolution.Type);
        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void AnUnreportedAliasErrorTypeRemainsUnfinished()
    {
        var f = new BindingFragments();
        var alias = new TypeAlias("Unfinished", "Probe", f.Types, null, BindingFragments.At<TypeAliasDeclarationNode>())
        {
            AliasedType = ErrorType.Instance,
        };
        f.Register(alias);

        var resolution = f.Builder.Resolver.ResolveWithFailure(WrittenType("Unfinished"), f.Scope);

        Assert.False(resolution.IsFailed);
        Assert.Same(ErrorType.Instance, resolution.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static TypeReferenceNode WrittenType(string name)
    {
        var named = BindingFragments.At<NamedSingleTypeRefSingleTypeRefNode>(18);
        named.Name = BindingFragments.Name(name);
        return new TypeReferenceNode { First = named };
    }

    private static (LocalVariable Local, TypeReferenceNode Type) BindLocal(BindingFragments f, string site, string name)
    {
        var type = WrittenType(name);
        _ = f.Local("source", f.Built.Type("Int32"));
        var initializer = BindingFragments.Expression(BindingFragments.Identifier("source"));
        var body = new BlockNode();
        var identifier = BindingFragments.Token("r");
        switch(site)
        {
            case "initialized":
                var declaration = new VariableDeclarationStatementStatementNode
                {
                    VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                    {
                        Type = type,
                        Name = identifier,
                        Initializer = initializer,
                    },
                };
                return (Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(declaration, f.Binding, f.Builder)).Local, type);

            case "uninitialized":
                var uninitialized = new UninitializedVariableStatementStatementNode
                {
                    UninitializedVariable = new UninitializedVariableDeclarationNode { Type = type, Name = identifier },
                };
                return (Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(uninitialized, f.Binding, f.Builder)).Local, type);

            case "for":
                var loop = new ForStatementStatementNode
                {
                    ForStatement = new ForStatementNode
                    {
                        Initializer = new ForInitializerVariableDeclarationForInitializerNode
                        {
                            VariableDeclaration = type,
                            Name = identifier,
                            Initializer = initializer,
                        },
                        Body = body,
                    },
                };
                var boundFor = Assert.IsType<ForStatement>(StatementBinder.BindStatement(loop, f.Binding, f.Builder));
                return (Assert.IsType<LocalDeclarationStatement>(boundFor.Initializer).Local, type);

            case "with":
                var with = new WithTypedDeclarationStatementStatementNode
                {
                    WithTypedDeclaration = new WithTypedDeclarationStatementNode { Type = type, Name = identifier, Initializer = initializer },
                };
                var boundWith = StatementBinder.BindWithDeclaration(body, with, [], f.Binding, f.Builder);
                return (Assert.IsType<WithBlockStatement>(boundWith).Binding!, type);

            case "foreach":
                var each = new ForeachStatementStatementNode
                {
                    ForeachStatement = new TypedForeachForeachStatementNode { Type = type, Name = identifier, Collection = initializer, Body = body },
                };
                return (Assert.IsType<ForEachStatement>(StatementBinder.BindStatement(each, f.Binding, f.Builder)).Variable, type);

            case "function":
                var function = new LocalFunctionDeclarationNode
                {
                    Name = BindingFragments.Token("F"),
                    ReturnType = BindingFragments.TypeName("Void"),
                    Parameters = new ParameterListNode { Parameter = new ParameterNode { Type = type, Name = identifier } },
                    Body = body,
                };
                LocalFunctionBinder.Declare([new LocalFunctionDeclarationStatementStatementNode { LocalFunction = function }], f.Binding, f.Builder);
                var boundFunction = Assert.IsType<LocalFunctionStatement>(LocalFunctionBinder.BindBody(function, f.Binding, f.Builder));
                return (Assert.Single(boundFunction.Parameters), type);

            case "lambda":
                var lambda = BindingFragments.Lambda(initializer, name);
                var parameter = ((ParenthesizedLambdaParamsLambdaParameterListNode)lambda.Params).Params!.LambdaParam;
                var typed = Assert.IsType<TypedLambdaParamLambdaParamNode>(parameter);
                typed.Type = type;
                typed.Name = identifier;
                var boundLambda = Assert.IsType<LambdaExpression>(LambdaBinder.Bind(lambda, f.Binding, f.Builder));
                return (Assert.Single(boundLambda.Parameters), type);

            default:
                throw new ArgumentOutOfRangeException(nameof(site));
        }
    }

    private static void ReportOverload(BindingFragments f, LocalVariable local)
    {
        var candidate = f.Function("Take", f.Built.Type("Void"), f.Built.Type("Int32"));
        var choice = new OverloadChoice<Method> { Selected = null, Unresolvable = [candidate] };
        OverloadFailure.Report(choice, [candidate], [new LocalReferenceExpression(BindingFragments.At<CompilationUnitNode>(), local)],
            "method 'Take'", BindingFragments.At<CompilationUnitNode>(), f.Diagnostics);
    }
}

[Trait("Tier", "1")]
public sealed class ParameterFailureBindingTests
{
    [Fact]
    public void AParameterCopiesOnlyTheRecordedTypeFailureIntoItsBodyLocal()
    {
        var failed = new Parameter("failed", ErrorType.Instance) { IsFailed = true };
        var unfinished = new Parameter("unfinished", ErrorType.Instance);

        Assert.True(BodyBinder.ParameterLocal(failed).IsFailed);
        Assert.False(BodyBinder.ParameterLocal(unfinished).IsFailed);
    }
}