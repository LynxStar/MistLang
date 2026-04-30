// Feature: method-overloading, Property 14: Overload emission completeness
// Feature: method-overloading, Property 15: Round-trip compilation of overloaded programs
// **Validates: Requirements 7.1, 7.2, 7.3**

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for C# emission of overloaded methods and constructors,
/// and round-trip compilation of overloaded Mist programs.
///
/// Property 14: Overload emission completeness
/// For any type with N overloaded methods (or constructors) sharing a name,
/// the C# emitter produces exactly N separate C# method (or constructor) declarations.
///
/// Property 15: Round-trip compilation of overloaded programs
/// For any valid Mist program containing overloaded methods and/or constructors,
/// the full pipeline (parse → semantic model → type check → emit → Roslyn compile)
/// produces a successful C# compilation.
/// </summary>
public sealed partial class OverloadEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public OverloadEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistOverloadEmissionTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Primitive type names used for generating distinct parameter signatures.
    /// </summary>
    private static readonly string[] PrimitiveTypeNames =
        ["int", "bool", "double", "float", "char"];

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveType };
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

    // Regex to count method declarations with a given name in emitted C#.
    // Matches patterns like: void DoWork(int p0)  or  public void DoWork()
    [GeneratedRegex(@"(?:(?:public|private|static)\s+)*(?:void|int|bool|double|float|char|long|string|object)\s+(\w+)\s*\(", RegexOptions.Multiline)]
    private static partial Regex MethodDeclPattern();

    // Regex to count constructor declarations with a given name in emitted C#.
    // Matches patterns like: MyClass(int p0)  or  public MyClass()
    // Constructors have no return type, so we match: [modifiers] ClassName(
    // We need to be careful not to match method declarations.
    [GeneratedRegex(@"^\s*(?:(?:public|private|static)\s+)*(\w+)\s*\([^)]*\)(?:\s*:\s*(?:this|base)\s*\([^)]*\))?\s*$", RegexOptions.Multiline)]
    private static partial Regex ConstructorDeclPattern();

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>
    /// Scenario for Property 14: N overloaded methods with distinct parameter types.
    /// </summary>
#pragma warning disable CA1034
    public record OverloadedMethodEmissionScenario(
        string MethodName,
        IReadOnlyList<string> ParamTypeNames)
    {
        public override string ToString() =>
            $"{MethodName}: {ParamTypeNames.Count} overloads [{string.Join(", ", ParamTypeNames)}]";
    }

    /// <summary>
    /// Scenario for Property 14: N overloaded constructors with distinct parameter types.
    /// </summary>
    public record OverloadedConstructorEmissionScenario(
        string ClassName,
        IReadOnlyList<string> ParamTypeNames)
    {
        public override string ToString() =>
            $"{ClassName}: {ParamTypeNames.Count} constructor overloads [{string.Join(", ", ParamTypeNames)}]";
    }

    /// <summary>
    /// Scenario for Property 15: A valid Mist program with overloaded methods.
    /// </summary>
    public record RoundTripOverloadScenario(
        string MistSource,
        int ExpectedMethodOverloads)
    {
        public override string ToString() =>
            $"RoundTrip: {ExpectedMethodOverloads} overloads, source length={MistSource.Length}";
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates N (2–5) overloaded methods with distinct single-parameter types.
    /// </summary>
    public static Arbitrary<OverloadedMethodEmissionScenario> ArbitraryOverloadedMethodEmission()
    {
        var gen =
            from overloadCount in Gen.Choose(2, 5)
            from typeIndices in Gen.ArrayOf(Gen.Choose(0, PrimitiveTypeNames.Length - 1), overloadCount)
                .Where(indices => indices.Distinct().Count() == indices.Length)
            let paramTypes = typeIndices.Select(i => PrimitiveTypeNames[i]).ToList()
            select new OverloadedMethodEmissionScenario("DoWork", paramTypes);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates N (2–5) overloaded constructors with distinct single-parameter types.
    /// </summary>
    public static Arbitrary<OverloadedConstructorEmissionScenario> ArbitraryOverloadedConstructorEmission()
    {
        var gen =
            from overloadCount in Gen.Choose(2, 5)
            from typeIndices in Gen.ArrayOf(Gen.Choose(0, PrimitiveTypeNames.Length - 1), overloadCount)
                .Where(indices => indices.Distinct().Count() == indices.Length)
            let paramTypes = typeIndices.Select(i => PrimitiveTypeNames[i]).ToList()
            select new OverloadedConstructorEmissionScenario("MyClass", paramTypes);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates valid Mist programs with overloaded methods for round-trip compilation.
    /// </summary>
    public static Arbitrary<RoundTripOverloadScenario> ArbitraryRoundTripOverload()
    {
        var gen =
            from overloadCount in Gen.Choose(2, 4)
            from typeIndices in Gen.ArrayOf(Gen.Choose(0, PrimitiveTypeNames.Length - 1), overloadCount)
                .Where(indices => indices.Distinct().Count() == indices.Length)
            let paramTypes = typeIndices.Select(i => PrimitiveTypeNames[i]).ToList()
            let source = BuildMistSource(paramTypes)
            select new RoundTripOverloadScenario(source, overloadCount);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Builds a valid Mist source string with overloaded methods in a class with a Main entry point.
    /// </summary>
    private static string BuildMistSource(IReadOnlyList<string> paramTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("class Program {");
        sb.AppendLine("    public static void Main() { }");
        sb.AppendLine();

        for (var i = 0; i < paramTypes.Count; i++)
        {
            sb.AppendLine($"    public void DoWork({paramTypes[i]} p{i}) {{ }}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // ─── AST construction for Property 14 ────────────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode with a class containing N overloaded methods
    /// and a Main entry point. Returns the AST and a matching source string.
    /// </summary>
    private static (CompilationUnitNode Ast, string Source) BuildMethodOverloadAst(
        OverloadedMethodEmissionScenario scenario)
    {
        var sb = new StringBuilder();
        sb.AppendLine("class MyClass {");

        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
        var offset = "class MyClass {\n".Length;

        // Add a Main entry point
        var mainSource = "    public static void Main() { }";
        sb.AppendLine(mainSource);
        sb.AppendLine();

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
        members.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });

        // Add overloaded methods
        for (var i = 0; i < scenario.ParamTypeNames.Count; i++)
        {
            var typeName = scenario.ParamTypeNames[i];
            var paramName = $"p{i}";
            var methodLine = $"    public void {scenario.MethodName}({typeName} {paramName}) {{ }}";
            var lineStart = sb.Length;
            sb.AppendLine(methodLine);

            // Calculate the span for the parameter list (just "typeName paramName")
            var paramText = $"{typeName} {paramName}";
            var paramStart = lineStart + methodLine.IndexOf(paramText, StringComparison.Ordinal);

            var param = new ParameterNode
            {
                Type = MakePrimitiveTypeRef(typeName),
                Name = MakeToken(paramName),
            };

            var paramList = new ParameterListNode
            {
                Parameter = param,
                Span = new Storm.Runtime.SourceSpan(paramStart, paramText.Length),
            };

            var method = new MethodDeclarationNode
            {
                Name = MakeToken(scenario.MethodName),
                ReturnType = new VoidTypeRefTypeReferenceNode(),
                Parameters = paramList,
                Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            };

            members.Add(new MethodMemberMemberDeclarationNode { Method = method });
        }

        sb.AppendLine("}");
        var source = sb.ToString();

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = members.ToImmutable(),
        };

        var ast = new CompilationUnitNode
        {
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        return (ast, source);
    }

    /// <summary>
    /// Builds a CompilationUnitNode with a class containing N overloaded constructors
    /// and a Main entry point. Returns the AST and a matching source string.
    /// </summary>
    private static (CompilationUnitNode Ast, string Source) BuildConstructorOverloadAst(
        OverloadedConstructorEmissionScenario scenario)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"class {scenario.ClassName} {{");

        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();

        // Add a Main entry point
        sb.AppendLine("    public static void Main() { }");
        sb.AppendLine();

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
        members.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });

        // Add overloaded constructors
        for (var i = 0; i < scenario.ParamTypeNames.Count; i++)
        {
            var typeName = scenario.ParamTypeNames[i];
            var paramName = $"p{i}";
            var ctorLine = $"    public {scenario.ClassName}({typeName} {paramName}) {{ }}";
            var lineStart = sb.Length;
            sb.AppendLine(ctorLine);

            var paramText = $"{typeName} {paramName}";
            var paramStart = lineStart + ctorLine.IndexOf(paramText, StringComparison.Ordinal);

            var param = new ParameterNode
            {
                Type = MakePrimitiveTypeRef(typeName),
                Name = MakeToken(paramName),
            };

            var paramList = new ParameterListNode
            {
                Parameter = param,
                Span = new Storm.Runtime.SourceSpan(paramStart, paramText.Length),
            };

            var ctor = new ConstructorDeclarationNode
            {
                Name = MakeToken(scenario.ClassName),
                Parameters = paramList,
                Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            };

            members.Add(new ConstructorMemberMemberDeclarationNode { Constructor = ctor });
        }

        sb.AppendLine("}");
        var source = sb.ToString();

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.ClassName),
            Members = members.ToImmutable(),
        };

        var ast = new CompilationUnitNode
        {
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        return (ast, source);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 14: Overload emission completeness (methods)
    //
    // **Validates: Requirements 7.1, 7.2**
    //
    // For any type with N overloaded methods sharing a name, the C# emitter
    // produces exactly N separate C# method declarations with that name.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 20,
        Arbitrary = new[] { typeof(OverloadEmissionPropertyTests) })]
    public bool OverloadedMethods_EmitExactlyNDeclarations(OverloadedMethodEmissionScenario scenario)
    {
        CleanOutputDir();

        var (ast, source) = BuildMethodOverloadAst(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // Count method declarations with the overloaded name in the emitted C#
        var matches = MethodDeclPattern().Matches(csContent);
        var overloadCount = matches.Count(m => m.Groups[1].Value == scenario.MethodName);

        return overloadCount == scenario.ParamTypeNames.Count;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 14: Overload emission completeness (constructors)
    //
    // **Validates: Requirements 7.1, 7.2**
    //
    // For any type with N overloaded constructors, the C# emitter produces
    // exactly N separate C# constructor declarations.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 20,
        Arbitrary = new[] { typeof(OverloadEmissionPropertyTests) })]
    public bool OverloadedConstructors_EmitExactlyNDeclarations(OverloadedConstructorEmissionScenario scenario)
    {
        CleanOutputDir();

        var (ast, source) = BuildConstructorOverloadAst(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // Count constructor declarations: lines matching "ClassName(" that are NOT method declarations.
        // A constructor line has no return type before the class name.
        // We count occurrences of the pattern: [modifiers] ClassName(
        // where there is no return type keyword before ClassName.
        var constructorCount = CountConstructorDeclarations(csContent, scenario.ClassName);

        return constructorCount == scenario.ParamTypeNames.Count;
    }

    /// <summary>
    /// Counts constructor declarations for a given class name in emitted C# source.
    /// A constructor is a line matching [modifiers] ClassName(...) with no return type.
    /// </summary>
    private static int CountConstructorDeclarations(string csContent, string className)
    {
        // Match lines that look like constructors: optional modifiers, then ClassName(
        // but NOT preceded by a return type keyword.
        var pattern = $@"^\s*(?:(?:public|private)\s+)?{Regex.Escape(className)}\s*\(";
        var regex = new Regex(pattern, RegexOptions.Multiline);
        return regex.Matches(csContent).Count;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 15: Round-trip compilation of overloaded programs
    //
    // **Validates: Requirements 7.3**
    //
    // For any valid Mist program containing overloaded methods, the full
    // pipeline (parse → semantic model → type check → emit → Roslyn compile)
    // produces a successful C# compilation.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 20,
        Arbitrary = new[] { typeof(OverloadEmissionPropertyTests) })]
    public bool OverloadedProgram_RoundTripCompilationSucceeds(RoundTripOverloadScenario scenario)
    {
        // Create a unique temp directory for this test run
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "MistRoundTrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write the Mist source to a file
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, scenario.MistSource);

            // Create the full pipeline driver
            var driver = new CompilerDriver(
                new Mist.Lexing.MistLexer(),
                new Mist.Parsing.MistParser(),
                new SemanticModelBuilder(),
                ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
                ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: Path.Combine(tempDir, "out"),
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = driver.Compile(options);

            // The compilation should succeed
            if (!result.Succeeded)
            {
                // Log diagnostics for debugging
                var errors = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"[{d.Id.Value}] {d.Message}")
                    .ToList();

                // Return false — the round-trip compilation failed
                return false;
            }

            return true;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
