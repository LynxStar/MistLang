// Feature: void-unit-type, Task 7.7: Unit tests for emitter void scenarios
// Requirements: 10.1, 10.2, 10.3, 10.4, 10.5

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying the C# emitter correctly handles void as a unit type.
///
/// - Void.Void emits MistVoid.Instance (Req 10.1)
/// - Void variables emit MistVoid type (Req 10.2)
/// - Void parameters emit MistVoid type (Req 10.2)
/// - Fire-and-forget void methods emit C# void (Req 10.4)
/// - Value-returning void methods emit MistVoid return type (Req 10.3)
/// - MistVoid struct is emitted when needed (Req 10.1, 10.5)
/// </summary>
public sealed class VoidEmitterTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public VoidEmitterTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistVoidEmitterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

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
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates the expression <c>Void.Void</c> as an AST node.
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
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeMethodCallExpression(string methodName, ExpressionNode[]? args = null)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(methodName) };
        ArgumentListNode? argList = null;
        if (args is { Length: > 0 })
        {
            argList = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = args[0] } };
        }
        var suffix = new InvocationSuffixPostfixSuffixNode { Arguments = argList };
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

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    private static StatementNode MakeTypedVarDeclStatement(
        TypeReferenceNode typeRef, string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = typeRef,
                Name = Tok(varName),
                Initializer = initializer,
            },
        };
    }

    private static StatementNode MakeInferredVarDeclStatement(string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok(varName),
                Initializer = initializer,
            },
        };
    }

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        // Return the content of the main source file (not MistVoid.g.cs or .csproj)
        var csFile = Directory.GetFiles(_outputDir, "*.cs")
            .FirstOrDefault(f => !f.EndsWith("MistVoid.g.cs", StringComparison.Ordinal));
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private string GetMistVoidFileContent()
    {
        var mistVoidPath = Path.Combine(_outputDir, "MistVoid.g.cs");
        return File.Exists(mistVoidPath) ? File.ReadAllText(mistVoidPath) : "";
    }

    private bool MistVoidFileExists()
    {
        return File.Exists(Path.Combine(_outputDir, "MistVoid.g.cs"));
    }

    private void CleanOutputDir()
    {
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    /// <summary>
    /// Builds a standard Program class with the given members plus a static Main method.
    /// </summary>
    private static ClassDeclarationNode MakeProgramClass(ImmutableArray<MemberDeclarationNode> members)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var allMembers = members.Add(
            new MethodMemberMemberDeclarationNode { Method = mainMethod });

        return new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = allMembers,
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(ClassDeclarationNode classDecl)
    {
        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 10.1: Void.Void emits MistVoid.Instance
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_VoidDotVoid_EmitsMistVoidInstance()
    {
        CleanOutputDir();

        // Program: class Program { public static void Main() { var x = Void.Void; } }
        var varDecl = MakeInferredVarDeclStatement("x", MakeVoidDotVoidExpression());

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(varDecl),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(classDecl);
        var source = "class Program { public static void Main() { var x = Void.Void; } }";

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("MistVoid.Instance", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 10.2: Void variable emits MistVoid type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_VoidVariable_EmitsMistVoidType()
    {
        CleanOutputDir();

        // Program: class Program { public static void Main() { void x = Void.Void; } }
        var varDecl = MakeTypedVarDeclStatement(
            new VoidTypeRefTypeReferenceNode(), "x", MakeVoidDotVoidExpression());

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(varDecl),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(classDecl);
        var source = "class Program { public static void Main() { void x = Void.Void; } }";

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        // The typed variable declaration should use MistVoid, not void
        Assert.Contains("MistVoid x = MistVoid.Instance;", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 10.2: Void parameter emits MistVoid type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_VoidParameter_EmitsMistVoidType()
    {
        CleanOutputDir();

        // Program with a method that takes a void parameter:
        // class Program {
        //   public void Accept(void v) { return; }
        //   public static void Main() { }
        // }
        var voidParam = new ParameterNode
        {
            Type = new VoidTypeRefTypeReferenceNode(),
            Name = Tok("v"),
        };
        var paramList = new ParameterListNode { Parameter = voidParam };

        var acceptMethod = new MethodDeclarationNode
        {
            Name = Tok("Accept"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = paramList,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = null },
                    }),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var programClass = MakeProgramClass(
            ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = acceptMethod }));

        var ast = MakeCompilationUnit(programClass);
        var source = "class Program { public void Accept(void v) { return; } public static void Main() { } }";

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        // The parameter should use MistVoid type, not void
        Assert.Contains("MistVoid v", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 10.4: Fire-and-forget void method emits C# void
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_FireAndForgetVoidMethod_EmitsCSharpVoid()
    {
        CleanOutputDir();

        // Program:
        // class Program {
        //   public void DoWork() { return; }
        //   public static void Main() { DoWork(); }
        // }
        var doWorkMethod = new MethodDeclarationNode
        {
            Name = Tok("DoWork"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = null },
                    }),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var callExpr = MakeMethodCallExpression("DoWork");
        var callStmt = MakeExpressionStatement(callExpr);

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(callStmt),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = doWorkMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(classDecl);
        var source = "class Program { public void DoWork() { return; } public static void Main() { DoWork(); } }";

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        // Fire-and-forget: should emit standard C# void, not MistVoid
        Assert.Contains("public void DoWork()", csContent);
        Assert.DoesNotContain("MistVoid DoWork", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 10.3: Value-returning void method emits MistVoid return type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_ValueReturningVoidMethod_EmitsMistVoidReturnType()
    {
        CleanOutputDir();

        // Program:
        // class Program {
        //   public void DoWork() { return; }
        //   public static void Main() { var result = DoWork(); }
        // }
        var doWorkMethod = new MethodDeclarationNode
        {
            Name = Tok("DoWork"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = null },
                    }),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Main: var result = DoWork();
        var callExpr = MakeMethodCallExpression("DoWork");
        var varDecl = MakeInferredVarDeclStatement("result", callExpr);

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(varDecl),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = doWorkMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(classDecl);
        var source = "class Program { public void DoWork() { return; } public static void Main() { var result = DoWork(); } }";

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        // Value-returning: should emit MistVoid return type
        Assert.Contains("public MistVoid DoWork()", csContent);
        // The method body should have return MistVoid.Instance
        Assert.Contains("return MistVoid.Instance;", csContent);
        // The variable declaration should use var
        Assert.Contains("var result = DoWork();", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 10.1, 10.5: MistVoid struct is emitted when needed
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_WhenVoidUsedAsValue_EmitsMistVoidStruct()
    {
        CleanOutputDir();

        // Program: class Program { public static void Main() { var x = Void.Void; } }
        var varDecl = MakeInferredVarDeclStatement("x", MakeVoidDotVoidExpression());

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(varDecl),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(classDecl);
        var source = "class Program { public static void Main() { var x = Void.Void; } }";

        var model = BuildModel(ast, source);
        EmitAndGetCsContent(model);

        // The MistVoid struct should be emitted in a separate shared file
        var mistVoidContent = GetMistVoidFileContent();
        Assert.NotEmpty(mistVoidContent);
        Assert.Contains("public readonly struct MistVoid", mistVoidContent);
        Assert.Contains("System.IEquatable<MistVoid>", mistVoidContent);
        Assert.Contains("public static readonly MistVoid Instance = default;", mistVoidContent);
        Assert.Contains("public bool Equals(MistVoid other) => true;", mistVoidContent);
        Assert.Contains("public override string ToString() => \"Void\";", mistVoidContent);
    }

    [Fact]
    public void Emit_WhenNoVoidValueUsage_DoesNotEmitMistVoidStruct()
    {
        CleanOutputDir();

        // Program with no void value usage — just a simple void method called fire-and-forget
        // class Program {
        //   public void DoWork() { return; }
        //   public static void Main() { DoWork(); }
        // }
        var doWorkMethod = new MethodDeclarationNode
        {
            Name = Tok("DoWork"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = null },
                    }),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var callExpr = MakeMethodCallExpression("DoWork");
        var callStmt = MakeExpressionStatement(callExpr);

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(callStmt),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = doWorkMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(classDecl);
        var source = "class Program { public void DoWork() { return; } public static void Main() { DoWork(); } }";

        var model = BuildModel(ast, source);
        EmitAndGetCsContent(model);

        // No void value usage → MistVoid.g.cs file should NOT be emitted
        Assert.False(MistVoidFileExists());
    }
}
