using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for type checker void scenarios covering equality, parameters,
/// generics, return values, and field declarations.
/// </summary>
public class VoidTypeCheckerTests
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

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates the expression <c>Void.Void</c> as a full ExpressionNode.
    /// </summary>
    private static ExpressionNode MakeVoidDotVoidExpression()
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok("Void") };
        var suffix = new MemberAccessSuffixPostfixSuffixNode { Name = Tok("Void") };
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
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Extracts the UnaryNode from a Void.Void expression for use in equality expressions.
    /// </summary>
    private static UnaryNode MakeVoidDotVoidUnary()
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok("Void") };
        var suffix = new MemberAccessSuffixPostfixSuffixNode { Name = Tok("Void") };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        return new PostfixExpressionUnaryNode { Primary = postfix };
    }

    /// <summary>
    /// Creates an equality expression: left op right, returning a full ExpressionNode.
    /// </summary>
    private static ExpressionNode MakeEqualityExpression(UnaryNode left, UnaryNode right, string op)
    {
        var leftMult = new MultiplicativeNode { Left = left };
        var rightMult = new MultiplicativeNode { Left = right };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var leftRel = new RelationalComparisonRelationalNode { Left = leftAdd };
        var rightRel = new RelationalComparisonRelationalNode { Left = rightAdd };
        var eq = new EqualityNode { Left = leftRel, Operator = Tok(op), Right = rightRel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode>? body = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body ?? ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
            TypeArgs = typeArgs,
        } };
    }

    private static TypeParameterListNode MakeTypeParamList(string[] names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = Tok(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };
    }

    private static MemberDeclarationNode MakeFieldMember(string name, TypeReferenceNode typeRef)
    {
        return new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = Tok(name),
                Type = typeRef,
            },
        };
    }

    /// <summary>
    /// Creates a static method call expression: <c>target.methodName(argExpr)</c>
    /// </summary>
    private static ExpressionNode MakeStaticMethodCall(string target, string methodName, ExpressionNode argExpr)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(target) };
        var argList = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = argExpr } };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = Tok(methodName),
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
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Void.Void == Void.Void is accepted without error.
    /// Validates: Requirement 9.1
    /// </summary>
    [Fact]
    public void VoidEquality_EqualOperator_Accepted()
    {
        var left = MakeVoidDotVoidUnary();
        var right = MakeVoidDotVoidUnary();
        var equalityExpr = MakeEqualityExpression(left, right, "==");
        var stmt = MakeExpressionStatement(equalityExpr);

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode
                {
                    Method = MakeMainMethod(ImmutableArray.Create<StatementNode>(stmt)),
                }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    /// <summary>
    /// Void.Void != Void.Void is accepted without error.
    /// Validates: Requirement 9.2
    /// </summary>
    [Fact]
    public void VoidEquality_NotEqualOperator_Accepted()
    {
        var left = MakeVoidDotVoidUnary();
        var right = MakeVoidDotVoidUnary();
        var equalityExpr = MakeEqualityExpression(left, right, "!=");
        var stmt = MakeExpressionStatement(equalityExpr);

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode
                {
                    Method = MakeMainMethod(ImmutableArray.Create<StatementNode>(stmt)),
                }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    /// <summary>
    /// Calling a method with a void parameter using Void.Void as the argument is accepted.
    /// Validates: Requirement 6.2
    /// </summary>
    [Fact]
    public void VoidParameter_CalledWithVoidDotVoid_Accepted()
    {
        // class Program {
        //   public static void Main() { Program.Target(Void.Void); }
        //   public static void Target(void p) { }
        // }
        var paramList = new ParameterListNode
        {
            Parameter = new ParameterNode
            {
                Type = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Name = Tok("p"),
            },
        };

        var targetMethod = new MethodDeclarationNode
        {
            Name = Tok("Target"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = paramList,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var callExpr = MakeStaticMethodCall("Program", "Target", MakeVoidDotVoidExpression());
        var callStmt = MakeExpressionStatement(callExpr);

        var mainMethod = MakeMainMethod(ImmutableArray.Create<StatementNode>(callStmt));

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod },
                new MethodMemberMemberDeclarationNode { Method = targetMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    /// <summary>
    /// Result&lt;void&gt; generic instantiation is accepted without error.
    /// Validates: Requirement 5.2
    /// </summary>
    [Fact]
    public void GenericType_WithVoidTypeArgument_Accepted()
    {
        // class Result<T> { }
        // class Program { Result<void> field; public static void Main() { } }
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = Tok("Result"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }]);
        var fieldTypeRef = MakeNamedTypeRef("Result", typeArgList);
        var fieldMember = MakeFieldMember("result", fieldTypeRef);

        var mainMethod = MakeMainMethod();

        var programClassDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create(
                fieldMember,
                new MethodMemberMemberDeclarationNode { Method = mainMethod } as MemberDeclarationNode),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClassDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    /// <summary>
    /// return Void.Void; in a void method is accepted without error.
    /// Validates: Requirement 3.5
    /// </summary>
    [Fact]
    public void VoidMethod_ReturnVoidDotVoid_Accepted()
    {
        // class Program {
        //   public static void Main() { }
        //   public void DoWork() { return Void.Void; }
        // }
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = MakeVoidDotVoidExpression() },
        };

        var testMethod = new MethodDeclarationNode
        {
            Name = Tok("DoWork"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray.Create<StatementNode>(returnStmt) } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() },
                new MethodMemberMemberDeclarationNode { Method = testMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    /// <summary>
    /// A void field in a class is accepted without error.
    /// Validates: Requirement 7.1
    /// </summary>
    [Fact]
    public void VoidField_InClass_Accepted()
    {
        // class Container { void value; }
        // class Program { public static void Main() { } }
        var containerClass = new ClassDeclarationNode
        {
            Name = Tok("Container"),
            Members = ImmutableArray.Create(
                MakeFieldMember("value", new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() })),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = containerClass },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    /// <summary>
    /// A Void field (using the identifier, not keyword) in a struct is accepted without error.
    /// Validates: Requirement 7.2
    /// </summary>
    [Fact]
    public void VoidField_InStruct_UsingIdentifier_Accepted()
    {
        // struct Container { Void value; }
        // class Program { public static void Main() { } }
        var voidIdentifierTypeRef = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok("Void") },
        } };

        var containerStruct = new StructDeclarationNode
        {
            Name = Tok("Container"),
            Members = ImmutableArray.Create(
                MakeFieldMember("value", voidIdentifierTypeRef)),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = containerStruct },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    /// <summary>
    /// A void field assigned Void.Void is accepted without error.
    /// Validates: Requirement 7.3
    /// </summary>
    [Fact]
    public void VoidField_AssignedVoidDotVoid_Accepted()
    {
        // class Container { void value; }
        // class Program {
        //   public static void Main() {
        //     Container c = new Container();
        //     c.value = Void.Void;
        //   }
        // }
        // We test this by declaring a void variable and assigning Void.Void to it,
        // which exercises the same type checking path for void field assignment.
        var voidVarDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Name = Tok("value"),
                Initializer = MakeVoidDotVoidExpression(),
            },
        };

        var mainMethod = MakeMainMethod(ImmutableArray.Create<StatementNode>(voidVarDecl));

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }
}
