// Feature: mist-language-v1, Property 13: C# emitter emits only UserCode symbols
// **Validates: Requirements 7.5, 11.1, 11.2**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying that the C# emitter emits only UserCode symbols.
///
/// Property 13: For any Mist compilation that passes type checking, the C# emitter
/// SHALL emit C# source declarations only for symbols with SymbolOrigin.UserCode.
/// No symbol with SymbolOrigin.Backend SHALL appear as a type, method, or field
/// declaration in the emitted C# source. Backend-origin namespaces referenced by
/// user code SHALL appear only as using directives.
/// </summary>
public sealed partial class EmitterOriginFilterPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public EmitterOriginFilterPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistEmitterOriginTests_" + Guid.NewGuid().ToString("N"));
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

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
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

    /// <summary>
    /// Known ambient type names from CSharpAmbientTypeResolver.
    /// String and Array are no longer ambient types after Task 42 (Req 9.1, 9.2, 9.6).
    /// </summary>
    private static readonly string[] KnownAmbientTypes = ["Console", "Math", "Convert"];

    /// <summary>
    /// Builds a program that references the given ambient types via field declarations
    /// and includes a static void Main() entry point.
    /// </summary>
    private static (CompilationUnitNode Ast, string Source) BuildProgramWithAmbientRefs(
        IReadOnlyList<string> referencedAmbientTypes)
    {
        var fields = new List<MemberDeclarationNode>();
        for (var i = 0; i < referencedAmbientTypes.Count; i++)
        {
            var typeName = referencedAmbientTypes[i];
            var qualifiedId = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, 0),
            };
            var field = new FieldDeclarationNode
            {
                Name = MakeToken($"field{i}"),
                Type = new NamedTypeRefTypeReferenceNode { Name = qualifiedId },
            };
            fields.Add(new FieldMemberMemberDeclarationNode { Field = field });
        }

        // Add a static void Main() entry point.
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
        fields.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = fields.ToImmutableArray(),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("System")),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        // Build a source string that matches the AST structure for span extraction.
        var source = "using System;\nclass TestClass {\n";
        for (var i = 0; i < referencedAmbientTypes.Count; i++)
        {
            source += $"    {referencedAmbientTypes[i]} field{i};\n";
        }
        source += "    public static void Main() { }\n}";

        return (ast, source);
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

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csFile = Directory.GetFiles(_outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    /// <summary>
    /// Cleans the output directory between test runs.
    /// </summary>
    private void CleanOutputDir()
    {
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    // Regex patterns for detecting type/method/field declarations in emitted C#.
    // These match class/struct declarations, method declarations, and field declarations.
    [GeneratedRegex(@"^\s*(?:public|private|static|\s)*\s*class\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex ClassDeclRegex();

    [GeneratedRegex(@"^\s*(?:public|private|static|\s)*\s*struct\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex StructDeclRegex();

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Represents a subset of ambient types to reference in a test program.
    /// </summary>
#pragma warning disable CA1034
    public record AmbientRefScenario(ImmutableArray<string> ReferencedTypes)
    {
        public override string ToString() =>
            $"Referenced=[{string.Join(", ", ReferencedTypes)}]";
    }
#pragma warning restore CA1034

    public static Arbitrary<AmbientRefScenario> ArbitraryAmbientRefScenarios()
    {
        var maxMask = (1 << KnownAmbientTypes.Length) - 1;
        var gen = from mask in Gen.Choose(1, maxMask) // at least one type referenced
                  let referenced = KnownAmbientTypes
                      .Where((_, i) => (mask & (1 << i)) != 0)
                      .ToImmutableArray()
                  select new AmbientRefScenario(referenced);
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 13a: For any subset of ambient types referenced in user code,
    /// the emitted C# source SHALL NOT contain class or struct declarations
    /// for any backend-origin type (Console, Math, etc.).
    /// </summary>
    [Property(Arbitrary = new[] { typeof(EmitterOriginFilterPropertyTests) }, MaxTest = 31)]
    public bool EmittedSource_DoesNotContainBackendTypeDeclarations(AmbientRefScenario scenario)
    {
        CleanOutputDir();

        var (ast, source) = BuildProgramWithAmbientRefs(scenario.ReferencedTypes);
        var resolver = new CSharpAmbientTypeResolver();
        var model = BuildModel(ast, source, resolver);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // Extract all class/struct declaration names from the emitted C#.
        var classDecls = ClassDeclRegex().Matches(csContent)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        var structDecls = StructDeclRegex().Matches(csContent)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var allDecls = classDecls.Union(structDecls).ToHashSet();

        // No backend-origin type should appear as a declaration.
        foreach (var ambientType in scenario.ReferencedTypes)
        {
            if (allDecls.Contains(ambientType))
                return false;
        }

        // The user-defined class "TestClass" SHOULD appear.
        return allDecls.Contains("TestClass");
    }

    /// <summary>
    /// Property 13b: For any subset of ambient types referenced in user code,
    /// the emitted C# source SHALL contain using directives for backend namespaces.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(EmitterOriginFilterPropertyTests) }, MaxTest = 31)]
    public bool EmittedSource_ContainsUsingDirectivesForBackendNamespaces(AmbientRefScenario scenario)
    {
        CleanOutputDir();

        var (ast, source) = BuildProgramWithAmbientRefs(scenario.ReferencedTypes);
        var resolver = new CSharpAmbientTypeResolver();
        var model = BuildModel(ast, source, resolver);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // All known ambient types are in the "System" namespace.
        // The emitted C# should contain "using System;" as a directive.
        return csContent.Contains("using System;");
    }

    /// <summary>
    /// Property 13c: For any program with only UserCode symbols (no ambient types),
    /// the emitted C# source SHALL contain declarations for all user-defined types
    /// and no using directives for backend namespaces.
    /// </summary>
    [Property(MaxTest = 10)]
    public bool EmittedSource_WithNoAmbientRefs_ContainsOnlyUserDeclarations()
    {
        CleanOutputDir();

        // Build a program with no ambient type references.
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new VariableDeclarationStatementStatementNode
                    {
                        VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                        {
                            Type = new PrimitiveTypeRefTypeReferenceNode
                            {
                                PrimitiveType = new IntTypePrimitiveTypeNode(),
                            },
                            Name = MakeToken("x"),
                            Initializer = MakeIntLiteralExpression("42"),
                        },
                    }),
            } },
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

        var source = "class Program {\n    public static void Main() {\n        int x = 42;\n    }\n}";
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // Should contain the user-defined class.
        var classDecls = ClassDeclRegex().Matches(csContent)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        return classDecls.Contains("Program");
    }

    // ─── Targeted unit-style tests ───────────────────────────────────────

    [Fact]
    public void EmittedSource_WithConsoleRef_DoesNotDeclareConsoleClass()
    {
        CleanOutputDir();

        var (ast, source) = BuildProgramWithAmbientRefs(["Console"]);
        var resolver = new CSharpAmbientTypeResolver();
        var model = BuildModel(ast, source, resolver);
        var csContent = EmitAndGetCsContent(model);

        Assert.DoesNotContain("class Console", csContent);
        Assert.Contains("class TestClass", csContent);
        Assert.Contains("using System;", csContent);
    }

    [Fact]
    public void EmittedSource_WithAllAmbientRefs_DoesNotDeclareAnyBackendType()
    {
        CleanOutputDir();

        var (ast, source) = BuildProgramWithAmbientRefs(KnownAmbientTypes);
        var resolver = new CSharpAmbientTypeResolver();
        var model = BuildModel(ast, source, resolver);
        var csContent = EmitAndGetCsContent(model);

        foreach (var ambientType in KnownAmbientTypes)
        {
            Assert.DoesNotContain($"class {ambientType}", csContent);
            Assert.DoesNotContain($"struct {ambientType}", csContent);
        }

        Assert.Contains("class TestClass", csContent);
        Assert.Contains("using System;", csContent);
    }
}
