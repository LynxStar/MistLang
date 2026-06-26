using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for MIST_REF_0008: ref field storage prohibition.
/// Validates: Requirements 6.5, 14.8
/// </summary>
public class RefFieldStorageProhibitionTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

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
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
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

    private static ExpressionNode MakeIntLiteralExpression(string value = "42")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value),
        };
        return WrapPrimaryAsExpression(primary);
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

    /// <summary>
    /// Creates an assignment statement: this.fieldName = rhsExpr;
    /// </summary>
    private static StatementNode MakeFieldAssignmentStatement(string fieldName, ExpressionNode rhsExpr)
    {
        // Build the LHS: this.fieldName
        var thisPrimary = new ThisExpressionPrimaryNode();
        var memberAccess = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = Tok(fieldName),
        };
        var lhsPostfix = new PostfixNode
        {
            Target = thisPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberAccess),
        };
        var lhsUnary = new PostfixExpressionUnaryNode { Primary = lhsPostfix };
        var lhsMult = new MultiplicativeNode { Left = lhsUnary };
        var lhsAdd = new AdditiveNode { Left = lhsMult };
        var lhsRel = new RelationalComparisonRelationalNode { Left = lhsAdd };
        var lhsEq = new EqualityNode { Left = lhsRel };
        var lhsLogAnd = new LogicalAndNode { Left = lhsEq };
        var lhsLogOr = new LogicalOrNode { Left = lhsLogAnd };

        // Build the full assignment expression: this.fieldName = rhs
        // The assignment RHS is now a full ExpressionNode (widened to reach lambdas).
        var assignment = new AssignmentNode { Target = new VoidCoalescingNode { Left = lhsLogOr }, Value = rhsExpr };
        var expr = new AssignmentFormExpressionNode { Assignment = assignment };

        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
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

    private static ParameterListNode MakeParameterList(params ParameterNode[] parameters)
    {
        if(parameters.Length == 0)
        {
            return new ParameterListNode();
        }

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

    private static MethodDeclarationNode MakeMethod(
        string name,
        ImmutableArray<StatementNode> body,
        ParameterListNode? parameters = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = MakeIntTypeRef(),
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    // ─── Tests: Ref parameter assigned to field (should emit MIST_REF_0008) ──

    /// <summary>
    /// Assigning a ref parameter to a field should emit MIST_REF_0008.
    /// Validates: Requirement 6.5 (refs cannot be stored in fields)
    /// </summary>
    [Fact]
    public void RefParam_AssignedToField_EmitsDiagnostic()
    {
        // void SetField(ref int x) { this._value = x; }
        var assignStmt = MakeFieldAssignmentStatement("_value", MakeIdentifierExpression("x"));

        var paramList = MakeParameterList(MakeRefParameter("x"));
        var method = MakeMethod("SetField",
            ImmutableArray.Create(assignStmt),
            parameters: paramList);
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0008").ToList();
        _ = Assert.Single(refErrors);
        Assert.Contains("Cannot store a ref in a field", refErrors[0].Message);
    }

    /// <summary>
    /// Assigning a ref readonly parameter to a field should emit MIST_REF_0008.
    /// Validates: Requirement 6.5 (refs cannot be stored in fields)
    /// </summary>
    [Fact]
    public void RefReadonlyParam_AssignedToField_EmitsDiagnostic()
    {
        // void SetField(readonly ref int x) { this._value = x; }
        var assignStmt = MakeFieldAssignmentStatement("_value", MakeIdentifierExpression("x"));

        // readonly ref parameter: modifier=READONLY, refModifier=REF
        var refReadonlyParam = new ParameterNode
        {
            Modifier = Tok("readonly"),
            RefModifier = Tok("ref"),
            Type = MakeIntTypeRef(),
            Name = Tok("x"),
        };
        var paramList = MakeParameterList(refReadonlyParam);
        var method = MakeMethod("SetField",
            ImmutableArray.Create(assignStmt),
            parameters: paramList);
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0008").ToList();
        _ = Assert.Single(refErrors);
    }

    // ─── Tests: Non-ref parameter assigned to field (no diagnostic) ──────

    /// <summary>
    /// Assigning a non-ref parameter to a field should NOT emit MIST_REF_0008.
    /// This is a normal value assignment.
    /// </summary>
    [Fact]
    public void ValueParam_AssignedToField_NoDiagnostic()
    {
        // void SetField(int x) { this._value = x; }
        var assignStmt = MakeFieldAssignmentStatement("_value", MakeIdentifierExpression("x"));

        var paramList = MakeParameterList(MakeValueParameter("x"));
        var method = MakeMethod("SetField",
            ImmutableArray.Create(assignStmt),
            parameters: paramList);
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0008").ToList();
        Assert.Empty(refErrors);
    }

    /// <summary>
    /// Assigning a literal to a field should NOT emit MIST_REF_0008.
    /// </summary>
    [Fact]
    public void Literal_AssignedToField_NoDiagnostic()
    {
        // void SetField() { this._value = 42; }
        var assignStmt = MakeFieldAssignmentStatement("_value", MakeIntLiteralExpression("42"));

        var method = MakeMethod("SetField",
            ImmutableArray.Create(assignStmt));
        var ast = MakeClassWithFieldAndMethod(method);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0008").ToList();
        Assert.Empty(refErrors);
    }
}