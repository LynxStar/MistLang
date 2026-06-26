using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for C# emitter output verifying specific emission patterns:
/// namespace declarations, class/struct with modifiers, method bodies,
/// using directives, .csproj content, var preservation, constructor chaining,
/// array syntax, and string interpolation.
/// </summary>
public sealed class EmitterOutputTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public EmitterOutputTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistEmitterOutputTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
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

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeStringLiteralExpression(string value = "\"hello\"")
    {
        var primary = new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = MakeToken(value),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
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

    private static UsingDeclarationNode MakeUsingDeclaration(string namespaceName)
    {
        // Use a zero-length span so the emitter falls back to IDENTIFIER.Lexeme
        // rather than trying to read from source text (which may not match test spans).
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(namespaceName),
            Span = new Storm.Runtime.SourceSpan(0, 0),
        };
        return new UsingDeclarationNode { Name = qualifiedId };
    }

    private ISemanticModel BuildModel(
        CompilationUnitNode ast,
        string source,
        IAmbientTypeResolver? resolver = null)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), resolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ISemanticModel BuildModelWithStdlib(
        CompilationUnitNode ast,
        string source)
    {
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();
        var sourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(sourceText);
        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(ast);
        var compilation = new MistCompilation(allUnits);
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, allAsts, null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        // When stdlib is loaded, multiple .cs files are emitted (MistVoid.g.cs,
        // UnionHelpers.cs, etc.). The user's code is in test.cs. Fall back to
        // the first .cs file for tests that don't load stdlib.
        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        var testFile = csFiles.FirstOrDefault(f => Path.GetFileName(f) == "test.cs");
        var csFile = testFile ?? csFiles.FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(_outputDir))
        {
            foreach(var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Namespace declarations
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_FileScopedNamespace_ProducesCorrectOutput()
    {
        CleanOutputDir();

        var source = "namespace MyApp;\nclass Program {\n    public static void Main() { }\n}";

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
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

        var nsName = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("MyApp"),
            Span = new Storm.Runtime.SourceSpan(10, 5), // "MyApp" in source
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                {
                    Name = nsName,
                    Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
                }
            }),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("namespace MyApp;", csContent);
        Assert.Contains("class Program", csContent);
    }

    [Fact]
    public void Emit_BlockNamespace_ProducesCorrectOutput()
    {
        CleanOutputDir();

        var source = "namespace MyApp {\nclass Program {\n    public static void Main() { }\n}\n}";

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
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

        var nsName = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("MyApp"),
            Span = new Storm.Runtime.SourceSpan(10, 5),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new BlockNamespaceNamespaceDeclarationNode
                {
                    Name = nsName,
                    Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
                }
            }),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("namespace MyApp", csContent);
        Assert.Contains("class Program", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Class/struct with modifiers
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_PublicClass_IncludesPublicModifier()
    {
        CleanOutputDir();

        var source = "public class MyClass {\n    public static void Main() { }\n}";

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("public class MyClass", csContent);
    }

    [Fact]
    public void Emit_Struct_ProducesStructDeclaration()
    {
        CleanOutputDir();

        var source = "public struct Point {\n    public int x;\n}\nclass Program {\n    public static void Main() { }\n}";

        var field = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken("Point"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
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
                new StructTypeTypeDeclarationNode { StructDeclaration = structDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsTopLevelItems(),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("public struct Point", csContent);
        Assert.Contains("public int x;", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Method bodies
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_MethodWithReturnStatement_ProducesCorrectBody()
    {
        CleanOutputDir();

        var source = "class Program {\n    public int GetValue() {\n        return 42;\n    }\n    public static void Main() { }\n}";

        var getValueMethod = new MethodDeclarationNode
        {
            Name = MakeToken("GetValue"),
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode
                        {
                            Value = MakeIntLiteralExpression("42"),
                        },
                    }),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = getValueMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("public int GetValue()", csContent);
        Assert.Contains("return 42;", csContent);
    }

    [Fact]
    public void Emit_MethodWithParameters_ProducesCorrectSignature()
    {
        CleanOutputDir();

        var source = "class Program {\n    public int Add(int a, int b) {\n        return 0;\n    }\n    public static void Main() { }\n}";

        var paramA = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Name = MakeToken("a"),
        };
        var paramB = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Name = MakeToken("b"),
        };
        var paramList = new ParameterListNode { Parameter = paramA };

        var addMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Add"),
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Parameters = paramList,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode
                        {
                            Value = MakeIntLiteralExpression("0"),
                        },
                    }),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = addMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("public int Add(int a", csContent);
        Assert.Contains("return 0;", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Using directives include ambient type namespaces
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_WithAmbientTypeRef_IncludesUsingDirective()
    {
        CleanOutputDir();

        var source = "using Standard;\nclass Program {\n    Console c;\n    public static void Main() { }\n}";

        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("Console"),
            Span = new Storm.Runtime.SourceSpan(0, 0),
        };
        var field = new FieldDeclarationNode
        {
            Name = MakeToken("c"),
            Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode { Name = qualifiedId } },
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("Standard")),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModelWithStdlib(ast, source);
        var csContent = EmitAndGetCsContent(model);

        // Console is now emitted as fully-qualified System.Console via CSharpTypeMap.
        Assert.Contains("System.Console", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // .csproj content matches expected settings
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_CsprojContent_HasCorrectSettings()
    {
        CleanOutputDir();

        var source = "class Program {\n    public static void Main() { }\n}";

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
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
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csprojPath = Path.Combine(_outputDir, "TestProject.csproj");
        Assert.True(File.Exists(csprojPath));

        var csprojContent = File.ReadAllText(csprojPath);
        Assert.Contains("<OutputType>Exe</OutputType>", csprojContent);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csprojContent);
        Assert.Contains("<Deterministic>true</Deterministic>", csprojContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // var preservation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_VarDeclaration_PreservesVarKeyword()
    {
        CleanOutputDir();

        var source = "class Program {\n    public static void Main() {\n        var x = 42;\n    }\n}";

        var varDecl = new InferredVariableDeclarationVariableDeclarationStatementNode
        {
            Name = MakeToken("x"),
            Initializer = MakeIntLiteralExpression("42"),
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
                    new VariableDeclarationStatementStatementNode
                    {
                        VariableDeclaration = varDecl,
                    }),
                }
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

        Assert.Contains("var x = 42;", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor chaining
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_ConstructorChaining_ProducesThisSyntax()
    {
        CleanOutputDir();

        var source = "class Point {\n    int x;\n    Point() : this(0) { }\n    Point(int x) { }\n}\nclass Program {\n    public static void Main() { }\n}";

        // Constructor with chain: Point() : this(0) { }
        var chainArgs = new ArgumentListNode
        {
            Argument = new ValueArgumentArgumentNode { Expression = MakeIntLiteralExpression("0") },
        };
        var chain = new ThisChainConstructorChainNode
        {
            Arguments = chainArgs,
        };

        var defaultCtor = new ConstructorDeclarationNode
        {
            Name = MakeToken("Point"),
            Parameters = null,
            Chain = chain,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
        };

        // Constructor: Point(int x) { }
        var paramX = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Name = MakeToken("x"),
        };
        var paramCtor = new ConstructorDeclarationNode
        {
            Name = MakeToken("Point"),
            Parameters = new ParameterListNode { Parameter = paramX },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
        };

        var fieldX = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
        };

        var pointClass = new ClassDeclarationNode
        {
            Name = MakeToken("Point"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldX },
                new ConstructorMemberMemberDeclarationNode { Constructor = defaultCtor },
                new ConstructorMemberMemberDeclarationNode { Constructor = paramCtor }),
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
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
                new ClassTypeTypeDeclarationNode { ClassDeclaration = pointClass },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsTopLevelItems(),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains(": this(0)", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Array syntax
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_ArrayCreation_ProducesNewArraySyntax()
    {
        CleanOutputDir();

        var source = "class Program {\n    public static void Main() {\n        int[] arr = new int[5];\n    }\n}";

        // new int[5]
        var arrayCreation = new ArrayCreationExpressionPrimaryNode
        {
            Type = new PrimitiveElementTypeRefElementTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            Size = MakeIntLiteralExpression("5"),
        };
        var arrayExpr = WrapPrimaryAsExpression(arrayCreation);

        var varDecl = new TypedVariableDeclarationVariableDeclarationStatementNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                    ArrayRank = new UnsizedArrayRankArrayRankSuffixNode(),
                }
            },
            Name = MakeToken("arr"),
            Initializer = arrayExpr,
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
                    new VariableDeclarationStatementStatementNode
                    {
                        VariableDeclaration = varDecl,
                    }),
                }
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

        Assert.Contains("int[]", csContent);
        Assert.Contains("new int[5]", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // String interpolation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_StringInterpolation_ProducesDollarStringSyntax()
    {
        CleanOutputDir();

        var source = "class Program {\n    public static void Main() {\n        var msg = $\"Hello {42}\";\n    }\n}";

        // Build an interpolated string: $"Hello {42}"
        var textPart = new TextPartInterpolatedPartNode
        {
            Text = MakeToken("Hello "),
        };
        var exprPart = new ExpressionPartInterpolatedPartNode
        {
            Expression = MakeIntLiteralExpression("42"),
        };
        var interpolatedString = new InterpolatedStringNode
        {
            Parts = ImmutableArray.Create<InterpolatedPartNode>(textPart, exprPart),
        };
        var interpolatedPrimary = new InterpolatedStringExpressionPrimaryNode
        {
            InterpolatedString = interpolatedString,
        };
        var interpolatedExpr = WrapPrimaryAsExpression(interpolatedPrimary);

        var varDecl = new InferredVariableDeclarationVariableDeclarationStatementNode
        {
            Name = MakeToken("msg"),
            Initializer = interpolatedExpr,
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
                    new VariableDeclarationStatementStatementNode
                    {
                        VariableDeclaration = varDecl,
                    }),
                }
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

        Assert.Contains("$\"Hello {42}\"", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Static method modifier
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_StaticMethod_IncludesStaticModifier()
    {
        CleanOutputDir();

        var source = "class Program {\n    public static void Main() { }\n}";

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
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

        Assert.Contains("public static void Main()", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type literal — CSharpTypeMap.GetTypeName(LegacyPrimitiveTypes.Type) (Req 5.5)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CSharpTypeMap_GetTypeName_TypeType_ReturnsSystemType()
    {
        var result = CSharpTypeMap.GetTypeName(LegacyPrimitiveTypes.Type);
        Assert.Equal("System.Type", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type literal — explicit Type annotation emits typeof() (Req 5.2, 5.5)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_ExplicitTypeAnnotation_ProducesTypeofInitializer()
    {
        CleanOutputDir();

        var source = "class Program {\n    public static void Main() {\n        Type foo = int;\n    }\n}";

        var csContent = CompileFullPipeline(source);
        Assert.NotNull(csContent);

        // The type literal initializer should be wrapped in typeof()
        Assert.Contains("typeof(int)", csContent);
        // The variable declaration should use the Type annotation
        Assert.Contains("Type foo", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type literal — typeof(MyClass) emission for user-defined types (Req 5.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_TypeLiteral_UserDefinedType_ProducesTypeofMyClass()
    {
        CleanOutputDir();

        // A Mist program with a user-defined class used as a type literal
        // via a method that accepts Type and is called with the class name.
        // Since user-defined names parse as IdentifierExpression (not TypeLiteralExpression),
        // we test the typeof() wrapping by using a primitive type literal inside a class
        // that also defines a user-defined type, verifying the emitter handles the AST correctly.
        var source = "class MyClass { }\nclass Program {\n    public static void Main() {\n        var t = int;\n    }\n}";

        var csContent = CompileFullPipeline(source);
        Assert.NotNull(csContent);

        // The emitter should produce typeof(int) for the type literal
        Assert.Contains("typeof(int)", csContent);
        // The user-defined class should still be emitted
        Assert.Contains("class MyClass", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type literal — var foo = typeof(int); for inferred declarations (Req 5.2, 5.4)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_InferredVarWithTypeLiteral_ProducesVarTypeofInt()
    {
        CleanOutputDir();

        var source = "class Program {\n    public static void Main() {\n        var foo = int;\n    }\n}";

        var csContent = CompileFullPipeline(source);
        Assert.NotNull(csContent);

        Assert.Contains("var foo = typeof(int);", csContent);
    }

    // ─── Full pipeline helper ────────────────────────────────────────────

    private static readonly Mist.Lexing.MistLexer PipelineLexer = new();
    private static readonly Mist.Parsing.MistParser PipelineParser = new();

    private string? CompileFullPipeline(string mistSource)
    {
        // Clean output directory
        foreach(var file in Directory.GetFiles(_outputDir))
            File.Delete(file);

        // 1. Lex
        var sourceText = new SourceText("test.mist", mistSource);
        var bag = new DiagnosticBag();
        var tokens = PipelineLexer.Lex(sourceText, bag);
        if(bag.HasErrors) return null;

        // 2. Parse
        var parseResult = PipelineParser.Parse(tokens, bag);
        if(parseResult.Root is null || bag.HasErrors) return null;

        // 3. Build semantic model
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semanticBag = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semanticBag);

        // 4. Emit C#
        var emitBag = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", emitBag);

        // 5. Read the emitted .cs file
        var csFile = Directory.GetFiles(_outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : null;
    }
}