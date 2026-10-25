using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class LocalBindingContractBinderTests
{
    [Theory]
    [InlineData("new")]
    [InlineData("default")]
    [InlineData("array")]
    public void ATypedForInitializerUsesTheOrdinaryLocalTarget(string shape)
    {
        var f = new BindingFragments();
        var type = Target(f, shape);
        var ordinary = BindLocal(f, "ordinary", type, Value(shape));
        var counted = BindFor(f, "counted", type, Value(shape));

        Assert.Same(ordinary.Local.Type, counted.Local.Type);
        Assert.IsType(ordinary.Initializer!.GetType(), counted.Initializer);
        Assert.Same(ordinary.Initializer.Type, counted.Initializer!.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Null(f.Binding.TargetType);
    }

    [Theory]
    [InlineData("numeric")]
    [InlineData("declared")]
    public void ATypedForInitializerRecordsTheOrdinaryConversion(string kind)
    {
        var f = new BindingFragments();
        var source = f.Built.Type("Int32");
        var target = f.Built.Type("Int64");
        if(kind == "declared")
        {
            target = HandBuiltModel.Class("Converted");
            f.Register(target);
        }

        var conversion = HandBuiltModel.Conversion(target, source, target, isIntrinsic: kind == "numeric");
        _ = f.Local("source", source);
        var initializer = BindingFragments.Expression(BindingFragments.Identifier("source"));
        var ordinary = BindLocal(f, "ordinary", BindingFragments.TypeName(target.Name), initializer);
        var counted = BindFor(f, "counted", BindingFragments.TypeName(target.Name), initializer);

        if(kind == "declared")
        {
            Assert.Same(conversion, Assert.IsType<ImplicitConversionExpression>(ordinary.Initializer).Operator);
            Assert.Same(conversion, Assert.IsType<ImplicitConversionExpression>(counted.Initializer).Operator);
        }
        else
        {
            Assert.Same(Assert.IsType<LocalReferenceExpression>(ordinary.Initializer).Local,
                Assert.IsType<LocalReferenceExpression>(counted.Initializer).Local);
            Assert.Same(target, counted.Local.Type);
        }

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscardsRetainDistinctDeclarationsAndOneInitializerCallEach(bool typed)
    {
        var f = new BindingFragments();
        var function = f.Function("Next", f.Built.Type("Int32"));
        f.Scope.DeclareLocalFunction(new LocalFunctionDeclarationNode(), function, f.Diagnostics);
        var call = BindingFragments.Expression(BindingFragments.Identifier("Next"), new InvocationSuffixPostfixSuffixNode());
        var type = typed ? BindingFragments.TypeName("Int32") : null;
        var first = BindLocal(f, "_", type, call);
        var second = BindLocal(f, "_", type, call);
        var counted = BindFor(f, "_", type, call);

        Assert.True(first.Local.IsDiscard);
        Assert.True(second.Local.IsDiscard);
        Assert.True(counted.Local.IsDiscard);
        Assert.NotSame(first.Local, second.Local);
        Assert.NotSame(first.Local, counted.Local);
        Assert.Same(function, Assert.IsType<InvocationExpression>(first.Initializer).Method);
        Assert.Same(function, Assert.IsType<InvocationExpression>(second.Initializer).Method);
        Assert.Same(function, Assert.IsType<InvocationExpression>(counted.Initializer).Method);
        _ = Assert.IsType<NotFoundResult>(f.Scope.Lookup(new LookupRequest("_", MemberVisibility.InstanceAndStatic, null)));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("new", "MIST0378")]
    [InlineData("default", "MIST0379")]
    [InlineData("array", "MIST0375")]
    public void AnInferredForInitializerDoesNotInheritAnUnrelatedTarget(string shape, string diagnostic)
    {
        var f = new BindingFragments();
        _ = Target(f, shape);
        f.Binding.TargetType = f.Built.Type("Int32");
        var counted = BindFor(f, "_", null, Value(shape));

        Assert.True(counted.Local.IsFailed);
        Assert.Equal(diagnostic, Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
        Assert.Same(f.Built.Type("Int32"), f.Binding.TargetType);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("default")]
    [InlineData("array")]
    public void AFailedDeclaredTargetKeepsOneRootComplaint(string shape)
    {
        var f = new BindingFragments();
        var counted = BindFor(f, "x", BindingFragments.TypeName("Missing"), Value(shape));

        Assert.True(counted.Local.IsFailed);
        Assert.True(PriorFailure.Of(counted.Initializer!));
        Assert.Equal("MIST0298", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void AForeachBodyShadowsItsReadonlyIteratorAndKeepsCollectionIdentity()
    {
        var f = new BindingFragments();
        var array = f.Built.Type("Array<,>");
        _ = f.Local("x", f.Built.Interner.GetOrCreateConstructedType(array, [f.Built.Type("Int32")]));
        var body = new BlockNode
        {
            Statements = [Declare("x", null, Integer(2)), Read("x"),
                new BlockStatementStatementNode { BlockStatement = new BlockNode { Statements = [Declare("x", null, Integer(3)), Read("x")] } }],
        };
        var syntax = new ForeachStatementStatementNode
        {
            ForeachStatement = new VarForeachForeachStatementNode
            {
                Name = BindingFragments.Token("x"),
                Collection = BindingFragments.Expression(BindingFragments.Identifier("x")),
                Body = body,
            },
        };
        var bound = Assert.IsType<ForEachStatement>(StatementBinder.BindStatement(syntax, f.Binding, f.Builder));
        var shadow = Assert.IsType<LocalDeclarationStatement>(bound.Body.Statements[0]).Local;
        var nested = Assert.IsType<Block>(bound.Body.Statements[2]);
        var inner = Assert.IsType<LocalDeclarationStatement>(nested.Statements[0]).Local;

        Assert.True(bound.Variable.IsReadonly);
        Assert.NotSame(shadow, bound.Variable);
        Assert.NotSame(inner, shadow);
        Assert.Same(shadow, Assert.IsType<LocalReferenceExpression>(Assert.IsType<ExpressionStatement>(bound.Body.Statements[1]).Expression).Local);
        Assert.Same(inner, Assert.IsType<LocalReferenceExpression>(Assert.IsType<ExpressionStatement>(nested.Statements[1]).Expression).Local);
        Assert.Same(f.Scope.Locals["x"], Assert.IsType<LocalReferenceExpression>(bound.Collection).Local);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(f.Scope, f.Binding.Scope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AForeachDiscardKeepsAnIteratorIdentityWithoutLookup(bool typed)
    {
        var f = new BindingFragments();
        var array = f.Built.Type("Array<,>");
        _ = f.Local("items", f.Built.Interner.GetOrCreateConstructedType(array, [f.Built.Type("Int32")]));
        var collection = BindingFragments.Expression(BindingFragments.Identifier("items"));
        ForeachStatementNode iteration = typed
            ? new TypedForeachForeachStatementNode { Name = BindingFragments.Token("_"), Type = BindingFragments.TypeName("Int32"), Collection = collection, Body = new BlockNode { Statements = [Read("_")] } }
            : new VarForeachForeachStatementNode { Name = BindingFragments.Token("_"), Collection = collection, Body = new BlockNode { Statements = [Read("_")] } };
        var bound = Assert.IsType<ForEachStatement>(StatementBinder.BindStatement(new ForeachStatementStatementNode { ForeachStatement = iteration }, f.Binding, f.Builder));

        Assert.True(bound.Variable.IsDiscard);
        Assert.True(bound.Variable.IsReadonly);
        _ = Assert.IsType<ErrorExpression>(Assert.IsType<ExpressionStatement>(Assert.Single(bound.Body.Statements)).Expression);
        Assert.Equal("MIST0296", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    private static TypeReferenceNode Target(BindingFragments f, string shape)
    {
        if(shape == "new")
        {
            f.Register(HandBuiltModel.Class("Thing"));
            return BindingFragments.TypeName("Thing");
        }

        if(shape == "array")
        {
            f.Register(f.Built.Type("Array<,>"));
            var type = BindingFragments.TypeName("Int32");
            ((NamedSingleTypeRefSingleTypeRefNode)type.First).ArrayRank = new UnsizedArrayRankArrayRankSuffixNode();
            return type;
        }

        return BindingFragments.TypeName("Int32");
    }

    private static ExpressionNode Value(string shape)
    {
        PrimaryNode primary = shape switch
        {
            "new" => BindingFragments.At<TargetTypedNewExpressionPrimaryNode>(),
            "default" => BindingFragments.At<DefaultLiteralExpressionPrimaryNode>(),
            "array" => BindingFragments.At<ArrayLiteralExpressionPrimaryNode>(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        return BindingFragments.Expression(primary);
    }

    private static ExpressionNode Integer(int value)
    {
        return BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token(value.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
    }

    private static ExpressionStatementStatementNode Read(string name)
    {
        return new ExpressionStatementStatementNode { ExpressionStatement = new ExpressionStatementNode { Expression = BindingFragments.Expression(BindingFragments.Identifier(name)) } };
    }

    private static VariableDeclarationStatementStatementNode Declare(string name, TypeReferenceNode? type, ExpressionNode value)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = type is null
                ? new InferredVariableDeclarationVariableDeclarationStatementNode { Name = BindingFragments.Token(name), Initializer = value }
                : new TypedVariableDeclarationVariableDeclarationStatementNode { Name = BindingFragments.Token(name), Type = type, Initializer = value },
        };
    }

    private static LocalDeclarationStatement BindLocal(BindingFragments f, string name, TypeReferenceNode? type, ExpressionNode value)
    {
        return Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(Declare(name, type, value), f.Binding, f.Builder));
    }

    private static LocalDeclarationStatement BindFor(BindingFragments f, string name, TypeReferenceNode? type, ExpressionNode value)
    {
        ForInitializerNode initializer = type is null
            ? new ForInitializerVarDeclarationForInitializerNode { Name = BindingFragments.Token(name), Initializer = value }
            : new ForInitializerVariableDeclarationForInitializerNode { Name = BindingFragments.Token(name), VariableDeclaration = type, Initializer = value };
        var loop = Assert.IsType<ForStatement>(StatementBinder.BindStatement(new ForStatementStatementNode
        {
            ForStatement = new ForStatementNode { Initializer = initializer, Body = new BlockNode() },
        }, f.Binding, f.Builder));
        return Assert.IsType<LocalDeclarationStatement>(loop.Initializer);
    }
}