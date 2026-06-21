// Feature: target-typed-new, Property 1: Parsing round-trip
// **Validates: Requirements 2.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a test scenario for target-typed new expression round-trip testing.
/// Includes generated source code and a description of the variant being tested.
/// </summary>
public sealed class TargetTypedNewRoundTripScenario
{
    /// <summary>The complete Mist source string containing a target-typed new expression.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public TargetTypedNewRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests for parsing round-trip of target-typed new expressions.
///
/// Property 1: For any valid Mist source containing a target-typed new expression
/// (with or without arguments, with or without property initializers), parsing the
/// source into an AST, pretty-printing that AST back to source text, and re-parsing
/// should produce an equivalent AST. Equivalence is verified via idempotence of
/// parse → pretty-print (printed1 == printed2).
///
/// **Validates: Requirements 2.5**
/// </summary>
public class TargetTypedNewRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<TargetTypedNewRoundTripScenario> ArbitraryScenario() =>
        TargetTypedNewRoundTripGenerator.GenScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Parsing round-trip
    //
    // **Validates: Requirements 2.5**
    //
    // For any valid Mist source containing a target-typed new expression,
    // parsing and pretty-printing the result, then parsing and pretty-printing
    // again, SHALL produce identical output (idempotence / round-trip stability).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TargetTypedNewRoundTripPropertyTests) })]
    public Property RoundTrip_TargetTypedNew_PrintParseIsIdempotent(TargetTypedNewRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if (string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}

/// <summary>
/// FsCheck generators for Mist source strings containing target-typed new expressions.
/// Generates random valid target-typed new expressions covering:
/// <list type="bullet">
///   <item>No arguments: <c>new()</c></item>
///   <item>With varying argument counts: <c>new(arg1, arg2)</c></item>
///   <item>With property initializer block: <c>new(args) { Prop = value }</c></item>
///   <item>No-args with property initializer (no parens): <c>new { Prop = value }</c></item>
///   <item>With arguments and property initializer: <c>new(args) { Prop = value }</c></item>
/// </list>
/// </summary>
public static class TargetTypedNewRoundTripGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Point", "Config", "Settings", "Options", "Data", "State", "Node", "Item"];

    private static readonly string[] PropertyNames =
        ["Name", "Value", "Count", "Size", "Width", "Height", "Timeout", "Label", "Score", "Enabled"];

    // ─── Value expression generators ────────────────────────────────────

    private static Gen<string> GenIntExpr() =>
        Gen.Choose(0, 999).Select(n => n.ToString());

    private static Gen<string> GenBoolExpr() =>
        Gen.Elements("true", "false");

    private static Gen<string> GenStringExpr() =>
        Gen.Elements("\"hello\"", "\"world\"", "\"test\"", "\"\"");

    private static Gen<string> GenValueExpr() =>
        Gen.OneOf(
            GenIntExpr(),
            GenBoolExpr(),
            GenStringExpr());

    // ─── Argument generators ────────────────────────────────────────────

    private static Gen<string> GenArgList(int count)
    {
        if (count == 0)
            return Gen.Constant("");

        var gens = Enumerable.Range(0, count)
            .Select(_ => GenValueExpr())
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(args => string.Join(", ", args));
    }

    // ─── Property assignment generators ─────────────────────────────────

    private static Gen<string> GenAssignment(int index) =>
        from value in GenValueExpr()
        select $"{PropertyNames[index]} = {value}";

    private static Gen<string> GenAssignments(int count)
    {
        if (count == 0)
            return Gen.Constant("");

        var gens = Enumerable.Range(0, count)
            .Select(i => GenAssignment(i))
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(assignments => string.Join(", ", assignments));
    }

    // ─── Scenario generators ────────────────────────────────────────────

    /// <summary>
    /// Generates a target-typed new expression with no arguments and no initializer.
    /// Example: <c>Point p = new();</c>
    /// </summary>
    private static Gen<TargetTypedNewRoundTripScenario> GenNoArgsNoInitializer()
    {
        return from typeName in Gen.Elements(TypeNames)
               let newExpr = "new()"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source, $"no-args: {typeName} x = new()");
    }

    /// <summary>
    /// Generates a target-typed new expression with arguments and no initializer.
    /// Example: <c>Point p = new(1, 2);</c>
    /// </summary>
    private static Gen<TargetTypedNewRoundTripScenario> GenWithArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from argCount in Gen.Choose(1, 4)
               from args in GenArgList(argCount)
               let newExpr = $"new({args})"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source,
                   $"with-args({argCount}): {typeName} x = new({args})");
    }

    /// <summary>
    /// Generates a target-typed new expression with no arguments but with property initializer.
    /// Example: <c>Point p = new() { X = 1, Y = 2 };</c>
    /// </summary>
    private static Gen<TargetTypedNewRoundTripScenario> GenNoArgsWithInitializer()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 4)
               from assignments in GenAssignments(assignmentCount)
               let newExpr = $"new() {{ {assignments} }}"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source,
                   $"no-args-with-init({assignmentCount}): {typeName} x = new() {{ ... }}");
    }

    /// <summary>
    /// Generates a target-typed new expression with arguments and property initializer.
    /// Example: <c>Point p = new(1, 2) { Label = "hello" };</c>
    /// </summary>
    private static Gen<TargetTypedNewRoundTripScenario> GenWithArgsAndInitializer()
    {
        return from typeName in Gen.Elements(TypeNames)
               from argCount in Gen.Choose(1, 3)
               from args in GenArgList(argCount)
               from assignmentCount in Gen.Choose(1, 4)
               from assignments in GenAssignments(assignmentCount)
               let newExpr = $"new({args}) {{ {assignments} }}"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source,
                   $"with-args-and-init({argCount},{assignmentCount}): {typeName} x = new({args}) {{ ... }}");
    }

    /// <summary>
    /// Generates a target-typed new expression with only a property initializer (no parens).
    /// Example: <c>Point p = new { X = 1, Y = 2 };</c>
    /// </summary>
    private static Gen<TargetTypedNewRoundTripScenario> GenNoParensWithInitializer()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 4)
               from assignments in GenAssignments(assignmentCount)
               let newExpr = $"new {{ {assignments} }}"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source,
                   $"no-parens-with-init({assignmentCount}): {typeName} x = new {{ ... }}");
    }

    // ─── Combined generator ─────────────────────────────────────────────

    /// <summary>
    /// Generates a random target-typed new expression scenario covering all variants.
    /// </summary>
    public static Gen<TargetTypedNewRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNoArgsNoInitializer(),
            GenWithArgs(),
            GenNoArgsWithInitializer(),
            GenWithArgsAndInitializer(),
            GenNoParensWithInitializer());
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a target-typed new expression in a minimal compilable Mist program structure.
    /// Uses a typed variable declaration so the target-typed new has a context type.
    /// </summary>
    private static string WrapInProgram(string typeName, string newExpr)
    {
        return $@"class Program {{
    public static void Main() {{
        {typeName} x = {newExpr};
    }}
}}";
    }
}
