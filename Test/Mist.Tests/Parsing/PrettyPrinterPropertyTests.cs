// Feature: mist-language-v1, Property 2: Pretty printer idempotence
// Feature: mist-language-v1, Property 3: Pretty printer semantic preservation
// **Validates: Requirements 12.2, 12.3, 12.4**

using System.Collections.Immutable;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

[Trait("Tier", "2")]
public class PrettyPrinterPropertyTests
{
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

    private static ExpressionNode MakeMethodCallExpression(string target, string methodName, params ExpressionNode[] args)
    {
        ArgumentListNode? argList = null;
        if(args.Length > 0)
        {
            argList = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = args[0] } };
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
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeAssignmentExpression(string target, ExpressionNode value)
    {
        var targetLogOr = MakeIdentifierExpression(target).AsAssignment().Target.Left;
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = targetLogOr }, Group0 = new AssignmentNodeGroup0 { Value = value } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode WrapPrimaryInExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeBinaryExpression(ExpressionNode left, string op, ExpressionNode right)
    {
        var leftUnary = ((RelationalComparisonRelationalNode)left.AsAssignment().Target.Left.Left.Left.Left).Left.Left.Left.UnwrapRange()!;
        var rightUnary = ((RelationalComparisonRelationalNode)right.AsAssignment().Target.Left.Left.Left.Left).Left.Left.Left.UnwrapRange()!;

        var add = new AdditiveNode
        {
            Left = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = leftUnary } },
            Group0 = ImmutableArray.Create(new AdditiveNodeGroup0 { Operator = MakeToken(op), Right = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = rightUnary } } }),
        };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
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

    private static CompilationUnitNode BuildEmptyClass()
    {
        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode
                {
                    TypeBody = new TypeBodyDeclarationNode
                    {
                        Keyword = new ClassKeywordTypeKeywordNode(),
                        Name = MakeToken("Empty"),
                        Members = ImmutableArray<MemberDeclarationNode>.Empty,
                    },
                }).AsTopLevelItems(),
        };
    }

    private static CompilationUnitNode BuildClassWithMembers()
    {
        var fieldDecl = new FieldDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("x"),
        };

        var ctorParam = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("x"),
        };

        // this.x = x;
        _ = MakeAssignmentExpression("x", MakeIdentifierExpression("x"));
        var thisPrimary = new ThisExpressionPrimaryNode();
        var memberSuffix = new MemberAccessSuffixPostfixSuffixNode { Name = MakeToken("x") };
        var postfix = new PostfixNode
        {
            Target = thisPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberSuffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var targetLogOr = new LogicalOrNode { Left = logAnd };
        var assignExpr = new AssignmentFormExpressionNode
        {
            Assignment = new AssignmentNode
            {
                Target = new VoidCoalescingNode { Left = targetLogOr },
                Group0 = new AssignmentNodeGroup0 { Value = MakeIdentifierExpression("x") },
            },
        };

        var ctorDecl = new ConstructorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Point"),
            Parameters = new ParameterListNode { Parameter = ctorParam },
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                    MakeExpressionStatement(assignExpr)),
                }
            },
        };

        var methodDecl = new MethodDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("GetX"),
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                    MakeReturnStatement(MakeIdentifierExpression("x"))),
                }
            },
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
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
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

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
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("r"),
        };

        var structDecl = new TypeBodyDeclarationNode
        {
            Keyword = new StructKeywordTypeKeywordNode(),
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
                new BodyTypeTypeDeclarationNode { TypeBody = structDecl }).AsNamespaceBodyItems(),
        };

        return new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };
    }

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
                    VariableDeclaration = new TypeReferenceNode
                    {
                        First = new PrimitiveSingleTypeRefSingleTypeRefNode
                        {
                            PrimitiveType = new IntTypePrimitiveTypeNode(),
                        }
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
            ForeachStatement = new TypedForeachForeachStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
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
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Name = MakeToken("Run"),
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                    ifStmt,
                    whileStmt,
                    forStmt,
                    foreachStmt),
                }
            },
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    private static CompilationUnitNode BuildClassWithExpressions()
    {
        var varDecl = MakeVarDecl("x", MakeIntLiteralExpression("42"));

        // int y = x + 1;
        var addExpr = MakeBinaryExpression(MakeIdentifierExpression("x"), "+", MakeIntLiteralExpression("1"));
        var yDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Name = MakeToken("y"),
                Initializer = addExpr,
            },
        };

        var callStmt = MakeExpressionStatement(MakeMethodCallExpression("Console", "WriteLine"));

        var returnStmt = MakeReturnStatement();

        var methodDecl = new MethodDeclarationNode
        {
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Name = MakeToken("Test"),
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                    varDecl,
                    yDecl,
                    callStmt,
                    returnStmt),
                }
            },
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Expressions"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    private static CompilationUnitNode BuildConstructorChaining()
    {
        var paramX = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("x"),
        };

        var ctor1 = new ConstructorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Foo"),
            Parameters = new ParameterListNode { Parameter = paramX },
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode() },
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
                    Argument = new ValueArgumentArgumentNode { Expression = MakeIntLiteralExpression("0") },
                },
            },
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode() },
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Name = MakeToken("Foo"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctor1 },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctor2 }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    private static CompilationUnitNode BuildBlockNamespace()
    {
        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
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
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsNamespaceBodyItems(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };
    }

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

    [Fact]
    public void PrettyPrinter_Idempotence_PrintingTwiceProducesIdenticalOutput()
    {
        foreach(var ast in AllRepresentativeAsts())
        {
            var output1 = MistPrettyPrinter.Print(ast);
            var output2 = MistPrettyPrinter.Print(ast);

            Assert.Equal(output1, output2);
        }
    }

    [Fact]
    public void PrettyPrinter_Idempotence_OutputIsNonEmpty()
    {
        foreach(var ast in AllRepresentativeAsts())
        {
            var output = MistPrettyPrinter.Print(ast);
            Assert.False(string.IsNullOrWhiteSpace(output),
                "Pretty printer should produce non-empty output for valid ASTs");
        }
    }

    [Fact]
    public void PrettyPrinter_Idempotence_OutputContainsExpectedSyntax()
    {
        var emptyClassOutput = MistPrettyPrinter.Print(BuildEmptyClass());
        Assert.Contains("class Empty {", emptyClassOutput);
        Assert.Contains("}", emptyClassOutput);

        var membersOutput = MistPrettyPrinter.Print(BuildClassWithMembers());
        Assert.Contains("public class Point {", membersOutput);
        Assert.Contains("private int x;", membersOutput);
        Assert.Contains("public Point(int x) {", membersOutput);
        Assert.Contains("public int GetX() {", membersOutput);
        Assert.Contains("return x;", membersOutput);

        var nsOutput = MistPrettyPrinter.Print(BuildNamespaceWithStruct());
        Assert.Contains("using System;", nsOutput);
        Assert.Contains("namespace MyApp;", nsOutput);
        Assert.Contains("public struct Color {", nsOutput);
        Assert.Contains("public int r;", nsOutput);

        var cfOutput = MistPrettyPrinter.Print(BuildClassWithControlFlow());
        Assert.Contains("if (x) {", cfOutput);
        Assert.Contains("} else {", cfOutput);
        Assert.Contains("while (true) {", cfOutput);
        Assert.Contains("break;", cfOutput);
        Assert.Contains("for (int i = 0; ; ) {", cfOutput);
        Assert.Contains("foreach (int item in items) {", cfOutput);

        var exprOutput = MistPrettyPrinter.Print(BuildClassWithExpressions());
        Assert.Contains("var x = 42;", exprOutput);
        Assert.Contains("int y = x + 1;", exprOutput);
        Assert.Contains("Console.WriteLine();", exprOutput);
        Assert.Contains("return;", exprOutput);

        var chainOutput = MistPrettyPrinter.Print(BuildConstructorChaining());
        Assert.Contains("public Foo(int x) {", chainOutput);
        Assert.Contains(": this(0)", chainOutput);

        var blockNsOutput = MistPrettyPrinter.Print(BuildBlockNamespace());
        Assert.Contains("namespace MyNs {", blockNsOutput);
        Assert.Contains("class Inner {", blockNsOutput);
    }

    [Fact]
    public void PrettyPrinter_Idempotence_ConsistentIndentation()
    {
        var output = MistPrettyPrinter.Print(BuildClassWithMembers());
        var lines = output.Split('\n');

        foreach(var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if(string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var leadingSpaces = 0;
            foreach(var ch in trimmed)
            {
                if(ch == ' ')
                {
                    leadingSpaces++;
                }
                else
                {
                    break;
                }
            }

            Assert.True(leadingSpaces % 4 == 0,
                $"Line '{trimmed}' has {leadingSpaces} leading spaces, which is not a multiple of 4");
        }
    }

    [Fact]
    public void PrettyPrinter_Idempotence_OpeningBracesOnSameLine()
    {
        var output = MistPrettyPrinter.Print(BuildClassWithMembers());
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // Find lines with class/struct/method declarations — they should end with {
        foreach(var line in lines)
        {
            var trimmed = line.Trim();
            if(trimmed.StartsWith("class ") || trimmed.StartsWith("public class ") ||
                trimmed.StartsWith("struct ") || trimmed.StartsWith("public struct ") ||
                (trimmed.Contains("(") && trimmed.Contains(")") && !trimmed.EndsWith(";")))
            {
                if(!trimmed.StartsWith("//") && !trimmed.StartsWith("return") &&
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

    [Fact]
    public void PrettyPrinter_SemanticPreservation_TypeReferencesPreserved()
    {
        var ast = BuildClassWithMembers();
        var output = MistPrettyPrinter.Print(ast);

        Assert.Contains("int x", output);
        Assert.Contains("int GetX(", output);
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_ModifiersPreserved()
    {
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
        var ast = BuildNamespaceWithStruct();
        var output = MistPrettyPrinter.Print(ast);

        Assert.Contains("namespace MyApp;", output);

        Assert.Contains("struct Color", output);
        Assert.Contains("int r", output);
    }

    [Fact]
    public void PrettyPrinter_SemanticPreservation_ControlFlowKeywordsPreserved()
    {
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
        var ast = BuildConstructorChaining();
        var output = MistPrettyPrinter.Print(ast);

        Assert.Contains(": this(", output);
    }
}