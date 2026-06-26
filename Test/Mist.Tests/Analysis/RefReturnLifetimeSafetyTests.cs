using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for MIST_REF_0006: ref return lifetime safety validation.
/// Validates: Requirements 4.3, 4.5, 4.6, 6.2, 6.3, 6.4, 6.6, 6.8, 14.5
/// </summary>
public class RefReturnLifetimeSafetyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = Tok(name),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeThisFieldAccessExpression(string fieldName)
    {
        var thisPrimary = new ThisExpressionPrimaryNode();
        var memberAccess = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = Tok(fieldName),
        };
        var postfix = new PostfixNode
        {
            Target = thisPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberAccess),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeThisFieldIndexExpression(string fieldName)
    {
        var thisPrimary = new ThisExpressionPrimaryNode();
        var memberAccess = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = Tok(fieldName),
        };
        var indexSuffix = new IndexSuffixPostfixSuffixNode
        {
            Index = MakeIntLiteralExpression("0"),
        };
        var postfix = new PostfixNode
        {
            Target = thisPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberAccess, indexSuffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static TypeReferenceNode MakeIntTypeRef()
    {
        return new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            }
        };
    }

    private static ReturnStatementNode MakeReturnStatement(ExpressionNode value)
    {
        return new ReturnStatementNode { Value = value };
    }

    private static MethodDeclarationNode MakeRefReturningMethod(
        string name,
        ImmutableArray<StatementNode> body,
        OwnershipSpecifierNode? returnOwnership = null,
        ParameterListNode? parameters = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = MakeIntTypeRef(),
            ReturnOwnership = returnOwnership ?? new RefSpecifierOwnershipSpecifierNode(),
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    private static CompilationUnitNode MakeClassWithFieldAndMethod(
        MethodDeclarationNode method,
        string fieldName = "_value")
    {
        var fieldDecl = new FieldDeclarationNode
        {
            Name = Tok(fieldName),
            Type = MakeIntTypeRef(),
            Modifiers = ImmutableArray<ModifierNode>.Empty,
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("MyClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new MethodMemberMemberDeclarationNode { Method = method }),
        };
        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    private static ParameterListNode MakeParameterList(params ParameterNode[] parameters)
    {
        if(parameters.Length == 0)
            return new ParameterListNode();

        var first = parameters[0];
        var rest = parameters.Length > 1
            ? ImmutableArray.Create(parameters[1..])
            : ImmutableArray<ParameterNode>.Empty;

        return new ParameterListNode
        {
            Parameter = first,
            Parameters = rest,
        };
    }

    private static ParameterNode MakeRefParameter(string name)
    {
        return new ParameterNode
        {
            RefModifier = Tok("ref"),
            Type = MakeIntTypeRef(),
            Name = Tok(name),
        };
    }

    private static ParameterNode MakeValueParameter(string name)
    {
        return new ParameterNode
        {
            Type = MakeIntTypeRef(),
            Name = Tok(name),
        };
    }

    // ─── Tests: Valid ref return targets (no diagnostic) ─────────────────

    /// <summary>
    /// Returning this.field from a ref-returning method is valid.
    /// Validates: Requirement 6.3 (instance field via this)
    /// </summary>
    [Fact]
    public void RefReturn_ThisField_NoDiagnostic()
    {
        // ref int GetRef() { return ref this._value; }
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = MakeReturnStatement(MakeThisFieldAccessExpression("_value")),
        };

        var method = MakeRefReturningMethod("GetRef",
            ImmutableArray.Create<StatementNode>(returnStmt));
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0006").ToList();
        Assert.Empty(refErrors);
    }

    /// <summary>
    /// Returning this.array[i] from a ref-returning method is valid.
    /// Validates: Requirement 6.4 (array element via instance field)
    /// </summary>
    [Fact]
    public void RefReturn_ThisFieldArrayElement_NoDiagnostic()
    {
        // ref int GetRef() { return ref this._items[0]; }
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = MakeReturnStatement(MakeThisFieldIndexExpression("_items")),
        };

        var method = MakeRefReturningMethod("GetRef",
            ImmutableArray.Create<StatementNode>(returnStmt));
        var ast = MakeClassWithFieldAndMethod(method, "_items");
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0006").ToList();
        Assert.Empty(refErrors);
    }

    /// <summary>
    /// Returning a ref parameter from a ref-returning method is valid.
    /// Validates: Requirement 6.6 (ref parameter's storage outlives callee)
    /// </summary>
    [Fact]
    public void RefReturn_RefParameter_NoDiagnostic()
    {
        // ref int GetRef(ref int x) { return ref x; }
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = MakeReturnStatement(MakeIdentifierExpression("x")),
        };

        var paramList = MakeParameterList(MakeRefParameter("x"));
        var method = MakeRefReturningMethod("GetRef",
            ImmutableArray.Create<StatementNode>(returnStmt),
            parameters: paramList);
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0006").ToList();
        Assert.Empty(refErrors);
    }

    // ─── Tests: Invalid ref return targets (diagnostic expected) ─────────

    /// <summary>
    /// Returning a local variable from a ref-returning method is invalid.
    /// Validates: Requirements 4.5, 6.2, 14.5
    /// </summary>
    [Fact]
    public void RefReturn_LocalVariable_EmitsDiagnostic()
    {
        // ref int GetRef() { int x = 0; return ref x; }
        var localDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeIntTypeRef(),
                Name = Tok("x"),
                Initializer = MakeIntLiteralExpression("0"),
            },
        };
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = MakeReturnStatement(MakeIdentifierExpression("x")),
        };

        var method = MakeRefReturningMethod("GetRef",
            ImmutableArray.Create<StatementNode>(localDecl, returnStmt));
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0006").ToList();
        Assert.Single(refErrors);
        Assert.Contains("x", refErrors[0].Message);
    }

    /// <summary>
    /// Returning a non-ref parameter from a ref-returning method is invalid.
    /// Validates: Requirements 4.5, 6.2, 14.5
    /// </summary>
    [Fact]
    public void RefReturn_NonRefParameter_EmitsDiagnostic()
    {
        // ref int GetRef(int x) { return ref x; }
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = MakeReturnStatement(MakeIdentifierExpression("x")),
        };

        var paramList = MakeParameterList(MakeValueParameter("x"));
        var method = MakeRefReturningMethod("GetRef",
            ImmutableArray.Create<StatementNode>(returnStmt),
            parameters: paramList);
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0006").ToList();
        Assert.Single(refErrors);
        Assert.Contains("x", refErrors[0].Message);
    }

    /// <summary>
    /// Returning a literal from a ref-returning method is invalid.
    /// Validates: Requirements 4.6, 14.5
    /// </summary>
    [Fact]
    public void RefReturn_Literal_EmitsDiagnostic()
    {
        // ref int GetRef() { return ref 42; }
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = MakeReturnStatement(MakeIntLiteralExpression("42")),
        };

        var method = MakeRefReturningMethod("GetRef",
            ImmutableArray.Create<StatementNode>(returnStmt));
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0006").ToList();
        Assert.Single(refErrors);
    }

    /// <summary>
    /// Returning a ref readonly from a ref-readonly-returning method with a local variable is invalid.
    /// Validates: Requirements 4.5, 6.2
    /// </summary>
    [Fact]
    public void RefReadonlyReturn_LocalVariable_EmitsDiagnostic()
    {
        // ref readonly int GetRef() { int x = 0; return ref x; }
        var localDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeIntTypeRef(),
                Name = Tok("x"),
                Initializer = MakeIntLiteralExpression("0"),
            },
        };
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = MakeReturnStatement(MakeIdentifierExpression("x")),
        };

        var method = MakeRefReturningMethod("GetRef",
            ImmutableArray.Create<StatementNode>(localDecl, returnStmt),
            returnOwnership: new ReadonlyRefSpecifierOwnershipSpecifierNode());
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0006").ToList();
        Assert.Single(refErrors);
        Assert.Contains("x", refErrors[0].Message);
    }

    /// <summary>
    /// A non-ref-returning method returning a local variable does NOT produce MIST_REF_0006.
    /// Validates: The check only applies to ref-returning methods.
    /// </summary>
    [Fact]
    public void NonRefReturn_LocalVariable_NoDiagnostic()
    {
        // int GetValue() { int x = 0; return x; }
        var localDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeIntTypeRef(),
                Name = Tok("x"),
                Initializer = MakeIntLiteralExpression("0"),
            },
        };
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = MakeReturnStatement(MakeIdentifierExpression("x")),
        };

        // Non-ref returning method (no returnOwnership)
        var method = new MethodDeclarationNode
        {
            Name = Tok("GetValue"),
            ReturnType = MakeIntTypeRef(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(localDecl, returnStmt),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0006").ToList();
        Assert.Empty(refErrors);
    }
}