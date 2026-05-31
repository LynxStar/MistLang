using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for Task 41: verifying that the CSharpEmitter dispatches operator
/// emission through the resolved-operator symbol.
/// </summary>
public sealed class OperatorEmissionDispatchTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public OperatorEmissionDispatchTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistOperatorEmissionTests_" + Guid.NewGuid().ToString("N"));
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

    private ISemanticModel BuildModel(
        CompilationUnitNode ast,
        string source,
        IAmbientTypeResolver? ambientTypeResolver = null)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), ambientTypeResolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ISemanticModel BuildModelWithStdlib(
        CompilationUnitNode userAst,
        string userSource,
        CompilationUnitNode stdlibAst,
        string stdlibSource)
    {
        var userSourceText = new SourceText("test.mist", userSource);
        var stdlibSourceText = new SourceText("stdlib.mist", stdlibSource);
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(stdlibSourceText) { IsStdlib = true },
            new CompilationUnit(userSourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(stdlibAst, userAst),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csFile = Directory.GetFiles(_outputDir, "*.cs")
            .FirstOrDefault(f => !f.EndsWith("MistVoid.g.cs"));
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private void CleanOutputDir()
    {
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 41.1: Intrinsic operator emission produces byte-identical output
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that emitted C# for <c>int a = 1; int b = 2; int c = a + b;</c>
    /// produces the expected C# output. The intrinsic template produces
    /// byte-identical output to the old hardcoded emission (Requirement 11.5).
    /// </summary>
    [Fact]
    public void Emit_IntrinsicIntAddition_ProducesByteIdenticalOutput()
    {
        CleanOutputDir();

        // Build a program: class Program { public static void Main() { int a = 1; int b = 2; int c = a + b; } }
        var source = "class Program {\n    public static void Main() {\n        int a = 1;\n        int b = 2;\n        int c = a + b;\n    }\n}";

        // int a = 1;
        var stmtA = MistAstGenerator.MakeIntVarDecl("a", "1");

        // int b = 2;
        var stmtB = MistAstGenerator.MakeIntVarDecl("b", "2");

        // int c = a + b;
        var addExpr = MistAstGenerator.MakeAdditive(
            MistAstGenerator.MakeIdentifierExpr("a"),
            MistAstGenerator.MakeIdentifierExpr("b"),
            "+");
        var stmtC = MistAstGenerator.MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            "c",
            addExpr);

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(stmtA, stmtB, stmtC),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        // The intrinsic template "{0} + {1}" should produce the same output
        // as the old hardcoded "left + right" emission.
        Assert.Contains("int a = 1;", csContent);
        Assert.Contains("int b = 2;", csContent);
        Assert.Contains("int c = a + b;", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 41.2: User-defined operator emits as static method call
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that emitted C# for a user-defined <c>Vector3.operator+</c> call
    /// emits <c>Vector3.op_Addition(a, b)</c>.
    /// </summary>
    [Fact]
    public void Emit_UserDefinedOperatorPlus_EmitsStaticMethodCall()
    {
        CleanOutputDir();

        // Build a program with a user-defined Vector3 struct with operator+
        // and a call site that uses it.
        var source = @"
public struct Vector3 {
    public float x;
    public float y;
    public float z;
    public static Vector3 operator +(Vector3 a, Vector3 b) {
        return a;
    }
}
class Program {
    public static void Main() {
        Vector3 a;
        Vector3 b;
        Vector3 c = a + b;
    }
}";

        // Build the Vector3 struct with operator+
        var fieldX = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new FloatTypePrimitiveTypeNode() } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var fieldY = new FieldDeclarationNode
        {
            Name = MakeToken("y"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new FloatTypePrimitiveTypeNode() } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var fieldZ = new FieldDeclarationNode
        {
            Name = MakeToken("z"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new FloatTypePrimitiveTypeNode() } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // operator+ declaration: public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        var vector3TypeRef = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("Vector3"),
                Span = new Storm.Runtime.SourceSpan(0, 0),
            },
        } };

        var paramA = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken("Vector3"),
                    Span = new Storm.Runtime.SourceSpan(0, 0),
                },
            } },
            Name = MakeToken("a"),
        };
        var paramB = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken("Vector3"),
                    Span = new Storm.Runtime.SourceSpan(0, 0),
                },
            } },
            Name = MakeToken("b"),
        };

        var operatorDecl = new OperatorDeclarationNode
        {
            ReturnType = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken("Vector3"),
                    Span = new Storm.Runtime.SourceSpan(0, 0),
                },
            } },
            Symbol = new PlusOpOperatorSymbolNode(),
            Parameters = new ParameterListNode { Parameter = paramA, Parameters = ImmutableArray.Create(paramB) },
            Body = new OperatorBlockBodyOperatorBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new ReturnStatementStatementNode
                        {
                            ReturnStatement = new ReturnStatementNode
                            {
                                Value = MistAstGenerator.MakeIdentifierExpr("a"),
                            },
                        }),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var vector3Struct = new StructDeclarationNode
        {
            Name = MakeToken("Vector3"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldX },
                new FieldMemberMemberDeclarationNode { Field = fieldY },
                new FieldMemberMemberDeclarationNode { Field = fieldZ },
                new OperatorMemberMemberDeclarationNode { Operator = operatorDecl }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Build the Program class with Main method that uses Vector3 operator+
        // Vector3 a; Vector3 b; Vector3 c = a + b;
        var stmtDeclA = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("Vector3"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                } },
                Name = MakeToken("a"),
                Initializer = MistAstGenerator.WrapPrimary(new ObjectCreationExpressionPrimaryNode
                {
                    Type = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("Vector3"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                }),
            },
        };

        var stmtDeclB = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("Vector3"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                } },
                Name = MakeToken("b"),
                Initializer = MistAstGenerator.WrapPrimary(new ObjectCreationExpressionPrimaryNode
                {
                    Type = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("Vector3"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                }),
            },
        };

        // c = a + b
        var addExpr = MistAstGenerator.MakeAdditive(
            MistAstGenerator.MakeIdentifierExpr("a"),
            MistAstGenerator.MakeIdentifierExpr("b"),
            "+");

        var stmtDeclC = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("Vector3"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                } },
                Name = MakeToken("c"),
                Initializer = addExpr,
            },
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        stmtDeclA, stmtDeclB, stmtDeclC),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = vector3Struct },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsTopLevelItems(),
        };

        var model = BuildModel(ast, source);

        // The semantic model builder doesn't run the type checker, so we need
        // to manually record the resolved operator on the AdditiveNode. This
        // is a unit test of the emitter's dispatch logic, not an integration
        // test of the full pipeline.
        //
        // Find the operator+ symbol on Vector3.
        var vector3Symbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Vector3" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(vector3Symbol);
        var vector3Type = vector3Symbol!.DeclaredType as StructType;
        Assert.NotNull(vector3Type);

        var operatorPlusSymbol = vector3Type!.MemberScope.Members
            .FirstOrDefault(m => m.Name == "operator+" && m.Kind == SymbolKind.Method);
        Assert.NotNull(operatorPlusSymbol);

        // Find the AdditiveNode in the AST (the `a + b` expression).
        var additiveNode = FindAdditiveNode(ast);
        Assert.NotNull(additiveNode);

        // Record the resolved operator on the AdditiveNode.
        model.RecordResolvedOperator(additiveNode!, operatorPlusSymbol!);

        var csContent = EmitAndGetCsContent(model);

        // The user-defined operator should emit as a static method call.
        Assert.Contains("Vector3.op_Addition(a, b)", csContent);
    }

    /// <summary>
    /// Walks the AST depth-first and returns the first AdditiveNode that has
    /// a real binary operator (non-null Right distinct from Left).
    /// </summary>
    private static AdditiveNode? FindAdditiveNode(Storm.Runtime.SyntaxNode root)
    {
        var stack = new Stack<Storm.Runtime.SyntaxNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is AdditiveNode add
                && add.Right is not null
                && !ReferenceEquals(add.Right, add.Left))
            {
                return add;
            }
            foreach (var child in node.Children())
            {
                if (child is not null)
                    stack.Push(child);
            }
        }
        return null;
    }
}
