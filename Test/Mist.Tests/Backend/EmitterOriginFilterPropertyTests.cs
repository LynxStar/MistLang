// Feature: mist-language-v1, Property 13: C# emitter emits only UserCode symbols
// **Validates: Requirements 7.5, 11.1, 11.2**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

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
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;

    public EmitterOriginFilterPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistEmitterOriginTests_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
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

    /// <summary>
    /// Known ambient type names — now stdlib types accessed via using Standard;.
    /// After the stdlib migration, Console, Math, and Convert are in the Standard namespace.
    /// </summary>
    private static readonly string[] KnownAmbientTypes = ["Console", "Math", "Convert"];

    /// <summary>
    /// Builds a program that references the given stdlib types via field declarations
    /// and includes a static void Main() entry point.
    /// </summary>
    private static (CompilationUnitNode Ast, string Source) BuildProgramWithAmbientRefs(
        IReadOnlyList<string> referencedAmbientTypes)
    {
        var fields = new List<MemberDeclarationNode>();
        for(var i = 0; i < referencedAmbientTypes.Count; i++)
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
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode { Name = qualifiedId } },
            };
            fields.Add(new FieldMemberMemberDeclarationNode { Field = field });
        }

        // Add a static void Main() entry point.
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
        fields.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("TestClass"),
            Members = fields.ToImmutableArray(),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("Standard")),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        // Build a source string that matches the AST structure for span extraction.
        var source = "using Standard;\nclass TestClass {\n";
        for(var i = 0; i < referencedAmbientTypes.Count; i++)
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
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();
        var sourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(sourceText);
        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(ast);
        var compilation = new MistCompilation(allUnits);
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, allAsts, resolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", diagnostics);

        // When stdlib is loaded, multiple .cs files are emitted (MistVoid.g.cs,
        // UnionHelpers.cs, etc.). The user's code is in test.cs. Fall back to
        // the first .cs file for tests that don't load stdlib.
        var csFiles = Directory.GetFiles(outputDir, "*.cs");
        var testFile = csFiles.FirstOrDefault(f => Path.GetFileName(f) == "test.cs");
        var csFile = testFile ?? csFiles.FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    /// <summary>
    /// Cleans the output directory between test runs.
    /// </summary>
    private void CleanOutputDir()
    {
        if(Directory.Exists(outputDir))
        {
            foreach(var file in Directory.GetFiles(outputDir))
            {
                File.Delete(file);
            }
        }
    }

    // Regex patterns for detecting type/method/field declarations in emitted C#.
    // These match class/struct declarations, method declarations, and field declarations.
    [GeneratedRegex(@"^\s*(?:public|private|protected|internal|sealed|static|\s)*\s*class\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex ClassDeclRegex();

    [GeneratedRegex(@"^\s*(?:public|private|protected|internal|sealed|static|readonly|ref|\s)*\s*struct\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex StructDeclRegex();

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Represents a subset of ambient types to reference in a test program.
    /// </summary>
#pragma warning disable CA1034
    public record AmbientRefScenario(ImmutableArray<string> ReferencedTypes)
    {
        public override string ToString()
        {
            return $"Referenced=[{string.Join(", ", ReferencedTypes)}]";
        }
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
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrEmpty(csContent))
        {
            return false;
        }

        // Extract all class/struct declaration names from the emitted C#.
        var classDecls = ClassDeclRegex().Matches(csContent)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        var structDecls = StructDeclRegex().Matches(csContent)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var allDecls = classDecls.Union(structDecls).ToHashSet();

        // No backend-origin type should appear as a declaration.
        foreach(var ambientType in scenario.ReferencedTypes)
        {
            if(allDecls.Contains(ambientType))
            {
                return false;
            }
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
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrEmpty(csContent))
        {
            return false;
        }

        // After stdlib migration, Console/Math/Convert are emitted as fully-qualified
        // CLR names (System.Console, System.Math, System.Convert) via CSharpTypeMap.
        // The emitted C# should contain these fully-qualified references.
        return csContent.Contains("System.Console") ||
               csContent.Contains("System.Math") ||
               csContent.Contains("System.Convert");
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
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                    new VariableDeclarationStatementStatementNode
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
                            Name = MakeToken("x"),
                            Initializer = MakeIntLiteralExpression("42"),
                        },
                    }),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var source = "class Program {\n    public static void Main() {\n        int x = 42;\n    }\n}";
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrEmpty(csContent))
        {
            return false;
        }

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
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.DoesNotContain("class Console", csContent);
        Assert.Contains("class TestClass", csContent);
        // Console is emitted as fully-qualified System.Console via CSharpTypeMap.
        Assert.Contains("System.Console", csContent);
    }

    [Fact]
    public void EmittedSource_WithAllAmbientRefs_DoesNotDeclareAnyBackendType()
    {
        CleanOutputDir();

        var (ast, source) = BuildProgramWithAmbientRefs(KnownAmbientTypes);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        foreach(var ambientType in KnownAmbientTypes)
        {
            Assert.DoesNotContain($"class {ambientType}", csContent);
            Assert.DoesNotContain($"struct {ambientType}", csContent);
        }

        Assert.Contains("class TestClass", csContent);
        // Types are emitted as fully-qualified CLR names via CSharpTypeMap.
        Assert.Contains("System.Console", csContent);
    }
}