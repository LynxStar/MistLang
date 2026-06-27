// Feature: property-initializers, Property 2: Emitter produces valid C# initializer syntax
// **Validates: Requirements 8.1, 8.2, 8.3**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Describes a generated property initializer emission scenario for property testing.
/// </summary>
public sealed class PropertyInitializerEmissionScenario
{
    /// <summary>The complete Mist source string containing a property initializer expression.</summary>
    public string MistSource { get; }

    /// <summary>Human-readable description of the scenario.</summary>
    public string Description { get; }

    /// <summary>The property names used in the initializer block, in order.</summary>
    public ImmutableArray<string> PropertyNames { get; }

    /// <summary>The expected emitted value expressions, in order.</summary>
    public ImmutableArray<string> ExpectedValues { get; }

    /// <summary>Whether the scenario uses constructor arguments.</summary>
    public bool HasConstructorArgs { get; }

    public PropertyInitializerEmissionScenario(
        string mistSource,
        string description,
        ImmutableArray<string> propertyNames,
        ImmutableArray<string> expectedValues,
        bool hasConstructorArgs)
    {
        MistSource = mistSource;
        Description = description;
        PropertyNames = propertyNames;
        ExpectedValues = expectedValues;
        HasConstructorArgs = hasConstructorArgs;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests for C# emission of property initializer blocks.
///
/// **Validates: Requirements 8.1, 8.2, 8.3**
///
/// For any valid <c>new</c> expression with a property initializer block containing N
/// assignments, the emitted C# SHALL contain <c>{ Name1 = expr1, Name2 = expr2, ..., NameN = exprN }</c>
/// immediately after the constructor call.
///
/// Uses in-process compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
/// </summary>
public sealed class PropertyInitializerEmissionPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Config", "Options", "Settings", "Data", "State"];

    private static readonly string[] PropNames =
        ["Name", "Value", "Count", "Size", "Width", "Height", "Timeout", "Label", "Score", "Enabled"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<int> GenIntValue()
    {
        return Gen.Choose(0, 999);
    }

    private static Gen<(string MistExpr, string CSharpExpr)> GenValueExpression()
    {
        return GenIntValue().Select(n => (n.ToString(), n.ToString()));
    }

    /// <summary>
    /// Generates a scenario with constructor arguments and N property assignments.
    /// </summary>
    private static Gen<PropertyInitializerEmissionScenario> GenWithArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 5)
               from values in Gen.ArrayOf(GenValueExpression(), assignmentCount)
               let propNames = PropNames.Take(assignmentCount).ToImmutableArray()
               let mistAssignments = string.Join(", ",
                   propNames.Zip(values, (p, v) => $"{p} = {v.MistExpr}"))
               let expectedValues = values.Select(v => v.CSharpExpr).ToImmutableArray()
               let classDecl = BuildClassDeclaration(typeName, propNames)
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new {typeName}() {{ {mistAssignments} }};\n    }}\n}}"
               select new PropertyInitializerEmissionScenario(
                   source,
                   $"with-args({assignmentCount}): new {typeName}() {{ ... }}",
                   propNames,
                   expectedValues,
                   hasConstructorArgs: true);
    }

    /// <summary>
    /// Generates a scenario without constructor arguments (no-args variant) and N property assignments.
    /// </summary>
    private static Gen<PropertyInitializerEmissionScenario> GenNoArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 5)
               from values in Gen.ArrayOf(GenValueExpression(), assignmentCount)
               let propNames = PropNames.Take(assignmentCount).ToImmutableArray()
               let mistAssignments = string.Join(", ",
                   propNames.Zip(values, (p, v) => $"{p} = {v.MistExpr}"))
               let expectedValues = values.Select(v => v.CSharpExpr).ToImmutableArray()
               let classDecl = BuildClassDeclaration(typeName, propNames)
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new {typeName} {{ {mistAssignments} }};\n    }}\n}}"
               select new PropertyInitializerEmissionScenario(
                   source,
                   $"no-args({assignmentCount}): new {typeName} {{ ... }}",
                   propNames,
                   expectedValues,
                   hasConstructorArgs: false);
    }

    public static Arbitrary<PropertyInitializerEmissionScenario> ArbitraryScenario()
    {
        return Gen.OneOf(GenWithArgs(), GenNoArgs()).ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Mist class declaration with settable int properties for the given names.
    /// </summary>
    private static string BuildClassDeclaration(string typeName, ImmutableArray<string> propNames)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"class {typeName} {{");
        foreach(var prop in propNames)
        {
            _ = sb.AppendLine($"    public int {prop} {{ get; set; }}");
        }

        _ = sb.AppendLine("}");
        return sb.ToString();
    }

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

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        if(tcBag.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return (false, "", FormatDiagnostics(tcBag));
        }

        // Emit in-memory (no disk I/O)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, emitBag);

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
    /// Builds the expected initializer pattern: <c>{ Name1 = expr1, Name2 = expr2, ..., NameN = exprN }</c>
    /// </summary>
    private static string BuildExpectedInitializerPattern(
        ImmutableArray<string> propNames, ImmutableArray<string> expectedValues)
    {
        var assignments = propNames.Zip(expectedValues, (p, v) => $"{p} = {v}");
        return $"{{ {string.Join(", ", assignments)} }}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Emitter produces valid C# initializer syntax
    //
    // **Validates: Requirements 8.1, 8.2, 8.3**
    //
    // For any valid `new` expression with a property initializer block
    // containing N assignments, the emitted C# SHALL contain
    // `{ Name1 = expr1, Name2 = expr2, ..., NameN = exprN }` immediately
    // after the constructor call.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyInitializerEmissionPropertyTests) })]
    public Property EmittedCSharp_ContainsInitializerSyntax(
        PropertyInitializerEmissionScenario scenario)
    {
        var (succeeded, csContent, diagnosticText) = CompileInMemory(scenario.MistSource);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation failed: {scenario.Description}\nDiagnostics:\n{diagnosticText}\nSource:\n{scenario.MistSource}");
        }

        if(string.IsNullOrWhiteSpace(csContent))
        {
            return false.ToProperty()
                .Label($"C# output was empty: {scenario.Description}");
        }

        // Requirement 8.1: Emitted C# contains object initializer syntax
        var expectedPattern = BuildExpectedInitializerPattern(
            scenario.PropertyNames, scenario.ExpectedValues);

        if(!csContent.Contains(expectedPattern))
        {
            return false.ToProperty()
                .Label($"Missing initializer pattern '{expectedPattern}'\nEmitted:\n{csContent}\nSource:\n{scenario.MistSource}");
        }

        // Requirement 8.2: Each property assignment appears as Name = expr
        foreach(var (propName, expectedValue) in scenario.PropertyNames.Zip(
            scenario.ExpectedValues, (p, v) => (p, v)))
        {
            var assignmentPattern = $"{propName} = {expectedValue}";
            if(!csContent.Contains(assignmentPattern))
            {
                return false.ToProperty()
                    .Label($"Missing assignment '{assignmentPattern}' in emitted C#\nEmitted:\n{csContent}");
            }
        }

        // Requirement 8.3: Assignments are comma-separated
        // Verify the full comma-separated list appears in the output
        var commaSeparatedAssignments = string.Join(", ",
            scenario.PropertyNames.Zip(scenario.ExpectedValues, (p, v) => $"{p} = {v}"));
        if(!csContent.Contains(commaSeparatedAssignments))
        {
            return false.ToProperty()
                .Label($"Assignments not comma-separated: expected '{commaSeparatedAssignments}'\nEmitted:\n{csContent}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }
}