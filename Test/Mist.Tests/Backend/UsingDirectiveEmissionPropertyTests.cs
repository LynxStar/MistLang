// Feature: inferred-namespaces, Property 15: C# Emitter Using Directive Output
// For any Mist source file with `using` declarations, the emitted C# output
// SHALL contain corresponding `using` directives for each declared namespace.
// **Validates: Requirements 8.6**

using System.Collections.Immutable;
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
/// Property-based tests verifying the C# emitter produces correct using
/// directives for each using declaration in the Mist source.
/// </summary>
public sealed class UsingDirectiveEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;

    public UsingDirectiveEmissionPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistUsingEmitTests_" + Guid.NewGuid().ToString("N"));
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

    private static UsingDeclarationNode MakeUsingDeclaration(string namespaceName)
    {
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(namespaceName),
            Span = new Storm.Runtime.SourceSpan(0, 0),
        };
        return new UsingDeclarationNode { Name = qualifiedId };
    }

    private static TypeBodyDeclarationNode MakeClassDecl(string name)
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

        return new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
    }

    private ISemanticModel BuildModel(
        CompilationUnitNode ast,
        string source,
        string namespaceRoot = "TestProject",
        string projectFolder = "/test")
    {
        var sourceText = new SourceText(Path.Combine(projectFolder, "test.mist"), source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return builder.Build(
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
        CSharpEmitter.Emit(model, outputDir, "TestProject", diagnostics);

        var csFile = Directory.GetFiles(outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

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

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Represents a using directive emission scenario.
    /// </summary>
#pragma warning disable CA1034
    public record UsingScenario(ImmutableArray<string> UsingNamespaces)
    {
        public override string ToString()
        {
            return $"Usings=[{string.Join(", ", UsingNamespaces)}]";
        }
    }
#pragma warning restore CA1034

    private static Gen<string> GenNamespaceName()
    {
        var identifiers = Gen.Elements(
            "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
            "Models", "Services", "Utils", "Core", "Data");

        return from segCount in Gen.Choose(1, 3)
               from segments in Gen.ArrayOf(identifiers, segCount)
               select string.Join(".", segments);
    }

    public static Arbitrary<UsingScenario> ArbitraryUsingScenario()
    {
        var gen = from count in Gen.Choose(1, 4)
                  from namespaces in Gen.ArrayOf(GenNamespaceName(), count)
                  let distinct = namespaces.Distinct().ToImmutableArray()
                  where distinct.Length > 0
                  select new UsingScenario(distinct);
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 15: For any Mist source file with using declarations,
    /// the emitted C# output SHALL contain corresponding using directives.
    /// Validates: Requirement 8.6
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UsingDirectiveEmissionPropertyTests) }, MaxTest = 100)]
    public bool UsingDeclarations_EmitCorrespondingCSharpUsingDirectives(UsingScenario scenario)
    {
        var classDecl = MakeClassDecl("Program");

        var usingDecls = scenario.UsingNamespaces
            .Select(MakeUsingDeclaration)
            .ToImmutableArray();

        var ast = new CompilationUnitNode
        {
            Usings = usingDecls,
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var usingLines = string.Join("\n", scenario.UsingNamespaces.Select(ns => $"using {ns};"));
        var source = $"{usingLines}\nclass Program {{ public static void Main() {{ }} }}";

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        // Every using declaration from the Mist source should appear as a
        // using directive in the emitted C#.
        foreach(var ns in scenario.UsingNamespaces)
        {
            if(!csContent.Contains($"using {ns};"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Property 15b: Each using declaration produces exactly one using directive
    /// in the emitted C# output (no duplicates, no omissions).
    /// Validates: Requirement 8.6
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UsingDirectiveEmissionPropertyTests) }, MaxTest = 100)]
    public bool UsingDeclarations_EachProducesExactlyOneDirective(UsingScenario scenario)
    {
        var classDecl = MakeClassDecl("Program");

        var usingDecls = scenario.UsingNamespaces
            .Select(MakeUsingDeclaration)
            .ToImmutableArray();

        var ast = new CompilationUnitNode
        {
            Usings = usingDecls,
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var usingLines = string.Join("\n", scenario.UsingNamespaces.Select(ns => $"using {ns};"));
        var source = $"{usingLines}\nclass Program {{ public static void Main() {{ }} }}";

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        // Extract using lines from the emitted C#.
        var emittedUsings = csContent
            .Split('\n')
            .Where(line => line.TrimStart().StartsWith("using "))
            .Select(line => line.Trim())
            .ToList();

        // Each source using namespace should appear exactly once.
        foreach(var ns in scenario.UsingNamespaces)
        {
            var directive = $"using {ns};";
            var count = emittedUsings.Count(u => u == directive);
            if(count != 1)
            {
                return false;
            }
        }

        return true;
    }
}