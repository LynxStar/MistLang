// Generators for Mist switch expression source strings.
// Produces random but structurally valid programs containing switch expressions
// for property-based testing.
// Feature: switch-expressions, Property 1: Parse–Print–Parse Round Trip
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.3, 3.4, 4.1–4.8, 11.1–11.6**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated switch expression source string for property-based testing.
/// Carries the complete Mist program source text along with a human-readable description.
/// </summary>
public sealed class SwitchExpressionSourceScenario
{
    /// <summary>The complete Mist program source string containing a switch expression.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public SwitchExpressionSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// FsCheck generators for Mist programs containing switch expressions.
/// Generates random valid switch expressions covering:
/// <list type="bullet">
///   <item>Scrutinee types: bool, int, string, type unions (int | string)</item>
///   <item>Arm counts: 0–10</item>
///   <item>Pattern types: constant, type, var, discard, property, relational, logical, list</item>
///   <item>Guard presence/absence</item>
///   <item>Trailing comma presence/absence</item>
///   <item>Expression contexts: assignment RHS, method argument, tuple element</item>
/// </list>
/// Each generated program is a complete, compilable Mist source file with namespace,
/// class, and method wrapping.
/// </summary>
public static class MistSwitchExpressionGenerator
{
    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] VarNames =
        ["x", "y", "z", "val", "item", "input", "data"];

    private static readonly string[] ResultVarNames =
        ["result", "output", "answer", "label", "desc"];

    private static readonly string[] StringLiterals =
        ["\"hello\"", "\"world\"", "\"foo\"", "\"bar\"", "\"test\""];

    private static readonly string[] IntLiterals =
        ["0", "1", "2", "3", "42", "99", "100"];

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern generators (produce source text fragments)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates a constant pattern for a bool scrutinee.</summary>
    private static Gen<string> GenBoolConstantPattern() =>
        Gen.Elements("true", "false");

    /// <summary>Generates a constant pattern for an int scrutinee.</summary>
    private static Gen<string> GenIntConstantPattern() =>
        Gen.Elements(IntLiterals);

    /// <summary>Generates a constant pattern for a string scrutinee.</summary>
    private static Gen<string> GenStringConstantPattern() =>
        Gen.Elements(StringLiterals);

    /// <summary>Generates a type pattern for a union scrutinee (int | string).</summary>
    private static Gen<string> GenUnionTypePattern() =>
        from bindName in Gen.Elements("n", "s", "a", "b")
        from typeName in Gen.Elements("int", "string")
        select $"{typeName} {bindName}";

    /// <summary>Generates a var pattern (catch-all that binds).</summary>
    private static Gen<string> GenVarPattern() =>
        Gen.Elements("v", "w", "q").Select(name => $"var {name}");

    /// <summary>Generates a discard pattern (var _ or bare _).</summary>
    private static Gen<string> GenDiscardPattern() =>
        Gen.Elements("var _", "_");

    /// <summary>Generates a relational pattern for an int scrutinee.</summary>
    private static Gen<string> GenRelationalPattern() =>
        from op in Gen.Elements("<", ">", "<=", ">=")
        from value in Gen.Choose(0, 100)
        select $"{op} {value}";

    /// <summary>Generates a logical (and) pattern for an int scrutinee.</summary>
    private static Gen<string> GenLogicalAndPattern() =>
        from lo in Gen.Choose(0, 49)
        from hi in Gen.Choose(50, 100)
        select $">= {lo} and <= {hi}";

    /// <summary>Generates a property pattern for a string scrutinee.</summary>
    private static Gen<string> GenPropertyPattern() =>
        from propValue in Gen.Choose(0, 20)
        from op in Gen.Elements("> ", "<= ", ">= ")
        select "{ Length: " + op + propValue + " }";

    /// <summary>Generates a list pattern for an int array scrutinee.</summary>
    public static Gen<string> GenListPattern() =>
        from first in Gen.Choose(0, 5)
        select $"[{first}, ..]";

    // ═══════════════════════════════════════════════════════════════════════
    // Arm generators (pattern + optional guard + result expression)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates a result expression (string literal for simplicity).</summary>
    private static Gen<string> GenResultExpression() =>
        Gen.Elements(StringLiterals);

    /// <summary>
    /// Generates a single switch arm source text for a bool scrutinee.
    /// </summary>
    private static Gen<string> GenBoolArm()
    {
        return from pattern in GenBoolConstantPattern()
               from result in GenResultExpression()
               select $"{pattern} => {result}";
    }

    /// <summary>
    /// Generates a single switch arm source text for an int scrutinee.
    /// Includes constant, relational, logical, and type-with-guard patterns.
    /// </summary>
    public static Gen<string> GenIntArm()
    {
        var constantArm = from pattern in GenIntConstantPattern()
                          from result in GenResultExpression()
                          select $"{pattern} => {result}";

        var relationalArm = from pattern in GenRelationalPattern()
                            from result in GenResultExpression()
                            select $"{pattern} => {result}";

        var logicalArm = from pattern in GenLogicalAndPattern()
                         from result in GenResultExpression()
                         select $"{pattern} => {result}";

        var typeArmWithGuard = from bindName in Gen.Elements("n", "a", "b")
                               from op in Gen.Elements(">", "<", ">=", "<=")
                               from value in Gen.Choose(0, 50)
                               from result in GenResultExpression()
                               select $"int {bindName} when {bindName} {op} {value} => {result}";

        return Gen.OneOf(constantArm, relationalArm, logicalArm, typeArmWithGuard);
    }

    /// <summary>
    /// Generates a single switch arm source text for a string scrutinee.
    /// Includes constant and property patterns.
    /// </summary>
    public static Gen<string> GenStringArm()
    {
        var constantArm = from pattern in GenStringConstantPattern()
                          from result in GenResultExpression()
                          select $"{pattern} => {result}";

        var propertyArm = from pattern in GenPropertyPattern()
                          from result in GenResultExpression()
                          select $"{pattern} => {result}";

        return Gen.OneOf(constantArm, propertyArm);
    }

    /// <summary>
    /// Generates a single switch arm source text for a union (int | string) scrutinee.
    /// </summary>
    public static Gen<string> GenUnionArm()
    {
        return from pattern in GenUnionTypePattern()
               from result in GenResultExpression()
               select $"{pattern} => {result}";
    }

    /// <summary>
    /// Generates a catch-all arm (var _ or var x).
    /// </summary>
    public static Gen<string> GenCatchAllArm() =>
        from pattern in Gen.Frequency((3, GenDiscardPattern()), (1, GenVarPattern()))
        from result in GenResultExpression()
        select $"{pattern} => {result}";

    // ═══════════════════════════════════════════════════════════════════════
    // Switch expression generators (complete source scenarios)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a complete, exhaustive switch expression for a bool scrutinee.
    /// Bool is exhaustive when both true and false are covered, or a catch-all is present.
    /// </summary>
    public static Gen<SwitchExpressionSourceScenario> GenBoolSwitchExpression()
    {
        var exhaustiveByConstants = from trailingComma in Gen.Elements(true, false)
                                    from result1 in GenResultExpression()
                                    from result2 in GenResultExpression()
                                    let comma = trailingComma ? "," : ""
                                    select $"true => {result1}, false => {result2}{comma}";

        var exhaustiveByCatchAll = from armCount in Gen.Choose(0, 2)
                                   from arms in Gen.ArrayOf(GenBoolArm(), armCount)
                                   from catchAll in GenCatchAllArm()
                                   from trailingComma in Gen.Elements(true, false)
                                   let comma = trailingComma ? "," : ""
                                   let allArms = arms.Length > 0
                                       ? string.Join(", ", arms) + ", " + catchAll + comma
                                       : catchAll + comma
                                   select allArms;

        return from varName in Gen.Elements(VarNames)
               from resultVar in Gen.Elements(ResultVarNames)
               from armsText in Gen.OneOf(exhaustiveByConstants, exhaustiveByCatchAll)
               let body = $"bool {varName} = true;\n    var {resultVar} = {varName} switch {{ {armsText} }};"
               let source = WrapInProgram(body, resultVar)
               select new SwitchExpressionSourceScenario(source, $"bool switch: {varName}");
    }

    /// <summary>
    /// Generates a complete, exhaustive switch expression for an int scrutinee.
    /// Int requires a catch-all for exhaustiveness.
    /// </summary>
    public static Gen<SwitchExpressionSourceScenario> GenIntSwitchExpression()
    {
        return from varName in Gen.Elements(VarNames)
               from resultVar in Gen.Elements(ResultVarNames)
               from value in Gen.Choose(0, 100)
               from armCount in Gen.Choose(0, 10)
               from arms in Gen.ArrayOf(GenIntArm(), armCount)
               from catchAll in GenCatchAllArm()
               from trailingComma in Gen.Elements(true, false)
               let comma = trailingComma ? "," : ""
               let allArms = arms.Length > 0
                   ? string.Join(", ", arms) + ", " + catchAll + comma
                   : catchAll + comma
               let body = $"int {varName} = {value};\n    var {resultVar} = {varName} switch {{ {allArms} }};"
               let source = WrapInProgram(body, resultVar)
               select new SwitchExpressionSourceScenario(source, $"int switch({armCount} arms): {varName}");
    }

    /// <summary>
    /// Generates a complete, exhaustive switch expression for a string scrutinee.
    /// String requires a catch-all for exhaustiveness.
    /// </summary>
    public static Gen<SwitchExpressionSourceScenario> GenStringSwitchExpression()
    {
        return from varName in Gen.Elements(VarNames)
               from resultVar in Gen.Elements(ResultVarNames)
               from strVal in Gen.Elements(StringLiterals)
               from armCount in Gen.Choose(0, 4)
               from arms in Gen.ArrayOf(GenStringArm(), armCount)
               from catchAll in GenCatchAllArm()
               from trailingComma in Gen.Elements(true, false)
               let comma = trailingComma ? "," : ""
               let allArms = arms.Length > 0
                   ? string.Join(", ", arms) + ", " + catchAll + comma
                   : catchAll + comma
               let body = $"string {varName} = {strVal};\n    var {resultVar} = {varName} switch {{ {allArms} }};"
               let source = WrapInProgram(body, resultVar)
               select new SwitchExpressionSourceScenario(source, $"string switch({armCount} arms): {varName}");
    }

    /// <summary>
    /// Generates a complete, exhaustive switch expression for a type union (int | string) scrutinee.
    /// Union is exhaustive when all constituent types are covered by type patterns, or a catch-all is present.
    /// </summary>
    public static Gen<SwitchExpressionSourceScenario> GenUnionSwitchExpression()
    {
        // Exhaustive by covering all types
        var exhaustiveByTypes = from result1 in GenResultExpression()
                                from result2 in GenResultExpression()
                                from trailingComma in Gen.Elements(true, false)
                                let comma = trailingComma ? "," : ""
                                select $"int n => {result1}, string s => {result2}{comma}";

        // Exhaustive by catch-all
        var exhaustiveByCatchAll = from armCount in Gen.Choose(0, 2)
                                   from arms in Gen.ArrayOf(GenUnionArm(), armCount)
                                   from catchAll in GenCatchAllArm()
                                   from trailingComma in Gen.Elements(true, false)
                                   let comma = trailingComma ? "," : ""
                                   let allArms = arms.Length > 0
                                       ? string.Join(", ", arms) + ", " + catchAll + comma
                                       : catchAll + comma
                                   select allArms;

        return from varName in Gen.Elements(VarNames)
               from resultVar in Gen.Elements(ResultVarNames)
               from armsText in Gen.OneOf(exhaustiveByTypes, exhaustiveByCatchAll)
               let body = $"int | string {varName} = 42;\n    var {resultVar} = {varName} switch {{ {armsText} }};"
               let source = WrapInProgram(body, resultVar)
               select new SwitchExpressionSourceScenario(source, $"union switch: {varName}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Context generators (switch in different expression positions)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a switch expression used as a method argument.
    /// </summary>
    public static Gen<SwitchExpressionSourceScenario> GenSwitchAsMethodArgument()
    {
        return from varName in Gen.Elements(VarNames)
               from value in Gen.Choose(0, 100)
               from armCount in Gen.Choose(0, 3)
               from arms in Gen.ArrayOf(GenIntArm(), armCount)
               from catchAll in GenCatchAllArm()
               from trailingComma in Gen.Elements(true, false)
               let comma = trailingComma ? "," : ""
               let allArms = arms.Length > 0
                   ? string.Join(", ", arms) + ", " + catchAll + comma
                   : catchAll + comma
               let body = $"int {varName} = {value};\n    Console.WriteLine({varName} switch {{ {allArms} }});"
               let source = WrapInProgramWithUsing(body)
               select new SwitchExpressionSourceScenario(source, $"switch as argument: {varName}");
    }

    /// <summary>
    /// Generates a switch expression used as a tuple element.
    /// </summary>
    public static Gen<SwitchExpressionSourceScenario> GenSwitchAsTupleElement()
    {
        return from varName in Gen.Elements(VarNames)
               from value in Gen.Choose(0, 100)
               from catchAll in GenCatchAllArm()
               from trailingComma in Gen.Elements(true, false)
               let comma = trailingComma ? "," : ""
               let switchExpr = $"{varName} switch {{ {catchAll}{comma} }}"
               let body = $"int {varName} = {value};\n    var tuple = ({switchExpr}, 42);"
               let source = WrapInProgram(body, "tuple")
               select new SwitchExpressionSourceScenario(source, $"switch as tuple element: {varName}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Combined generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random switch expression source scenario covering all variants.
    /// </summary>
    public static Gen<SwitchExpressionSourceScenario> GenSwitchExpressionSource()
    {
        return Gen.OneOf(
            GenBoolSwitchExpression(),
            GenIntSwitchExpression(),
            GenStringSwitchExpression(),
            GenUnionSwitchExpression(),
            GenSwitchAsMethodArgument(),
            GenSwitchAsTupleElement());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Program wrapping helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps a body of statements in a complete Mist program with namespace, class, and Main method.
    /// Optionally references a result variable to prevent unused-variable warnings.
    /// </summary>
    private static string WrapInProgram(string body, string? resultVarToUse = null)
    {
        var useStatement = resultVarToUse is not null
            ? $"\n    var __discard = {resultVarToUse};"
            : "";

        return "namespace TestNs;\n\npublic class Program\n{\n    public static void Main()\n    {\n    "
            + body + useStatement + "\n    }\n}";
    }

    /// <summary>
    /// Wraps a body in a program that includes <c>using Standard;</c> for Console access.
    /// </summary>
    private static string WrapInProgramWithUsing(string body)
    {
        return "using Standard;\n\nnamespace TestNs;\n\npublic class Program\n{\n    public static void Main()\n    {\n    "
            + body + "\n    }\n}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registration
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FsCheck Arbitrary for <see cref="SwitchExpressionSourceScenario"/>.
    /// Register with <c>[Property(Arbitrary = new[] { typeof(MistSwitchExpressionGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<SwitchExpressionSourceScenario> ArbitrarySwitchExpressionSourceScenario() =>
        GenSwitchExpressionSource().ToArbitrary();
}