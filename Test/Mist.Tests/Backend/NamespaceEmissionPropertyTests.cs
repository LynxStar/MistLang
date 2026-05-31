// Feature: inferred-namespaces, Property 14: C# Emitter Namespace Output
// For any Mist source file with namespace declarations (inferred or explicit),
// the emitted C# output SHALL contain the corresponding namespace declarations
// with their associated types. File-scoped Mist namespaces and inferred namespaces
// SHALL produce file-scoped C# namespace declarations. Block Mist namespaces
// SHALL produce block C# namespace declarations.
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying the C# emitter produces correct namespace
/// declarations for inferred, file-scoped, and block namespaces.
/// </summary>
public sealed class NamespaceEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public NamespaceEmissionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistNsEmitTests_" + Guid.NewGuid().ToString("N"));
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

    private static QualifiedIdentifierNode MakeQualifiedId(string name)
    {
        return new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(name),
            Span = new Storm.Runtime.SourceSpan(0, 0),
        };
    }

    private static ClassDeclarationNode MakeClassDecl(string name)
    {
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

        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
    }

    private static StructDeclarationNode MakeStructDecl(string name)
    {
        var field = new FieldDeclarationNode
        {
            Name = MakeToken("value"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
        };

        return new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field }),
        };
    }

    private ISemanticModel BuildModel(
        CompilationUnitNode ast,
        string source,
        string namespaceRoot = "TestProject",
        string projectFolder = "/test",
        string filePath = "test.mist")
    {
        var sourceText = new SourceText(
            string.IsNullOrEmpty(projectFolder) ? filePath : Path.Combine(projectFolder, filePath),
            source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: namespaceRoot,
            projectFolder: projectFolder,
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        CleanOutputDir();
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csFile = Directory.GetFiles(_outputDir, "*.cs").FirstOrDefault();
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

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Represents a namespace emission scenario.
    /// </summary>
#pragma warning disable CA1034
    public record NamespaceScenario(
        string NamespaceRoot,
        string ClassName,
        NamespaceStyle Style)
    {
        public override string ToString() =>
            $"Root={NamespaceRoot}, Class={ClassName}, Style={Style}";
    }

    public enum NamespaceStyle
    {
        Inferred,
        FileScoped,
        Block,
    }

    public record MultiNamespaceScenario(
        string NamespaceRoot,
        string FileScopedNs,
        string BlockNs,
        string InferredClassName,
        string FileScopedClassName,
        string BlockClassName)
    {
        public override string ToString() =>
            $"Root={NamespaceRoot}, FileScoped={FileScopedNs}, Block={BlockNs}";
    }
#pragma warning restore CA1034

    private static Gen<string> GenValidIdentifier()
    {
        return Gen.Elements(
            "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
            "Foo", "Bar", "Baz", "Widget", "Service");
    }

    private static Gen<string> GenNamespaceRoot()
    {
        return from seg1 in GenValidIdentifier()
               from seg2 in GenValidIdentifier()
               select $"{seg1}.{seg2}";
    }

    public static Arbitrary<NamespaceScenario> ArbitraryNamespaceScenario()
    {
        var gen = from root in GenNamespaceRoot()
                  from className in GenValidIdentifier()
                  from style in Gen.Elements(
                      NamespaceStyle.Inferred,
                      NamespaceStyle.FileScoped,
                      NamespaceStyle.Block)
                  select new NamespaceScenario(root, className, style);
        return gen.ToArbitrary();
    }

    public static Arbitrary<MultiNamespaceScenario> ArbitraryMultiNamespaceScenario()
    {
        var gen = from root in GenNamespaceRoot()
                  from fsNs in GenNamespaceRoot()
                  from bNs in GenNamespaceRoot()
                  from inferredClass in GenValidIdentifier()
                  from fsClass in GenValidIdentifier()
                  from bClass in GenValidIdentifier()
                  where inferredClass != fsClass && fsClass != bClass && inferredClass != bClass
                  select new MultiNamespaceScenario(root, fsNs, bNs, inferredClass, fsClass, bClass);
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 14a: For types with an inferred namespace (no explicit namespace in source),
    /// the emitted C# SHALL contain a file-scoped namespace declaration.
    /// Validates: Requirement 8.1
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceEmissionPropertyTests) }, MaxTest = 100)]
    public bool InferredNamespace_EmitsFileScopedCSharpNamespace(NamespaceScenario scenario)
    {
        if (scenario.Style != NamespaceStyle.Inferred) return true;

        var classDecl = MakeClassDecl(scenario.ClassName);
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var source = $"class {scenario.ClassName} {{ public static void Main() {{ }} }}";
        var model = BuildModel(ast, source, namespaceRoot: scenario.NamespaceRoot);
        var csContent = EmitAndGetCsContent(model);

        // The emitted C# should contain a file-scoped namespace declaration
        // matching the inferred namespace (which equals the namespace root for
        // files at the project root).
        return csContent.Contains($"namespace {scenario.NamespaceRoot};")
            && csContent.Contains($"class {scenario.ClassName}");
    }

    /// <summary>
    /// Property 14b: For types in an explicit file-scoped namespace,
    /// the emitted C# SHALL contain a file-scoped namespace declaration.
    /// Validates: Requirement 8.2
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceEmissionPropertyTests) }, MaxTest = 100)]
    public bool FileScopedNamespace_EmitsFileScopedCSharpNamespace(NamespaceScenario scenario)
    {
        if (scenario.Style != NamespaceStyle.FileScoped) return true;

        var classDecl = MakeClassDecl(scenario.ClassName);
        var nsName = MakeQualifiedId(scenario.NamespaceRoot);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode
                {
                    NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                    {
                        Name = nsName,
                        Body = ImmutableArray.Create<TypeDeclarationNode>(
                            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
                    },
                }),
        };

        var source = $"namespace {scenario.NamespaceRoot};\nclass {scenario.ClassName} {{ public static void Main() {{ }} }}";
        var model = BuildModel(ast, source, namespaceRoot: "OtherRoot");
        var csContent = EmitAndGetCsContent(model);

        return csContent.Contains($"namespace {scenario.NamespaceRoot};")
            && csContent.Contains($"class {scenario.ClassName}");
    }

    /// <summary>
    /// Property 14c: For types in an explicit block namespace,
    /// the emitted C# SHALL contain a block namespace declaration.
    /// Validates: Requirement 8.3
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceEmissionPropertyTests) }, MaxTest = 100)]
    public bool BlockNamespace_EmitsBlockCSharpNamespace(NamespaceScenario scenario)
    {
        if (scenario.Style != NamespaceStyle.Block) return true;

        var classDecl = MakeClassDecl(scenario.ClassName);
        var nsName = MakeQualifiedId(scenario.NamespaceRoot);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode
                {
                    NamespaceDeclaration = new BlockNamespaceNamespaceDeclarationNode
                    {
                        Name = nsName,
                        Body = ImmutableArray.Create<TypeDeclarationNode>(
                            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
                    },
                }),
        };

        var source = $"namespace {scenario.NamespaceRoot} {{\nclass {scenario.ClassName} {{ public static void Main() {{ }} }}\n}}";
        var model = BuildModel(ast, source, namespaceRoot: "OtherRoot");
        var csContent = EmitAndGetCsContent(model);

        // Block namespace should NOT have a semicolon after the name
        // and should have braces.
        var hasBlockNs = csContent.Contains($"namespace {scenario.NamespaceRoot}")
            && csContent.Contains("{")
            && !csContent.Contains($"namespace {scenario.NamespaceRoot};");

        return hasBlockNs && csContent.Contains($"class {scenario.ClassName}");
    }

    /// <summary>
    /// Property 14d: When a file contains multiple namespaces (inferred + explicit),
    /// the emitted C# SHALL contain each namespace with its associated types.
    /// Validates: Requirements 8.4, 8.5
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceEmissionPropertyTests) }, MaxTest = 100)]
    public bool MultipleNamespaces_EmitsAllNamespaceSections(MultiNamespaceScenario scenario)
    {
        // Build a file with:
        // 1. A top-level type (inferred namespace)
        // 2. A file-scoped namespace with a type
        // 3. A block namespace with a type
        var inferredClass = MakeClassDecl(scenario.InferredClassName);
        var fileScopedClass = MakeClassDecl(scenario.FileScopedClassName);
        var blockClass = MakeStructDecl(scenario.BlockClassName);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                // Top-level type (inferred namespace)
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = inferredClass },
                },
                // File-scoped namespace
                new NamespaceItemTopLevelItemNode
                {
                    NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                    {
                        Name = MakeQualifiedId(scenario.FileScopedNs),
                        Body = ImmutableArray.Create<TypeDeclarationNode>(
                            new ClassTypeTypeDeclarationNode { ClassDeclaration = fileScopedClass }).AsNamespaceBodyItems(),
                    },
                },
                // Block namespace
                new NamespaceItemTopLevelItemNode
                {
                    NamespaceDeclaration = new BlockNamespaceNamespaceDeclarationNode
                    {
                        Name = MakeQualifiedId(scenario.BlockNs),
                        Body = ImmutableArray.Create<TypeDeclarationNode>(
                            new StructTypeTypeDeclarationNode { StructDeclaration = blockClass }).AsNamespaceBodyItems(),
                    },
                }),
        };

        var source = $"class {scenario.InferredClassName} {{ public static void Main() {{ }} }}\n"
            + $"namespace {scenario.FileScopedNs};\nclass {scenario.FileScopedClassName} {{ }}\n"
            + $"namespace {scenario.BlockNs} {{\nstruct {scenario.BlockClassName} {{ int value; }}\n}}";

        var model = BuildModel(ast, source, namespaceRoot: scenario.NamespaceRoot);
        var csContent = EmitAndGetCsContent(model);

        // Verify all three namespace sections are present.
        var hasInferredNs = csContent.Contains($"namespace {scenario.NamespaceRoot};");
        var hasFileScopedNs = csContent.Contains($"namespace {scenario.FileScopedNs};");
        var hasBlockNs = csContent.Contains($"namespace {scenario.BlockNs}");

        var hasInferredClass = csContent.Contains($"class {scenario.InferredClassName}");
        var hasFileScopedClass = csContent.Contains($"class {scenario.FileScopedClassName}");
        var hasBlockStruct = csContent.Contains($"struct {scenario.BlockClassName}");

        return hasInferredNs && hasFileScopedNs && hasBlockNs
            && hasInferredClass && hasFileScopedClass && hasBlockStruct;
    }
}
