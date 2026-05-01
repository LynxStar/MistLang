// Feature: mist-language-v1, Property 14: C# emitter file-count invariant
// **Validates: Requirements 11.1, 11.10**

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying the C# emitter file-count invariant.
///
/// Property 14: For any Mist compilation with N source files that passes type checking,
/// the C# emitter SHALL produce exactly N .cs files plus one .csproj file in the
/// output directory.
/// </summary>
public sealed class EmitterFileCountPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public EmitterFileCountPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistEmitterFileCountTests_" + Guid.NewGuid().ToString("N"));
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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a CompilationUnitNode with a class containing a static void Main().
    /// The class name is parameterized to allow unique names across files.
    /// </summary>
    private static CompilationUnitNode MakeCompilationUnit(string className, bool hasMain = false)
    {
        var members = new List<MemberDeclarationNode>();

        // Add a simple int field.
        var field = new FieldDeclarationNode
        {
            Name = MakeToken("value"),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };
        members.Add(new FieldMemberMemberDeclarationNode { Field = field });

        if (hasMain)
        {
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
            members.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });
        }

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = members.ToImmutableArray(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a multi-file compilation with N source files.
    /// The first file contains the Main entry point.
    /// </summary>
    private (ISemanticModel Model, int FileCount) BuildMultiFileModel(int fileCount)
    {
        var units = new List<CompilationUnit>();
        var asts = new List<Storm.Runtime.SyntaxNode>();

        for (var i = 0; i < fileCount; i++)
        {
            var className = $"Class{i}";
            var hasMain = i == 0; // Only the first class has Main.
            var source = hasMain
                ? $"class {className} {{\n    int value;\n    public static void Main() {{ }}\n}}"
                : $"class {className} {{\n    int value;\n}}";

            var sourceText = new SourceText($"File{i}.mist", source);
            units.Add(new CompilationUnit(sourceText));
            asts.Add(MakeCompilationUnit(className, hasMain));
        }

        var compilation = new MistCompilation(units.ToImmutableArray());
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, asts.ToImmutableArray(), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);

        return (model, fileCount);
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
    /// Represents a file count scenario (1 to 5 source files).
    /// </summary>
#pragma warning disable CA1034
    public record FileCountScenario(int FileCount)
    {
        public override string ToString() => $"FileCount={FileCount}";
    }
#pragma warning restore CA1034

    public static Arbitrary<FileCountScenario> ArbitraryFileCountScenarios()
    {
        var gen = from n in Gen.Choose(1, 5)
                  select new FileCountScenario(n);
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 14: For any compilation with N source files, the emitter produces
    /// exactly N .cs files plus one .csproj file.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(EmitterFileCountPropertyTests) }, MaxTest = 50)]
    public bool Emitter_ProducesExactlyNPlusOneCsprojFiles(FileCountScenario scenario)
    {
        CleanOutputDir();

        var (model, fileCount) = BuildMultiFileModel(scenario.FileCount);
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        var csprojFiles = Directory.GetFiles(_outputDir, "*.csproj");

        return csFiles.Length == fileCount && csprojFiles.Length == 1;
    }

    /// <summary>
    /// Property 14b: The .cs file names correspond to the source file names
    /// with the extension changed from .mist to .cs.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(EmitterFileCountPropertyTests) }, MaxTest = 50)]
    public bool Emitter_CsFileNamesMatchSourceFileNames(FileCountScenario scenario)
    {
        CleanOutputDir();

        var (model, fileCount) = BuildMultiFileModel(scenario.FileCount);
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csFileNames = Directory.GetFiles(_outputDir, "*.cs")
            .Select(Path.GetFileName)
            .OrderBy(n => n)
            .ToList();

        var expectedNames = Enumerable.Range(0, fileCount)
            .Select(i => $"File{i}.cs")
            .OrderBy(n => n)
            .ToList();

        if (csFileNames.Count != expectedNames.Count)
            return false;

        for (var i = 0; i < csFileNames.Count; i++)
        {
            if (csFileNames[i] != expectedNames[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 14c: The .csproj file is named after the project.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(EmitterFileCountPropertyTests) }, MaxTest = 50)]
    public bool Emitter_CsprojFileIsNamedAfterProject(FileCountScenario scenario)
    {
        CleanOutputDir();

        var (model, _) = BuildMultiFileModel(scenario.FileCount);
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csprojFiles = Directory.GetFiles(_outputDir, "*.csproj");
        return csprojFiles.Length == 1 &&
               Path.GetFileName(csprojFiles[0]) == "TestProject.csproj";
    }

    // ─── Targeted unit-style tests ───────────────────────────────────────

    [Fact]
    public void SingleFile_ProducesOneCsAndOneCsproj()
    {
        CleanOutputDir();

        var (model, _) = BuildMultiFileModel(1);
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        Assert.Single(Directory.GetFiles(_outputDir, "*.cs"));
        Assert.Single(Directory.GetFiles(_outputDir, "*.csproj"));
    }

    [Fact]
    public void ThreeFiles_ProducesThreeCsAndOneCsproj()
    {
        CleanOutputDir();

        var (model, _) = BuildMultiFileModel(3);
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        Assert.Equal(3, Directory.GetFiles(_outputDir, "*.cs").Length);
        Assert.Single(Directory.GetFiles(_outputDir, "*.csproj"));
    }

    [Fact]
    public void FiveFiles_ProducesFiveCsAndOneCsproj()
    {
        CleanOutputDir();

        var (model, _) = BuildMultiFileModel(5);
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        Assert.Equal(5, Directory.GetFiles(_outputDir, "*.cs").Length);
        Assert.Single(Directory.GetFiles(_outputDir, "*.csproj"));
    }
}
