using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Xunit;

namespace Mist.Tests.Backend;

/// <summary>
/// Verifies that static fields and static methods inside generic type declarations
/// are emitted correctly using the existing C# pattern. The C# runtime provides
/// separate static state per constructed type.
///
/// **Validates: Requirements 10.1, 10.2, 10.3**
/// </summary>
public sealed class StaticMembersInGenericTypesTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public StaticMembersInGenericTypesTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistStaticGenericTests_" + Guid.NewGuid().ToString("N"));
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

    private static TypeParameterListNode MakeTypeParamList(params string[] names)
    {
        var first = new TypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => new TypeParameterNode { Name = Tok(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
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
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csFile = Directory.GetFiles(_outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    [Fact]
    public void GenericClass_WithStaticField_EmitsStaticFieldInsideGenericClass()
    {
        // Arrange: class Box<T> { static int count; }
        var staticModifier = new StaticModifierModifierNode();
        var fieldDecl = new FieldDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(staticModifier),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = Tok("count"),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Box"),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        // Act
        var model = BuildModel(ast, "class Box<T> { static int count; }");
        var csContent = EmitAndGetCsContent(model);

        // Assert: emitted C# should contain "class Box<T>" and "static int count;"
        Assert.Contains("class Box<T>", csContent);
        Assert.Contains("static int count;", csContent);
    }

    [Fact]
    public void GenericClass_WithStaticMethod_EmitsStaticMethodInsideGenericClass()
    {
        // Arrange: class Container<T> { static void Reset() { } }
        var staticModifier = new StaticModifierModifierNode();
        var methodDecl = new MethodDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(staticModifier),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Name = Tok("Reset"),
            Body = new BlockNode
            {
                Statements = ImmutableArray<StatementNode>.Empty,
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Container"),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        // Act
        var model = BuildModel(ast, "class Container<T> { static void Reset() { } }");
        var csContent = EmitAndGetCsContent(model);

        // Assert: emitted C# should contain "class Container<T>" and "static void Reset()"
        Assert.Contains("class Container<T>", csContent);
        Assert.Contains("static void Reset()", csContent);
    }

    [Fact]
    public void GenericClass_WithMultipleTypeParams_AndStaticMembers_EmitsCorrectly()
    {
        // Arrange: class Map<K, V> { static int size; static void Clear() { } }
        var staticMod = new StaticModifierModifierNode();

        var fieldDecl = new FieldDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new StaticModifierModifierNode()),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = Tok("size"),
        };

        var methodDecl = new MethodDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(staticMod),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Name = Tok("Clear"),
            Body = new BlockNode
            {
                Statements = ImmutableArray<StatementNode>.Empty,
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Map"),
            TypeParams = MakeTypeParamList("K", "V"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        // Act
        var model = BuildModel(ast, "class Map<K, V> { static int size; static void Clear() { } }");
        var csContent = EmitAndGetCsContent(model);

        // Assert
        Assert.Contains("class Map<K, V>", csContent);
        Assert.Contains("static int size;", csContent);
        Assert.Contains("static void Clear()", csContent);
    }
}
