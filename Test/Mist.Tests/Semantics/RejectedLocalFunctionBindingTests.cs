using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class RejectedLocalFunctionBindingTests
{
    [Fact]
    public void TheDeclarationOwnsItsUnboundTypesAndEveryReferenceToTheRejectedSignature()
    {
        var f = new BindingFragments();
        var node = Function("Same", "T", "T", true,
            Declare("value", "T", BindingFragments.Expression(BindingFragments.Identifier("x"))),
            Evaluate(BindingFragments.Expression(BindingFragments.Identifier("T"))),
            Return(BindingFragments.Expression(BindingFragments.Identifier("value"))));
        LocalFunctionBinder.Declare([Statement(node)], f.Binding, f.Builder);

        var body = Assert.IsType<LocalFunctionStatement>(LocalFunctionBinder.BindBody(node, f.Binding, f.Builder));
        var local = Assert.IsType<LocalDeclarationStatement>(body.Body.Statements[0]).Local;
        var typeValue = Assert.IsType<ExpressionStatement>(body.Body.Statements[1]).Expression;
        var genericCall = f.Bind(GenericCall("Same", "Int32", Literal("2")));
        var plainCall = f.Bind(BindingFragments.Identifier("Same"), Invocation(Literal("2")));
        var reference = f.Bind(BindingFragments.Identifier("Same"));
        var statedReference = f.Bind(new GenericNameExpressionPrimaryNode
        {
            Name = BindingFragments.Token("Same"),
            TypeArgs = BindingFragments.TypeArguments("Int32"),
        });

        Assert.True(Assert.Single(body.Parameters).IsFailed);
        Assert.True(local.IsFailed);
        Assert.Same(ErrorType.Instance, local.Type);
        Assert.True(PriorFailure.Of(typeValue));
        Assert.All(new[] { genericCall, plainCall, reference, statedReference }, value =>
        {
            _ = Assert.IsType<ErrorExpression>(value);
            Assert.True(PriorFailure.Of(value));
        });
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0370", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 51), new LinePosition(1, 52)), diagnostic.Position());
        Assert.Empty(body.Function.TypeParametersList);
    }

    [Fact]
    public void IndependentMissingTypesAndValuesStillReportAroundARejectedSignature()
    {
        var f = new BindingFragments();
        var node = Function("Same", "MissingReturn", "MissingParameter", true,
            Evaluate(BindingFragments.Expression(BindingFragments.Identifier("missingBody"))),
            Return(BindingFragments.Expression(BindingFragments.Identifier("x"))));
        LocalFunctionBinder.Declare([Statement(node)], f.Binding, f.Builder);

        _ = LocalFunctionBinder.BindBody(node, f.Binding, f.Builder);
        var call = Assert.IsType<ErrorExpression>(f.Bind(GenericCall("Same", "MissingArgument",
            BindingFragments.Expression(BindingFragments.Identifier("missingArgument")))));

        Assert.Equal(2, call.Children.Count);
        var diagnostics = f.Diagnostics.ToImmutableArray();
        Assert.Equal(6, diagnostics.Length);
        _ = Assert.Single(diagnostics, diagnostic => diagnostic.Number.Value == "MIST0370");
        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Number.Value == "MIST0298"));
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Number.Value == "MIST0296"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("MissingArgument", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("missingBody", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("missingArgument", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOrdinaryParameterInARejectedDeclarationStillOwnsItsIndependentRefusal()
    {
        var f = new BindingFragments();
        var node = Function("Same", "T", "T", true,
            Evaluate(BindingFragments.Expression(BindingFragments.Identifier("x"), Invocation())),
            Evaluate(BindingFragments.Expression(BindingFragments.Identifier("number"), Invocation())),
            Return(BindingFragments.Expression(BindingFragments.Identifier("x"))));
        node.Parameters!.Parameters =
        [new ParameterNode { Name = BindingFragments.Token("number"), Type = BindingFragments.TypeName("Int32"), Attributes = [] }];
        LocalFunctionBinder.Declare([Statement(node)], f.Binding, f.Builder);

        var body = Assert.IsType<LocalFunctionStatement>(LocalFunctionBinder.BindBody(node, f.Binding, f.Builder));

        Assert.True(body.Parameters[0].IsFailed);
        Assert.False(body.Parameters[1].IsFailed);
        Assert.Equal(new[] { "MIST0370", "MIST0363" },
            f.Diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Number.Value));
        Assert.Contains("number", f.Diagnostics.ToImmutableArray()[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedTypeNamesStayInsideTheirDeclarationScope()
    {
        var f = new BindingFragments();
        var node = Function("Same", "T", "T", true);
        LocalFunctionBinder.Declare([Statement(node)], f.Binding, f.Builder);
        _ = LocalFunctionBinder.BindBody(node, f.Binding, f.Builder);

        _ = Assert.IsType<NotFoundResult>(f.Scope.Lookup(new LookupRequest("T", MemberVisibility.None, null)));
        _ = f.Bind(BindingFragments.Identifier("T"));

        Assert.Equal(new[] { "MIST0370", "MIST0296" },
            f.Diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Number.Value));
    }

    [Fact]
    public void RejectedCallsStillTargetAnOrdinaryFunctionParameterAndBindItsLambdaBody()
    {
        var f = new BindingFragments();
        var node = Function("Same", "T", "Int32", true);
        node.Parameters!.Parameter.Type = new TypeReferenceNode
        {
            First = new FuncSingleTypeRefSingleTypeRefNode { TypeArgs = BindingFragments.TypeArguments("Int32", "Int32") },
        };
        LocalFunctionBinder.Declare([Statement(node)], f.Binding, f.Builder);
        var lambda = new LambdaFormExpressionNode
        {
            Lambda = BindingFragments.Lambda(BindingFragments.Expression(BindingFragments.Identifier("missingLambdaValue"))),
        };

        var call = Assert.IsType<ErrorExpression>(f.Bind(GenericCall("Same", "Int32", lambda)));

        var argument = Assert.IsType<LambdaExpression>(call.Children[1]);
        Assert.Same(f.Built.Type("Int32"), Assert.Single(argument.Parameters).Type);
        _ = Assert.IsType<ErrorExpression>(argument.Result);
        Assert.Equal(new[] { "MIST0370", "MIST0296" },
            f.Diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Number.Value));
    }

    [Theory]
    [InlineData("new")]
    [InlineData("default")]
    [InlineData("array")]
    public void RejectedReturnAndParameterTargetsRetainTheDeclarationFailure(string shape)
    {
        var f = new BindingFragments();
        var node = Function("Same", "T", "T", true,
            new BlockStatementStatementNode
            {
                BlockStatement = new BlockNode { Statements = [Return(TargetedValue(shape))] },
            });
        LocalFunctionBinder.Declare([Statement(node)], f.Binding, f.Builder);

        var body = Assert.IsType<LocalFunctionStatement>(LocalFunctionBinder.BindBody(node, f.Binding, f.Builder));
        var returned = Assert.IsType<ReturnStatement>(Assert.Single(Assert.IsType<Block>(Assert.Single(body.Body.Statements)).Statements)).Value;
        var call = Assert.IsType<ErrorExpression>(f.Bind(GenericCall("Same", "Int32", TargetedValue(shape))));

        Assert.True(PriorFailure.Of(returned!));
        Assert.True(PriorFailure.Of(call.Children[1]));
        Assert.Equal("MIST0370", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void ALocalFunctionParameterCollisionReportsTheSecondDeclarationNameSite()
    {
        var f = new BindingFragments();
        var source = new SourceText("local-function.mist", "Int32 Same(Int32 x) {\n  Int32 x = 1;\n}");
        var local = new TypedVariableDeclarationVariableDeclarationStatementNode
        {
            Origin = new ParsedOrigin(source),
            Span = new Storm.Runtime.SourceSpan(23, 12),
            Name = MistTypeGenerator.MakeToken("x", 30),
            Type = BindingFragments.TypeName("Int32"),
            Initializer = Literal("1"),
        };
        var node = Function("Same", "Int32", "Int32", false,
            new VariableDeclarationStatementStatementNode { VariableDeclaration = local });
        LocalFunctionBinder.Declare([Statement(node)], f.Binding, f.Builder);

        _ = LocalFunctionBinder.BindBody(node, f.Binding, f.Builder);

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0367", diagnostic.Number.Value);
        Assert.Equal("local-function.mist", diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(2, 9), new LinePosition(2, 10)), diagnostic.Position());
    }

    [Fact]
    public void AdmittedFunctionsKeepHoistingRecursionCapturesAndNestedShadowing()
    {
        var f = new BindingFragments();
        var captured = Assert.IsType<LocalReferenceExpression>(f.Local("seed", f.Built.Type("Int32"))).Local;
        var inner = Function("Same", "Int32", "Int32", false,
            Return(BindingFragments.Expression(BindingFragments.Identifier("seed"))));
        var outer = Function("Same", "Int32", "Int32", false,
            Evaluate(BindingFragments.Expression(BindingFragments.Identifier("Same"), Invocation(Literal("2")))),
            new BlockStatementStatementNode
            {
                BlockStatement = new BlockNode
                {
                    Statements =
                    [
                        Statement(inner),
                        Return(BindingFragments.Expression(BindingFragments.Identifier("Same"), Invocation(Literal("2")))),
                    ],
                },
            },
            Return(BindingFragments.Expression(BindingFragments.Identifier("seed"))));
        var block = new Block(new BlockNode
        {
            Statements =
            [Evaluate(BindingFragments.Expression(BindingFragments.Identifier("Same"), Invocation(Literal("2")))), Statement(outer)],
        });

        BodyBinder.BindBody(block, f.Scope, null, f.Builder);

        var before = Assert.IsType<InvocationExpression>(Assert.IsType<ExpressionStatement>(block.Statements[0]).Expression);
        var function = Assert.IsType<LocalFunctionStatement>(block.Statements[1]);
        var recursive = Assert.IsType<InvocationExpression>(Assert.IsType<ExpressionStatement>(function.Body.Statements[0]).Expression);
        var nested = Assert.IsType<Block>(function.Body.Statements[1]);
        var shadow = Assert.IsType<LocalFunctionStatement>(nested.Statements[0]);
        var nestedCall = Assert.IsType<InvocationExpression>(Assert.IsType<ReturnStatement>(nested.Statements[1]).Value);
        var capture = Assert.IsType<LocalReferenceExpression>(Assert.IsType<ReturnStatement>(shadow.Body.Statements[0]).Value);

        Assert.Same(function.Function, before.Method);
        Assert.Same(function.Function, recursive.Method);
        Assert.Same(shadow.Function, nestedCall.Method);
        Assert.NotSame(function.Function, shadow.Function);
        Assert.Same(captured, capture.Local);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static LocalFunctionDeclarationNode Function(
        string name, string returnType, string parameterType, bool generic, params StatementNode[] body)
    {
        var node = BindingFragments.At<LocalFunctionDeclarationNode>(50);
        node.Name = BindingFragments.Token(name);
        node.ReturnType = BindingFragments.TypeName(returnType);
        node.TypeParams = generic
            ? new TypeParameterListNode { TypeParameter = new RegularTypeParameterTypeParameterNode { Name = BindingFragments.Token("T") } }
            : null;
        node.Parameters = new ParameterListNode
        {
            Parameter = new ParameterNode { Name = BindingFragments.Token("x"), Type = BindingFragments.TypeName(parameterType), Attributes = [] },
        };
        node.Modifiers = [];
        node.Body = new BlockNode { Statements = [.. body] };
        return node;
    }

    private static LocalFunctionDeclarationStatementStatementNode Statement(LocalFunctionDeclarationNode function)
    {
        return new() { LocalFunction = function };
    }

    private static VariableDeclarationStatementStatementNode Declare(string name, string type, ExpressionNode initializer)
    {
        return new()
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Name = BindingFragments.Token(name),
                Type = BindingFragments.TypeName(type),
                Initializer = initializer,
            },
        };
    }

    private static ReturnStatementStatementNode Return(ExpressionNode value)
    {
        return new() { ReturnStatement = new ReturnStatementNode { Value = value } };
    }

    private static ExpressionStatementStatementNode Evaluate(ExpressionNode value)
    {
        return new() { ExpressionStatement = new ExpressionStatementNode { Expression = value } };
    }

    private static ExpressionNode Literal(string value)
    {
        return BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token(value) });
    }

    private static InvocationSuffixPostfixSuffixNode Invocation(params ExpressionNode[] arguments)
    {
        return new() { Arguments = BindingFragments.Arguments(arguments) };
    }

    private static GenericInvocationExpressionPrimaryNode GenericCall(string name, string type, params ExpressionNode[] arguments)
    {
        return new()
        {
            Name = BindingFragments.Token(name),
            TypeArgs = BindingFragments.TypeArguments(type),
            Arguments = BindingFragments.Arguments(arguments),
        };
    }

    private static ExpressionNode TargetedValue(string shape)
    {
        return BindingFragments.Expression(shape switch
        {
            "new" => new TargetTypedNewExpressionPrimaryNode(),
            "default" => new DefaultLiteralExpressionPrimaryNode(),
            _ => new ArrayLiteralExpressionPrimaryNode(),
        });
    }
}