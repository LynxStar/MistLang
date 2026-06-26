// Feature: property-initializers, Property 9: Omitted argument list emits empty parens in C#
// **Validates: Requirements 8.4**

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
/// Describes a scenario where a <c>new Type { ... }</c> expression (no-args variant)
/// is compiled and emitted to C#.
/// </summary>
public sealed class NoArgsEmissionScenario
{
    public string MistSource { get; }
    public string Description { get; }
    public string TypeName { get; }
    public ImmutableArray<string> PropertyNames { get; }

    public NoArgsEmissionScenario(
        string mistSource, string description,
        string typeName, ImmutableArray<string> propertyNames)
    {
        MistSource = mistSource;
        Description = description;
        TypeName = typeName;
        PropertyNames = propertyNames;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests verifying that the no-args variant of property initializer
/// expressions (<c>new Type { ... }</c>) always emits explicit empty parentheses
/// <c>()</c> in the C# output.
///
/// **Validates: Requirements 8.4**
///
/// For any <c>new</c> expression using the no-args variant, the emitted C# SHALL
/// include an explicit empty argument list <c>()</c> before the initializer block.
/// </summary>
public sealed class PropertyInitializerNoArgsEmissionPropertyTests
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

    public static Arbitrary<NoArgsEmissionScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<NoArgsEmissionScenario> GenScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 5)
               from values in Gen.ArrayOf(Gen.Choose(0, 999), assignmentCount)
               let propNames = PropNames.Take(assignmentCount).ToImmutableArray()
               let mistAssignments = string.Join(", ",
                   propNames.Zip(values, (p, v) => $"{p} = {v}"))
               let classDecl = BuildClassDeclaration(typeName, propNames)
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new {typeName} {{ {mistAssignments} }};\n    }}\n}}"
               select new NoArgsEmissionScenario(
                   source,
                   $"no-args({assignmentCount}): new {typeName} {{ ... }}",
                   typeName,
                   propNames);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Omitted argument list emits empty parens in C#
    //
    // **Validates: Requirements 8.4**
    //
    // For any `new` expression using the no-args variant (`new Type { ... }`),
    // the emitted C# SHALL include an explicit empty argument list `()` before
    // the initializer block.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyInitializerNoArgsEmissionPropertyTests) })]
    public Property NoArgsVariant_EmitsExplicitEmptyParens(NoArgsEmissionScenario scenario)
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

        // The emitted C# must contain `new TypeName()` with explicit empty parens
        // followed by the initializer block `{`
        var expectedPattern = $"new {scenario.TypeName}()";
        if(!csContent.Contains(expectedPattern))
        {
            return false.ToProperty()
                .Label($"Missing explicit empty parens pattern '{expectedPattern}'\n" +
                       $"Emitted:\n{csContent}\nSource:\n{scenario.MistSource}");
        }

        // Additionally verify the pattern `new TypeName() {` appears (parens before brace)
        var patternWithBrace = $"new {scenario.TypeName}() {{";
        if(!csContent.Contains(patternWithBrace))
        {
            return false.ToProperty()
                .Label($"Missing pattern '{patternWithBrace}' (parens before initializer brace)\n" +
                       $"Emitted:\n{csContent}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }
}