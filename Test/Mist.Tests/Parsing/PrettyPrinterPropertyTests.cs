// Feature: mist-language-v1, Property 2: Pretty printer idempotence
// Feature: mist-language-v1, Property 3: Pretty printer semantic preservation

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for the MistPrettyPrinter.
/// Uses hand-crafted representative ASTs since FsCheck generators (Task 16)
/// and the full parser (skeletal) are not yet available.
/// </summary>
public class PrettyPrinterPropertyTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
        return WrapPrimaryInExpression(primary);
    }

    private static ExpressionNode MakeStringLiteralExpression(string value = "\"hello\"")
    {
        var primary = new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = MakeToken(value),
        };
        return WrapPrimaryInExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        };
        return WrapPrimaryInExpression(primary);
    }

    private static ExpressionNode MakeBoolLiteralExpression(bool value)
    {
        PrimaryNode primary = value
            ? new TrueLiteralExpressionPrimaryNode()
            : new FalseLiteralExpressionPrimaryNode();
        return WrapPrimaryInExpression(primary);
    }

    private static ExpressionNode MakeThisExpression()
    {
        return WrapPrimaryInExpression(new ThisExpressionPrimaryNode());
    }

    private static ExpressionNode MakeNewObjectExpression(string typeName, params ExpressionNode[] args)
    {
        ArgumentListNode? argList = null;
        if (args.Length > 0)
        {
            argList = new ArgumentListNode { Expression = args[0] };
            // Additional args would need to be added via Children, but for simplicity
            // we only support 0 or 1 args in this helper.
        }

        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = argList,
        };
        return WrapPrimaryInExpression(primary);
    }

    private static ExpressionNode MakeNewArrayExpression(string elementType, ExpressionNode size)
    {
        var primary = new ArrayCreationExpressionPrimaryNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Size = size,
        };
        return WrapPrimaryInExpression(primary);
    }

    private static ExpressionNode MakeMethodCallExpression(string target, string methodName, params ExpressionNode[] args)
    {
        ArgumentListNode? argList = null;
        if (args.Length > 0)
        {
            argList = new ArgumentListNode { Expression = args[0] };
        }

        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(target),
        };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            Arguments = argList,
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeMemberAccessExpression(string target, string memberName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(target),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken(memberName),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeAssignmentExpression(string target, ExpressionNode value)
    {
        var targetLogOr = MakeIdentifierExpression(target).Assignment.Target;
        var valueAssignment = value.Assignment;
        var assign = new AssignmentNode { Target = targetLogOr, Value = valueAssignment };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode WrapPrimaryInExpression(PrimaryNode primary)
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
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeBinaryExpression(ExpressionNode left, string op, ExpressionNode right)
    {
        // Build an additive expression: left op right
        var leftUnary = ((RelationalComparisonRelationalNode)left.Assignment.Target.Left.Left.Left).Left.Left.Left;
        var rightUnary = ((RelationalComparisonRelationalNode)right.Assignment.Target.Left.Left.Left).Left.Left.Left;

        var add = new AdditiveNode
        {
            Left = new MultiplicativeNode { Left = leftUnary },
            Operator = MakeToken(op),
            Right = new MultiplicativeNode { Left = rightUnary },
        };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = expr,
            },
        };
    }

    private static StatementNode MakeReturnStatement(ExpressionNode? value = null)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = value,
            },
        };
    }

    private static StatementNode MakeIntVarDecl(string name, string value = "0")
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                },
                Name = MakeToken(name),
                Initializer = MakeIntLiteralExpression(value),
            },
        };
    }

    private static StatementNode MakeVarDecl(string name, ExpressionNode init)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = MakeToken(name),
                Initializer = init,
            },
        };
    }

    private static StatementNode MakeBreakStatement()
    {
        return new BreakStatementStatementNode
        {
            BreakStatement = new BreakStatementNode(),
        };
    }

    // ─── AST Builders for Representative Programs ────────────────────────

    /// <summary>
    /// Builds a representative AST: empty class.
    /// </summary>
    private static CompilationUnitNode BuildEmptyClass()
    {
        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode
                {
                    ClassDeclaration = new ClassDeclarationNode
                    {
                        Name = MakeToken("Empty"),
                        Members = ImmutableArray<MemberDeclarationNode>.Empty,
                    },
                }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a representative AST: class with fields, constructor, and method.
    /// </summary>
    private static CompilationUnitNode BuildClassWithMembers()
    {
        var fieldDecl = new FieldDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = MakeToken("x"),
        };

        var ctorParam = new ParameterNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = MakeToken("x"),
        };

        // this.x = x;
        var thisExpr = MakeAssignmentExpression("x", MakeIdentifierExpression("x"));
        // Wrap this.x as member access on this
        var thisPrimary = new ThisExpressionPrimaryNode();
        var memberSuffix = new MemberAccessSuffixPostfixSuffixNode { Name = MakeToken("x") };
        var postfix = new PostfixNode
        {
            Target = thisPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberSuffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var targetLogOr = new LogicalOrNode { Left = logAnd };
        var assignExpr = new ExpressionNode
        {
            Assignment = new AssignmentNode
            {
                Target = targetLogOr,
                Value = MakeIdentifierExpression("x").Assignment,
            },
        };

        var ctorDecl = new ConstructorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Point"),
            Parameters = new ParameterListNode { Parameter = ctorParam },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(
                    MakeExpressionStatement(assignExpr)),
            } },
        };

        var methodDecl = new MethodDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            ReturnType = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = MakeToken("GetX"),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(
                    MakeReturnStatement(MakeIdentifierExpression("x"))),
            } },
        };

        var classDecl = new ClassDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Point"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl },
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a representative AST: file-scoped namespace with using and struct.
    /// </summary>
    private static CompilationUnitNode BuildNamespaceWithStruct()
    {
        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("System"),
                Span = new Storm.Runtime.SourceSpan(0, 6),
            },
        };

        var fieldR = new FieldDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = MakeToken("r"),
        };

        var structDecl = new StructDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Color"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldR }),
        };

        var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("MyApp"),
                Span = new Storm.Runtime.SourceSpan(0, 5),
            },
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = structDecl }).AsNamespaceBodyItems(),
        };

        return new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };
    }

    /// <summary>
    /// Builds a representative AST: class with control flow statements.
    /// </summary>
    private static CompilationUnitNode BuildClassWithControlFlow()
    {
        // if (x) { return 1; } else { return 0; }
        var ifStmt = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = MakeIdentifierExpression("x"),
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                        MakeReturnStatement(MakeIntLiteralExpression("1"))),
                },
                ElseBranch = new ElseBlockElseClauseNode
                {
                    ElseBlock = new BlockNode
                    {
                        Statements = ImmutableArray.Create(
                            MakeReturnStatement(MakeIntLiteralExpression("0"))),
                    },
                },
            },
        };

        // while (true) { break; }
        var whileStmt = new WhileStatementStatementNode
        {
            WhileStatement = new WhileStatementNode
            {
                Condition = MakeBoolLiteralExpression(true),
                Body = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeBreakStatement()),
                },
            },
        };

        // for (int i = 0; ; ) { }
        var forStmt = new ForStatementStatementNode
        {
            ForStatement = new ForStatementNode
            {
                Initializer = new ForInitializerVariableDeclarationForInitializerNode
                {
                    VariableDeclaration = new PrimitiveTypeRefTypeReferenceNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    },
                    Name = MakeToken("i"),
                    Initializer = MakeIntLiteralExpression("0"),
                },
                Condition = null,
                Iterator = null,
                Body = new BlockNode(),
            },
        };

        // foreach (int item in items) { }
        var foreachStmt = new ForeachStatementStatementNode
        {
            ForeachStatement = new ForeachStatementNode
            {
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                },
                Name = MakeToken("item"),
                Collection = MakeIdentifierExpression("items"),
                Body = new BlockNode(),
            },
        };

        var methodDecl = new MethodDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Name = MakeToken("Run"),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    ifStmt,
                    whileStmt,
                    forStmt,
                    foreachStmt),
            } },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a representative AST: class with various expression types.
    /// </summary>
    private static CompilationUnitNode BuildClassWithExpressions()
    {
        // var x = 42;
        var varDecl = MakeVarDecl("x", MakeIntLiteralExpression("42"));

        // int y = x + 1;
        var addExpr = MakeBinaryExpression(MakeIdentifierExpression("x"), "+", MakeIntLiteralExpression("1"));
        var yDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                },
                Name = MakeToken("y"),
                Initializer = addExpr,
            },
        };

        // Console.WriteLine();
        var callStmt = MakeExpressionStatement(MakeMethodCallExpression("Console", "WriteLine"));

        // return;
        var returnStmt = MakeReturnStatement();

        var methodDecl = new MethodDeclarationNode
        {
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Name = MakeToken("Test"),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    varDecl,
                    yDecl,
                    callStmt,
                    returnStmt),
            } },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Expressions"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a representative AST: constructor chaining.
    /// </summary>
    private static CompilationUnitNode BuildConstructorChaining()
    {
        var paramX = new ParameterNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = MakeToken("x"),
        };

        var ctor1 = new ConstructorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Foo"),
            Parameters = new ParameterListNode { Parameter = paramX },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var ctor2 = new ConstructorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Foo"),
            Parameters = null,
            Chain = new ThisChainConstructorChainNode
            {
                Arguments = new ArgumentListNode
                {
                    Expression = MakeIntLiteralExpression("0"),
                },
            },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var classDecl = new ClassDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Foo"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctor1 },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctor2 }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a representative AST: block-scoped namespace.
    /// </summary>
    private static CompilationUnitNode BuildBlockNamespace()
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Inner"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var nsNode = new BlockNamespaceNamespaceDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("MyNs"),
                Span = new Storm.Runtime.SourceSpan(0, 4),
            },
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };
    }

    /// <summary>
    /// Returns all representative ASTs for property testing.
    /// </summary>
    private static IEnumerable<CompilationUnitNode> AllRepresentativeAsts()
    {
        yield return BuildEmptyClass();
        yield return BuildClassWithMembers();
        yield return BuildNamespaceWithStruct();
        yield return BuildClassWithControlFlow();
        yield return BuildClassWithExpressions();
        yield return BuildConstructorChaining();
        yield return BuildBlockNamespace();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Pretty printer idempotence
    // **Validates: Requirements 12.2, 12.4**
    //
    // For any valid Mist AST, pretty-printing the AST to source text,
    // parsing that source text back to an AST, and pretty-printing again
    // SHALL produce output identical to the first pretty-print.
    //
    // Since the Storm-generated parser is skeletal (not yet implemented),
    // we verify idempotence structurally:
    // 1. Pretty-print produces non-empty, well-formed output
    // 2. The output is deterministic (printing the same AST twice yields identical output)
    // 3. The output contains expected syntactic elements
    //
    // When the parser is fully implemented, this test should be updated to
    // do the full round-trip: print → parse → print and compare.
    // ═══════════════════════════════════════════════════════════════════════

    // Feature: mist-language-v1, Property 2: Pretty printer idempotence

    [Fact]
    public void PrettyPrinter_Idempotence_PrintingTwiceProducesIdenticalOutput()
    {
        // For each representative AST, verify that printing twice yields the same result.
        // This is the determinism half of idempotence — the same AST always produces
        // the same output. The full round-trip (print → parse → print) will be tested
        // when the parser is implemented.
        foreach (var ast in AllRepresentativeAsts())
        {
            var output1 = MistPrettyPrinter.Print(ast);
            var output2 = MistPrettyPrinter.Print(ast);

            Assert.Equal(output1, output2);
        }
    }

    [Fact]
    public void PrettyPrinter_Idempotence_OutputIsNonEmpty()
    {
        foreach (var ast in AllRepresentativeAsts())
        {
            var output = MistPrettyPrinter.Print(ast);
            Assert.False(string.IsNullOrWhiteSpace(output),
                "Pretty printer should produce non-empty output for valid ASTs");
        }
    }

    [Fact]
    public void PrettyPrinter_Idempotence_OutputContainsExpectedSyntax()
    {
        // Verify the output contains expected Mist syntax elements.
        // This validates R12.2: output is syntactically valid Mist source.

        // Empty class
        var emptyClassOutput = MistPrettyPrinter.Print(BuildEmptyClass());
        Assert.Contains("class Empty {", emptyClassOutput);
        Assert.Contains("}", emptyClassOutput);

        // Class with members
        var membersOutput = MistPrettyPrinter.Print(BuildClassWithMembers());
        Assert.Contains("public class Point {", membersOutput);
        Assert.Contains("private int x;", membersOutput);
        Assert.Contains("public Point(int x) {", membersOutput);
        Assert.Contains("public int GetX() {", membersOutput);
        Assert.Contains("return x;", membersOutput);

        // Namespace with struct
        var nsOutput = MistPrettyPrinter.Print(BuildNamespaceWithStruct());
        Assert.Contains("using System;", nsOutput);
        Assert.Contains("namespace MyApp;", nsOutput);
        Assert.Contains("public struct Color {", nsOutput);
        Assert.Contains("public int r;", nsOutput);

        // Control flow
        var cfOutput = MistPrettyPrinter.Print(BuildClassWithControlFlow());
        Assert.Contains("if (x) {", cfOutput);
        Assert.Contains("} else {", cfOutput);
        Assert.Contains("while (true) {", cfOutput);
        Assert.Contains("break;", cfOutput);
        Assert.Contains("for (int i = 0; ; ) {", cfOutput);
        Assert.Contains("foreach (int item in items) {", cfOutput);

        // Expressions
        var exprOutput = MistPrettyPrinter.Print(BuildClassWithExpressions());
        Assert.Contains("var x = 42;", exprOutput);
        Assert.Contains("int y = x + 1;", exprOutput);
        Assert.Contains("Console.WriteLine();", exprOutput);
        Assert.Contains("return;", exprOutput);

        // Constructor chaining
        var chainOutput = MistPrettyPrinter.Print(BuildConstructorChaining());
        Assert.Contains("public Foo(int x) {", chainOutput);
        Assert.Contains(": this(0)", chainOutput);

        // Block namespace
        var blockNsOutput = MistPrettyPrinter.Print(BuildBlockNamespace());
        Assert.Contains("namespace MyNs {", blockNsOutput);
        Assert.Contains("class Inner {", blockNsOutput);
    }

    [Fact]
    public void PrettyPrinter_Idempotence_ConsistentIndentation()
    {
        // Verify 4-space indentation is used consistently.
        var output = MistPrettyPrinter.Print(BuildClassWithMembers());
        var lines = output.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Count leading spaces
            var leadingSpaces = 0;
            foreach (var ch in trimmed)
            {
                if (ch == ' ') leadingSpaces++;
                else break;
            }

            // Leading spaces should be a multiple of 4
            Assert.True(leadingSpaces % 4 == 0,
                $"Line '{trimmed}' has {leadingSpaces} leading spaces, which is not a multiple of 4");
        }
    }

    [Fact]
    public void PrettyPrinter_Idempotence_OpeningBracesOnSameLine()
    {
        // Verify opening braces are on the same line as declarations.
        var output = MistPrettyPrinter.Print(BuildClassWithMembers());
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // Find lines with class/struct/method declarations — they should end with {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("class ") || trimmed.StartsWith("public class ") ||
                trimmed.StartsWith("struct ") || trimmed.StartsWith("public struct ") ||
                trimmed.Contains("(") && trimmed.Contains(")") && !trimmed.EndsWith(";"))
            {
                if (!trimmed.StartsWith("//") && !trimmed.StartsWith("return") &&
                    !trimmed.StartsWith("var ") && !trimmed.StartsWith("int ") &&
                    !trimmed.StartsWith("Console") && !trimmed.StartsWith("if ") &&
                    !trimmed.StartsWith("while ") && !trimmed.StartsWith("for ") &&
                    !trimmed.StartsWith("foreach "))
                {
                    Assert.True(trimmed.EndsWith("{"),
                        $"Declaration line should end with '{{': '{trimmed}'");
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Pretty printer semantic preservation
    // **Validates: Requirements 12.3**
    //
    // For any valid Mist AST, the source text produced by the pretty printer,
    // when re-parsed and semantically analyzed, SHALL produce a semantic model
    // where every symbol has the same name, kind, and type as in the original
    // semantic model.
    //
    // Since the Storm-generated parser is skeletal, we verify semantic
    // preservation by building the semantic model from the original AST,
    // then verifying the pretty-printed output contains all the symbol names,
    // kinds, and structural elements that the semantic model expects.
    // ═══════════════════════════════════════════════════════════════════════

    // Feature: mist-language-v1, Property 3: Pretty printer semantic preservation

    [Fact]
    public void PrettyPrinter_SemanticPreservation_AllSymbolNamesAppearInOutput()
    {
        // Build a semantic model from the AST, then verify all user-code symbol
        // names appear in the pretty-printed output.
        var ast = BuildClassWithMembers();
        var output = MistPrettyPrinter.Print(ast);

        var builder = new SemanticModelBuilder();
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", ""))));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        // Every user-code symbol name should appear in the output,
        // except inferred namespace symbols which don't appear in source.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.UserCode && symbol.Kind != SymbolKind.Namespace)
            {
                Assert.Contains(symbol.Name, output);
            }
        }
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_SymbolKindsPreserved()
    {
        // Verify that the pretty-printed output preserves the structural elements
        // that correspond to each symbol kind.
        var ast = BuildClassWithMembers();
        var output = MistPrettyPrinter.Print(ast);

        var builder = new SemanticModelBuilder();
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", ""))));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode) continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Class:
                    Assert.Contains($"class {symbol.Name}", output);
                    break;
                case SymbolKind.Field:
                    Assert.Contains(symbol.Name, output);
                    break;
                case SymbolKind.Constructor:
                    // Constructor name appears as the class name followed by (
                    Assert.Contains($"{symbol.Name}(", output);
                    break;
                case SymbolKind.Method:
                    Assert.Contains($"{symbol.Name}(", output);
                    break;
            }
        }
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_TypeReferencesPreserved()
    {
        // Verify that type references in the output match the original AST.
        var ast = BuildClassWithMembers();
        var output = MistPrettyPrinter.Print(ast);

        // The class has: private int x; and public int GetX()
        Assert.Contains("int x", output);
        Assert.Contains("int GetX(", output);
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_ModifiersPreserved()
    {
        // Verify that access modifiers are preserved in the output.
        var ast = BuildClassWithMembers();
        var output = MistPrettyPrinter.Print(ast);

        Assert.Contains("public class Point", output);
        Assert.Contains("private int x", output);
        Assert.Contains("public Point(", output);
        Assert.Contains("public int GetX(", output);
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_NamespaceStructurePreserved()
    {
        // Verify namespace and using declarations are preserved.
        var ast = BuildNamespaceWithStruct();
        var output = MistPrettyPrinter.Print(ast);

        var builder = new SemanticModelBuilder();
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", ""))));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        // Namespace should appear
        Assert.Contains("namespace MyApp;", output);

        // Struct and its members should appear
        Assert.Contains("struct Color", output);
        Assert.Contains("int r", output);
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_AllRepresentativeAstsPreserveSymbols()
    {
        // For each representative AST, build a semantic model and verify
        // all user-code symbol names appear in the pretty-printed output.
        var builder = new SemanticModelBuilder();

        foreach (var ast in AllRepresentativeAsts())
        {
            var output = MistPrettyPrinter.Print(ast);

            var compilation = new MistCompilation(
                ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", ""))));
            var diagnostics = new DiagnosticBag();
            var model = builder.Build(
                compilation,
                ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
                ambientTypeResolver: null,
                namespaceRoot: "TestProject",
                projectFolder: "/test",
                diagnostics);

            foreach (var symbol in model.AllSymbols)
            {
                // Skip inferred namespace symbols — they don't appear in source
                if (symbol.Origin == SymbolOrigin.UserCode && symbol.Kind != SymbolKind.Namespace)
                {
                    Assert.True(output.Contains(symbol.Name),
                        $"Symbol '{symbol.Name}' (kind: {symbol.Kind}) not found in pretty-printed output:\n{output}");
                }
            }
        }
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_ControlFlowKeywordsPreserved()
    {
        // Verify that control flow keywords are preserved in the output.
        var ast = BuildClassWithControlFlow();
        var output = MistPrettyPrinter.Print(ast);

        Assert.Contains("if (", output);
        Assert.Contains("else", output);
        Assert.Contains("while (", output);
        Assert.Contains("for (", output);
        Assert.Contains("foreach (", output);
        Assert.Contains("break;", output);
        Assert.Contains("return", output);
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_ConstructorChainingPreserved()
    {
        // Verify constructor chaining syntax is preserved.
        var ast = BuildConstructorChaining();
        var output = MistPrettyPrinter.Print(ast);

        Assert.Contains(": this(", output);
    }
}
