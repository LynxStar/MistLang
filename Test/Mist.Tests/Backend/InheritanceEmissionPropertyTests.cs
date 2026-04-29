// Feature: class-inheritance, Property 8: C# emission preserves inheritance structure
// **Validates: Requirements 7.1, 7.2**

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

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying that the C# emitter preserves inheritance structure.
///
/// Property 8: C# emission preserves inheritance structure
/// For any Mist program containing class declarations with inheritance clauses,
/// the emitted C# source shall contain <c>class DerivedName : BaseName</c> for each
/// derived class, and classes without inheritance clauses shall be emitted as
/// <c>class Name</c> without a base clause.
///
/// **Validates: Requirements 7.1, 7.2**
/// </summary>
public sealed partial class InheritanceEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public InheritanceEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistInheritanceEmissionTests_" + Guid.NewGuid().ToString("N"));
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

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseClassName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (baseClassName is not null)
        {
            classDecl.BaseClause = MakeToken(":");
            classDecl.BaseClassName = MakeToken(baseClassName);
        }

        return classDecl;
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

    private void CleanOutputDir()
    {
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    // Regex to extract class declarations from emitted C#.
    // Captures: optional modifiers, class name, and optional base clause.
    [GeneratedRegex(@"^\s*(?:(?:public|private|static)\s+)*class\s+(\w+)(?:\s*:\s*(\w+))?", RegexOptions.Multiline)]
    private static partial Regex ClassDeclPattern();

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a generated test scenario: a set of class declarations,
    /// some with inheritance clauses and some without.
    /// </summary>
#pragma warning disable CA1034
    public record InheritanceEmissionScenario(
        ImmutableArray<(string Name, string? BaseName)> Classes)
    {
        public override string ToString()
        {
            var parts = Classes.Select(c =>
                c.BaseName is not null ? $"{c.Name} : {c.BaseName}" : c.Name);
            return $"[{string.Join(", ", parts)}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta"];

    /// <summary>
    /// Generates a random set of 2-4 class declarations where some classes
    /// derive from previously declared classes and some are standalone.
    /// Always includes a Program class with Main as the last class.
    /// </summary>
    public static Gen<InheritanceEmissionScenario> GenScenario()
    {
        // Generate scenarios with 2 classes
        var gen2 = from idx0 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from idx1 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from inh1 in Gen.Elements(true, false)
                   from seed in Gen.Choose(0, 100000)
                   select BuildScenario(
                       new[] { idx0, idx1 },
                       new[] { false, inh1 },
                       seed);

        // Generate scenarios with 3 classes
        var gen3 = from idx0 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from idx1 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from idx2 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from inh1 in Gen.Elements(true, false)
                   from inh2 in Gen.Elements(true, false)
                   from seed in Gen.Choose(0, 100000)
                   select BuildScenario(
                       new[] { idx0, idx1, idx2 },
                       new[] { false, inh1, inh2 },
                       seed);

        // Generate scenarios with 4 classes
        var gen4 = from idx0 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from idx1 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from idx2 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from idx3 in Gen.Choose(0, ClassNamePool.Length - 1)
                   from inh1 in Gen.Elements(true, false)
                   from inh2 in Gen.Elements(true, false)
                   from inh3 in Gen.Elements(true, false)
                   from seed in Gen.Choose(0, 100000)
                   select BuildScenario(
                       new[] { idx0, idx1, idx2, idx3 },
                       new[] { false, inh1, inh2, inh3 },
                       seed);

        return Gen.OneOf(gen2, gen3, gen4);
    }

    private static InheritanceEmissionScenario BuildScenario(
        int[] nameIndices,
        bool[] inheritanceMask,
        int seed)
    {
        var rng = new Random(seed);
        var classes = new List<(string Name, string? BaseName)>();
        var declaredNames = new List<string>();

        for (var i = 0; i < nameIndices.Length; i++)
        {
            // Ensure unique class names
            var baseName = ClassNamePool[nameIndices[i]];
            var name = baseName;
            var suffix = 0;
            while (declaredNames.Contains(name) || name == "Program")
            {
                suffix++;
                name = baseName + suffix;
            }
            declaredNames.Add(name);

            // Determine base class: only if there's a previously declared class
            // and the inheritance mask says yes
            string? baseClassName = null;
            if (inheritanceMask[i] && i > 0)
            {
                baseClassName = declaredNames[rng.Next(i)];
            }

            classes.Add((name, baseClassName));
        }

        return new InheritanceEmissionScenario(classes.ToImmutableArray());
    }

    /// <summary>FsCheck Arbitrary registration.</summary>
    public static Arbitrary<InheritanceEmissionScenario> ArbitraryInheritanceEmissionScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode from the scenario. Adds a Program class
    /// with a static void Main() entry point at the end.
    /// </summary>
    private static CompilationUnitNode BuildAst(InheritanceEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        foreach (var (name, baseName) in scenario.Classes)
        {
            var classDecl = MakeClassDecl(name, baseName);
            typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        }

        // Add Program class with Main entry point
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = MakeClassDecl(
            "Program",
            members: ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }));

        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass });

        return new CompilationUnitNode
        {
            Types = typeDecls.ToImmutableArray(),
        };
    }

    /// <summary>
    /// Builds a minimal source string that matches the AST structure.
    /// The emitter uses source text for span-based extraction, so we provide
    /// a simple source that won't interfere with token lexeme-based fallback.
    /// </summary>
    private static string BuildSource(InheritanceEmissionScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (name, baseName) in scenario.Classes)
        {
            if (baseName is not null)
                sb.AppendLine($"class {name} : {baseName} {{ }}");
            else
                sb.AppendLine($"class {name} {{ }}");
        }
        sb.AppendLine("class Program { public static void Main() { } }");
        return sb.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 8: C# emission preserves inheritance structure
    //
    // **Validates: Requirements 7.1, 7.2**
    //
    // For any Mist program containing class declarations with inheritance
    // clauses, the emitted C# source shall contain
    // `class DerivedName : BaseName` for each derived class, and classes
    // without inheritance clauses shall be emitted as `class Name` without
    // a base clause.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(InheritanceEmissionPropertyTests) })]
    public bool CSharpEmission_PreservesInheritanceStructure(InheritanceEmissionScenario scenario)
    {
        CleanOutputDir();

        var ast = BuildAst(scenario);
        var source = BuildSource(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // Parse all class declarations from the emitted C#
        var matches = ClassDeclPattern().Matches(csContent);
        var emittedClasses = new Dictionary<string, string?>();
        foreach (Match match in matches)
        {
            var className = match.Groups[1].Value;
            var baseClass = match.Groups[2].Success ? match.Groups[2].Value : null;
            emittedClasses[className] = baseClass;
        }

        // Verify each class from the scenario appears with correct inheritance
        foreach (var (name, expectedBase) in scenario.Classes)
        {
            if (!emittedClasses.TryGetValue(name, out var actualBase))
                return false; // Class not found in emitted output

            if (expectedBase is not null)
            {
                // Derived class: must have `: BaseName`
                if (actualBase != expectedBase)
                    return false;
            }
            else
            {
                // Non-derived class: must NOT have a base clause
                if (actualBase is not null)
                    return false;
            }
        }

        // Also verify Program class is emitted without a base clause
        if (!emittedClasses.TryGetValue("Program", out var programBase))
            return false;
        if (programBase is not null)
            return false;

        return true;
    }
}
