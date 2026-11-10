// Feature: switch-expressions, Property 1: Parse–Print–Parse Round Trip
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.3, 3.4, 4.1–4.8, 11.1–11.6**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class SwitchExpressionSourceScenario
{
    public string Source { get; }

    public string Description { get; }

    public SwitchExpressionSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistSwitchExpressionGenerator
{
    private static readonly string[] VarNames =
        ["x", "y", "z", "val", "item", "input", "data"];

    private static readonly string[] ResultVarNames =
        ["result", "output", "answer", "label", "desc"];

    private static readonly string[] StringLiterals =
        ["\"hello\"", "\"world\"", "\"foo\"", "\"bar\"", "\"test\""];

    private static readonly string[] IntLiterals =
        ["0", "1", "2", "3", "42", "99", "100"];

    private static Gen<string> GenBoolConstantPattern()
    {
        return Gen.Elements("true", "false");
    }

    private static Gen<string> GenIntConstantPattern()
    {
        return Gen.Elements(IntLiterals);
    }

    private static Gen<string> GenStringConstantPattern()
    {
        return Gen.Elements(StringLiterals);
    }

    private static Gen<string> GenUnionTypePattern()
    {
        return from bindName in Gen.Elements("n", "s", "a", "b")
               from typeName in Gen.Elements("int", "string")
               select $"{typeName} {bindName}";
    }

    private static Gen<string> GenVarPattern()
    {
        return Gen.Elements("v", "w", "q").Select(name => $"var {name}");
    }

    private static Gen<string> GenDiscardPattern()
    {
        return Gen.Elements("var _", "_");
    }

    private static Gen<string> GenRelationalPattern()
    {
        return from op in Gen.Elements("<", ">", "<=", ">=")
               from value in Gen.Choose(0, 100)
               select $"{op} {value}";
    }

    private static Gen<string> GenLogicalAndPattern()
    {
        return from lo in Gen.Choose(0, 49)
               from hi in Gen.Choose(50, 100)
               select $">= {lo} and <= {hi}";
    }

    private static Gen<string> GenPropertyPattern()
    {
        return from propValue in Gen.Choose(0, 20)
               from op in Gen.Elements("> ", "<= ", ">= ")
               select "{ Length: " + op + propValue + " }";
    }

    public static Gen<string> GenListPattern()
    {
        return from first in Gen.Choose(0, 5)
               select $"[{first}, ..]";
    }

    private static Gen<string> GenResultExpression()
    {
        return Gen.Elements(StringLiterals);
    }

    private static Gen<string> GenBoolArm()
    {
        return from pattern in GenBoolConstantPattern()
               from result in GenResultExpression()
               select $"{pattern} => {result}";
    }

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

    public static Gen<string> GenUnionArm()
    {
        return from pattern in GenUnionTypePattern()
               from result in GenResultExpression()
               select $"{pattern} => {result}";
    }

    public static Gen<string> GenCatchAllArm()
    {
        return from pattern in Gen.Frequency((3, GenDiscardPattern()), (1, GenVarPattern()))
               from result in GenResultExpression()
               select $"{pattern} => {result}";
    }

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

    /// Int and string scrutinees are exhaustive only with a catch-all arm, so every arm list ends in one.
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

    public static Gen<SwitchExpressionSourceScenario> GenUnionSwitchExpression()
    {
        var exhaustiveByTypes = from result1 in GenResultExpression()
                                from result2 in GenResultExpression()
                                from trailingComma in Gen.Elements(true, false)
                                let comma = trailingComma ? "," : ""
                                select $"int n => {result1}, string s => {result2}{comma}";

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

    /// Reading resultVarToUse afterwards avoids an unused-variable warning.
    private static string WrapInProgram(string body, string? resultVarToUse = null)
    {
        var useStatement = resultVarToUse is not null
            ? $"\n    var __discard = {resultVarToUse};"
            : "";

        return "namespace TestNs;\n\npublic class Program\n{\n    public static void Main()\n    {\n    "
            + body + useStatement + "\n    }\n}";
    }

    private static string WrapInProgramWithUsing(string body)
    {
        return "using Standard;\n\nnamespace TestNs;\n\npublic class Program\n{\n    public static void Main()\n    {\n    "
            + body + "\n    }\n}";
    }

    public static Arbitrary<SwitchExpressionSourceScenario> ArbitrarySwitchExpressionSourceScenario()
    {
        return GenSwitchExpressionSource().ToArbitrary();
    }
}