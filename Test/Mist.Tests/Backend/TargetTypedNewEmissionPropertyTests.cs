// Feature: target-typed-new, Property 5: Emission equivalence
// **Validates: Requirements 6.1, 6.2, 6.3**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Describes a pair of programs: one using target-typed new and one using fully-typed new,
/// for comparing their emitted C# output.
/// </summary>
public sealed class EmissionEquivalenceScenario
{
    /// <summary>Mist source using target-typed new: <c>T x = new(args)</c>.</summary>
    public string TargetTypedSource { get; }

    /// <summary>Mist source using fully-typed new: <c>T x = new T(args)</c>.</summary>
    public string FullyTypedSource { get; }

    /// <summary>Human-readable description of the scenario.</summary>
    public string Description { get; }

    /// <summary>The type name being constructed.</summary>
    public string TypeName { get; }

    /// <summary>The number of constructor arguments.</summary>
    public int ArgCount { get; }

    public EmissionEquivalenceScenario(
        string targetTypedSource,
        string fullyTypedSource,
        string description,
        string typeName,
        int argCount)
    {
        TargetTypedSource = targetTypedSource;
        FullyTypedSource = fullyTypedSource;
        Description = description;
        TypeName = typeName;
        ArgCount = argCount;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests for emission equivalence between target-typed new and fully-typed new.
///
/// Property 5: For any type T, argument list args, and optional property initializer block,
/// the C# code emitted for <c>T x = new(args)</c> SHALL be identical to the C# code emitted
/// for <c>T x = new T(args)</c> (excluding whitespace differences in the declaration itself).
///
/// **Validates: Requirements 6.1, 6.2, 6.3**
///
/// Uses in-process compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
/// </summary>
public sealed class TargetTypedNewEmissionPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Point", "Config", "State", "Item", "Node"];

    private static readonly string[] PropNames =
        ["Name", "Value", "Count", "Size", "Width"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with constructor arguments only (no property initializer).
    /// Validates: Requirements 6.1, 6.3
    /// </summary>
    private static Gen<EmissionEquivalenceScenario> GenWithArgs()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from argCount in Gen.Choose(0, 3)
               let className = ClassNames[classIdx]
               let ctorParams = BuildCtorParams(argCount)
               let ctorArgs = BuildCtorArgs(argCount)
               let classDecl = BuildClassWithCtor(className, argCount)
               let targetTypedSource = classDecl + "\n" +
                   $"class Program {{\n    void Run() {{\n        {className} x = new({ctorArgs});\n    }}\n}}"
               let fullyTypedSource = classDecl + "\n" +
                   $"class Program {{\n    void Run() {{\n        {className} x = new {className}({ctorArgs});\n    }}\n}}"
               select new EmissionEquivalenceScenario(
                   targetTypedSource,
                   fullyTypedSource,
                   $"args({argCount}): {className}",
                   className,
                   argCount);
    }

    /// <summary>
    /// Generates a scenario with constructor arguments and a property initializer block.
    /// Validates: Requirements 6.2, 6.3
    /// </summary>
    private static Gen<EmissionEquivalenceScenario> GenWithArgsAndInitializer()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from argCount in Gen.Choose(0, 2)
               from propCount in Gen.Choose(1, 3)
               let className = ClassNames[classIdx]
               let ctorArgs = BuildCtorArgs(argCount)
               let propNames = PropNames.Take(propCount).ToImmutableArray()
               let initBlock = BuildInitializerBlock(propNames)
               let classDecl = BuildClassWithCtorAndProps(className, argCount, propNames)
               let targetTypedSource = classDecl + "\n" +
                   $"class Program {{\n    void Run() {{\n        {className} x = new({ctorArgs}) {{ {initBlock} }};\n    }}\n}}"
               let fullyTypedSource = classDecl + "\n" +
                   $"class Program {{\n    void Run() {{\n        {className} x = new {className}({ctorArgs}) {{ {initBlock} }};\n    }}\n}}"
               select new EmissionEquivalenceScenario(
                   targetTypedSource,
                   fullyTypedSource,
                   $"args({argCount})+init({propCount}): {className}",
                   className,
                   argCount);
    }

    /// <summary>
    /// Generates a scenario with only a property initializer block (no-args variant).
    /// Validates: Requirements 6.2, 6.3
    /// </summary>
    private static Gen<EmissionEquivalenceScenario> GenNoArgsWithInitializer()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from propCount in Gen.Choose(1, 3)
               let className = ClassNames[classIdx]
               let propNames = PropNames.Take(propCount).ToImmutableArray()
               let initBlock = BuildInitializerBlock(propNames)
               let classDecl = BuildClassWithProps(className, propNames)
               let targetTypedSource = classDecl + "\n" +
                   $"class Program {{\n    void Run() {{\n        {className} x = new {{ {initBlock} }};\n    }}\n}}"
               let fullyTypedSource = classDecl + "\n" +
                   $"class Program {{\n    void Run() {{\n        {className} x = new {className} {{ {initBlock} }};\n    }}\n}}"
               select new EmissionEquivalenceScenario(
                   targetTypedSource,
                   fullyTypedSource,
                   $"no-args+init({propCount}): {className}",
                   className,
                   0);
    }

    public static Arbitrary<EmissionEquivalenceScenario> ArbitraryScenario()
    {
        return Gen.OneOf(GenWithArgs(), GenWithArgsAndInitializer())
            .ToArbitrary();
    }

    // ─── Source builders ────────────────────────────────────────────────

    private static string BuildCtorParams(int argCount)
    {
        return argCount switch
        {
            1 => "int a",
            2 => "int a, int b",
            3 => "int a, int b, int c",
            _ => "",
        };
    }

    private static string BuildCtorArgs(int argCount)
    {
        return argCount switch
        {
            1 => "1",
            2 => "1, 2",
            3 => "1, 2, 3",
            _ => "",
        };
    }

    private static string BuildClassWithCtor(string className, int argCount)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"class {className} {{");
        if(argCount > 0)
        {
            var parms = BuildCtorParams(argCount);
            _ = sb.AppendLine($"    public {className}({parms}) {{ }}");
        }
        _ = sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildClassWithProps(string className, ImmutableArray<string> propNames)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"class {className} {{");
        foreach(var prop in propNames)
        {
            _ = sb.AppendLine($"    public int {prop} {{ get; set; }}");
        }
        _ = sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildClassWithCtorAndProps(
        string className, int argCount, ImmutableArray<string> propNames)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"class {className} {{");
        if(argCount > 0)
        {
            var parms = BuildCtorParams(argCount);
            _ = sb.AppendLine($"    public {className}({parms}) {{ }}");
        }
        foreach(var prop in propNames)
        {
            _ = sb.AppendLine($"    public int {prop} {{ get; set; }}");
        }
        _ = sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildInitializerBlock(ImmutableArray<string> propNames)
    {
        var assignments = propNames.Select((p, i) => $"{p} = {i + 10}");
        return string.Join(", ", assignments);
    }

    // ─── Compilation helper ─────────────────────────────────────────────

    /// <summary>
    /// Compiles Mist source in-process and returns the emitted C# content.
    /// </summary>
    private static (bool Succeeded, string CsContent, string DiagnosticText) CompileInMemory(
        string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(lexBag));
        }

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            return (false, "", FormatDiagnostics(parseBag));
        }

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);

        if(semBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(semBag));
        }

        // Type checker — filter out MIST_TC_0017 (entry point not found) since we're
        // testing emission equivalence, not program structure.
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();
        if(tcErrors.Count > 0)
        {
            return (false, "", string.Join("\n", tcErrors.Select(d => $"  {d.Id.Value}: {d.Message}")));
        }

        // Emit in-memory (no disk I/O)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);

        if(emitBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(emitBag));
        }

        // Concatenate all emitted .cs content
        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return (true, sb.ToString(), "");
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    /// <summary>
    /// Extracts the line containing the <c>new</c> expression from emitted C#.
    /// Returns the trimmed line or null if not found.
    /// </summary>
    private static string? ExtractNewExpressionLine(string csContent, string typeName)
    {
        var lines = csContent.Split('\n');
        foreach(var line in lines)
        {
            var trimmed = line.Trim();
            if(trimmed.Contains($"new {typeName}(") || trimmed.Contains($"new {typeName}()"))
            {
                return trimmed;
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Emission equivalence
    //
    // For any type T, argument list args, and optional property initializer
    // block, the C# code emitted for `T x = new(args)` SHALL be identical
    // to the C# code emitted for `T x = new T(args)`.
    //
    // **Validates: Requirements 6.1, 6.2, 6.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TargetTypedNewEmissionPropertyTests) })]
    public Property EmittedCSharp_IsIdenticalForTargetTypedAndFullyTyped(
        EmissionEquivalenceScenario scenario)
    {
        // Compile the target-typed version
        var (targetSucceeded, targetCs, targetDiag) = CompileInMemory(scenario.TargetTypedSource);
        if(!targetSucceeded)
        {
            return false.ToProperty()
                .Label($"Target-typed compilation failed: {scenario.Description}\n" +
                       $"Diagnostics:\n{targetDiag}\nSource:\n{scenario.TargetTypedSource}");
        }

        // Compile the fully-typed version
        var (fullySucceeded, fullyCs, fullyDiag) = CompileInMemory(scenario.FullyTypedSource);
        if(!fullySucceeded)
        {
            return false.ToProperty()
                .Label($"Fully-typed compilation failed: {scenario.Description}\n" +
                       $"Diagnostics:\n{fullyDiag}\nSource:\n{scenario.FullyTypedSource}");
        }

        // Extract the line containing the new expression from each output
        var targetLine = ExtractNewExpressionLine(targetCs, scenario.TypeName);
        var fullyLine = ExtractNewExpressionLine(fullyCs, scenario.TypeName);

        if(targetLine is null)
        {
            return false.ToProperty()
                .Label($"Could not find 'new {scenario.TypeName}' in target-typed output\n" +
                       $"Emitted:\n{targetCs}");
        }

        if(fullyLine is null)
        {
            return false.ToProperty()
                .Label($"Could not find 'new {scenario.TypeName}' in fully-typed output\n" +
                       $"Emitted:\n{fullyCs}");
        }

        // The emitted lines should be identical
        var areEqual = targetLine == fullyLine;

        return areEqual.ToProperty()
            .Label($"Emission mismatch for {scenario.Description}\n" +
                   $"Target-typed emitted: '{targetLine}'\n" +
                   $"Fully-typed emitted:  '{fullyLine}'");
    }
}