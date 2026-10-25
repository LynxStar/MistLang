using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;
using SourceSpan = Storm.Runtime.SourceSpan;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class LocalBindingContractOriginBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryDeclarationsReportTheSecondTypedOrInferredName(bool typed)
    {
        var f = new BindingFragments();
        var source = new SourceText("ordinary-locals.mist", typed
            ? "var item = 1;\n  Int32\n  item = 2;"
            : "var item = 1;\n  var\n  item = 2;");
        var first = Declaration(source, 0, 4, typed: false);
        var second = Declaration(source, source.Contents.IndexOf('\n') + 1, source.Contents.LastIndexOf("item", StringComparison.Ordinal), typed);

        _ = StatementBinder.BindStatement(first, f.Binding, f.Builder);
        _ = StatementBinder.BindStatement(second, f.Binding, f.Builder);

        AssertDuplicate(f, "ordinary-locals.mist", 3, 3);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ForHeaderNamesPermitBodyShadowingButBodyRepeatsStillRefuse(bool typed, bool repeatBody)
    {
        var f = new BindingFragments();
        var source = new SourceText("for-locals.mist", typed
            ? "for (Int32 item = 0;) {\n  var item = 1;\n  var item = 2;\n}"
            : "for (var item = 0;) {\n  var item = 1;\n  var item = 2;\n}");
        var firstBodyStart = source.Contents.IndexOf('\n') + 1;
        var firstBodyName = source.Contents.IndexOf("item", firstBodyStart, StringComparison.Ordinal);
        var secondBodyStart = source.Contents.IndexOf('\n', firstBodyStart) + 1;
        var secondBodyName = source.Contents.IndexOf("item", secondBodyStart, StringComparison.Ordinal);
        var headerName = source.Contents.IndexOf("item", StringComparison.Ordinal);
        var value = BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("0") });
        ForInitializerNode initializer;
        if(typed)
        {
            var node = At<ForInitializerVariableDeclarationForInitializerNode>(source, 5, firstBodyStart - 5);
            node.VariableDeclaration = BindingFragments.TypeName("Int32");
            node.Name = MistTypeGenerator.MakeToken("item", headerName);
            node.Initializer = value;
            initializer = node;
        }
        else
        {
            var node = At<ForInitializerVarDeclarationForInitializerNode>(source, 5, firstBodyStart - 5);
            node.Name = MistTypeGenerator.MakeToken("item", headerName);
            node.Initializer = value;
            initializer = node;
        }

        var statements = new List<StatementNode> { Declaration(source, firstBodyStart, firstBodyName, typed: false) };
        if(repeatBody)
        {
            statements.Add(Declaration(source, secondBodyStart, secondBodyName, typed: false));
        }

        var bound = Assert.IsType<ForStatement>(StatementBinder.BindStatement(new ForStatementStatementNode
        {
            ForStatement = new ForStatementNode { Initializer = initializer, Body = new BlockNode { Statements = [.. statements] } },
        }, f.Binding, f.Builder));

        var header = Assert.IsType<LocalDeclarationStatement>(bound.Initializer);
        var shadow = Assert.IsType<LocalDeclarationStatement>(bound.Body.Statements[0]);
        Assert.NotSame(header.Local, shadow.Local);
        Assert.Empty(f.Scope.Locals);
        if(repeatBody)
        {
            AssertDuplicate(f, "for-locals.mist", 3, 7);
        }
        else
        {
            Assert.Empty(f.Diagnostics.ToImmutableArray());
        }
    }

    [Fact]
    public void RepeatedMethodParameterReportsTheRetainedSecondParameterToken()
    {
        var f = new BindingFragments();
        var source = new SourceText("parameters.mist", "Int32 item,\n  Int32 item");
        var first = Parameter(source, 0, 6);
        var second = Parameter(source, 14, 20);
        var parameters = new ParameterBuilder(f.Builder.Resolver, f.Builder.Attributes, f.Diagnostics)
            .Resolve(new ParameterListNode { Parameter = first, Parameters = [second] }, f.Types);
        var method = new Method("Repeat", f.Built.Type("Void"), parameters, new MemberModifiers())
        {
            Scope = f.Scope,
            Body = new Block(new BlockNode { Statements = [] }),
        };

        BodyBinder.BindDelegate(method, f.Builder);

        Assert.Same(second, parameters[1].DeclarationNode);
        AssertDuplicate(f, "parameters.mist", 2, 9);
    }

    [Fact]
    public void AMethodLocalCollidingWithItsParameterReportsTheLocalToken()
    {
        var f = new BindingFragments();
        var source = new SourceText("method-local.mist", "Int32 item;\n  var item = 1;");
        var parameter = new Parameter("item", f.Built.Type("Int32")) { DeclarationNode = Parameter(source, 0, 6) };
        var declaration = At<InferredVariableDeclarationVariableDeclarationStatementNode>(source, 14, 13);
        declaration.Name = MistTypeGenerator.MakeToken("item", 18);
        declaration.Initializer = BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") });
        var method = new Method("Collision", f.Built.Type("Void"), [parameter], new MemberModifiers())
        {
            Scope = f.Scope,
            Body = new Block(new BlockNode
            {
                Statements = [new VariableDeclarationStatementStatementNode { VariableDeclaration = declaration }],
            }),
        };

        BodyBinder.BindDelegate(method, f.Builder);

        AssertDuplicate(f, "method-local.mist", 2, 7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LambdaParameterCollisionsReportTheSecondToken(bool typed)
    {
        var f = new BindingFragments();
        var source = new SourceText("lambda-parameters.mist", typed ? "(Int32 item,\n  Int32 item) => item" : "(item,\n  item) => item");
        var first = LambdaParameter(source, 1, typed ? 7 : 1, typed);
        var second = LambdaParameter(source, typed ? 15 : 9, typed ? 21 : 9, typed);
        var lambda = At<ExpressionLambdaLambdaExpressionNode>(source, 0, source.Contents.Length);
        lambda.Params = new ParenthesizedLambdaParamsLambdaParameterListNode
        {
            Params = new LambdaParamListNode { LambdaParam = first, LambdaParams = [second] },
        };
        lambda.Body = BindingFragments.Expression(BindingFragments.Identifier("item"));
        var type = f.Built.Type("Int32");

        using(f.Binding.PushTargetType(f.Built.Interner.GetOrCreateFunctionType([type, type], type)))
        {
            _ = LambdaBinder.Bind(lambda, f.Binding, f.Builder);
        }

        AssertDuplicate(f, "lambda-parameters.mist", 2, typed ? 9 : 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatternBindingsReportTheirRepeatedNameToken(bool typed)
    {
        var f = new BindingFragments();
        _ = f.Local("item", f.Built.Type("Int32"));
        var source = new SourceText("pattern.mist", typed ? "Int32\n  item" : "var\n  item");
        PrimaryPatternNode primary;
        if(typed)
        {
            var node = At<TypePatternPrimaryPatternNode>(source, 0, source.Contents.Length);
            node.Type = BindingFragments.TypeName("Int32");
            node.Name = MistTypeGenerator.MakeToken("item", 8);
            primary = node;
        }
        else
        {
            var node = At<VarPatternPrimaryPatternNode>(source, 0, source.Contents.Length);
            node.Name = MistTypeGenerator.MakeToken("item", 6);
            primary = node;
        }

        _ = PatternBinder.Bind(Pattern(primary), f.Built.Type("Int32"), f.Binding, f.Builder);

        AssertDuplicate(f, "pattern.mist", 2, 3);
    }

    [Fact]
    public void NamedTuplePatternBindingsRetainTheirElementNameSites()
    {
        var f = new BindingFragments();
        var source = new SourceText("tuple-pattern.mist", "(Int32 item,\n  Int32 item) whole");
        var first = At<TupleTypeElementNode>(source, 1, 10);
        first.Type = BindingFragments.TypeName("Int32");
        first.Name = MistTypeGenerator.MakeToken("item", 7);
        var second = At<TupleTypeElementNode>(source, 15, 10);
        second.Type = BindingFragments.TypeName("Int32");
        second.Name = MistTypeGenerator.MakeToken("item", 21);
        var primary = At<TypePatternPrimaryPatternNode>(source, 0, source.Contents.Length);
        primary.Type = new TypeReferenceNode
        {
            First = new TupleSingleTypeRefSingleTypeRefNode { First = first, Rest = second, TupleTypeElements = [] },
        };
        primary.Name = MistTypeGenerator.MakeToken("whole", 27);
        var type = f.Built.Type("Int32");

        _ = PatternBinder.Bind(Pattern(primary), f.Built.Interner.GetOrCreateTupleType([type, type]), f.Binding, f.Builder);

        AssertDuplicate(f, "tuple-pattern.mist", 2, 9);
    }

    private static void AssertDuplicate(BindingFragments f, string path, int line, int column)
    {
        var duplicate = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_DUPLICATE_LOCAL", duplicate.Number.Value);
        Assert.Equal(path, duplicate.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(line, column), new LinePosition(line, column + 4)), duplicate.Position());
    }

    private static ParameterNode Parameter(SourceText source, int start, int nameStart)
    {
        var node = At<ParameterNode>(source, start, nameStart + 4 - start);
        node.Name = MistTypeGenerator.MakeToken("item", nameStart);
        node.Type = BindingFragments.TypeName("Int32");
        node.Attributes = [];
        return node;
    }

    private static VariableDeclarationStatementStatementNode Declaration(SourceText source, int start, int nameStart, bool typed)
    {
        var value = BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") });
        VariableDeclarationStatementNode declaration;
        if(typed)
        {
            var node = At<TypedVariableDeclarationVariableDeclarationStatementNode>(source, start, source.Contents.Length - start);
            node.Type = BindingFragments.TypeName("Int32");
            node.Name = MistTypeGenerator.MakeToken("item", nameStart);
            node.Initializer = value;
            declaration = node;
        }
        else
        {
            var node = At<InferredVariableDeclarationVariableDeclarationStatementNode>(source, start, source.Contents.Length - start);
            node.Name = MistTypeGenerator.MakeToken("item", nameStart);
            node.Initializer = value;
            declaration = node;
        }

        return new VariableDeclarationStatementStatementNode { VariableDeclaration = declaration };
    }

    private static LambdaParamNode LambdaParameter(SourceText source, int start, int nameStart, bool typed)
    {
        if(typed)
        {
            var node = At<TypedLambdaParamLambdaParamNode>(source, start, nameStart + 4 - start);
            node.Name = MistTypeGenerator.MakeToken("item", nameStart);
            node.Type = BindingFragments.TypeName("Int32");
            return node;
        }

        var untyped = At<UntypedLambdaParamLambdaParamNode>(source, start, nameStart + 4 - start);
        untyped.Name = MistTypeGenerator.MakeToken("item", nameStart);
        return untyped;
    }

    private static PatternNode Pattern(PrimaryPatternNode primary)
    {
        return new()
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };
    }

    private static T At<T>(SourceText source, int start, int length) where T : SyntaxNode, new()
    {
        return new T { Origin = new ParsedOrigin(source), Span = new SourceSpan(start, length) };
    }
}