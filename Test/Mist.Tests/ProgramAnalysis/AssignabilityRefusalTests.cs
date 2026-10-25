// Tier 1 (Unit): each refused slot is judged from its bound types and constant, without upstream stages.

using Mist.ProgramAnalysis.Rules;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class AssignabilityRefusalTests
{
    [Theory]
    [InlineData("across-newtypes", "Feet", "Meters", null, "MIST0158")]
    [InlineData("argument", "String", "Int32", null, "MIST0157")]
    [InlineData("array-literal-element", "Int32", "String", 1L, "MIST0157")]
    [InlineData("assignment", "String", "Int32", null, "MIST0157")]
    [InlineData("async-return-value", "String", "Int32", null, "MIST0157")]
    [InlineData("coalesce-right-operand", "String", "Int32", null, "MIST0157")]
    [InlineData("coalescing-assignment", "String", "Int32", null, "MIST0157")]
    [InlineData("constant-into-char", "Int32", "Char", 65L, "MIST0157")]
    [InlineData("constant-out-of-range-into-an-optional", "Int32", "Byte?", 300L, "MIST0157")]
    [InlineData("constant-out-of-range", "Int32", "Byte", 300L, "MIST0157")]
    [InlineData("constant-outside-a-refined-base", "Int32", "SmallAdult", -5L, "MIST0157")]
    [InlineData("constant-outside-a-refined-indexer-base", "Int32", "RefinedByte", -5L, "MIST0157")]
    [InlineData("constant-outside-a-refined-parameter-base", "Int32", "RefinedByte", -5L, "MIST0157")]
    [InlineData("constant-outside-a-refined-union-constituent", "Int32", "SmallOrName", -5L, "MIST0157")]
    [InlineData("constant-outside-a-substituted-refinement-base", "Int32", "SubstitutedByte", 300L, "MIST0157")]
    [InlineData("constructed-type-argument-mismatch", "Box<String>", "Box<Int32>", null, "MIST0157")]
    [InlineData("constructor-argument", "String", "Int32", null, "MIST0157")]
    [InlineData("constructor-chain-argument", "String", "Int32", null, "MIST0157")]
    [InlineData("declared-implicit-after-declared-implicit", "Reading", "FeetClass", null, "MIST0158")]
    [InlineData("deconstruction-element-ambiguous", "Tally", "Double", null, "MIST0156")]
    [InlineData("double-literal-into-float", "Double", "Single", null, "MIST0157")]
    [InlineData("downcast-by-assignment", "Animal", "Dog", null, "MIST0157")]
    [InlineData("enum-cast-result-into-the-enum", "Priority?", "Priority", null, "MIST0157")]
    [InlineData("enum-into-another-enum", "Color", "Priority", null, "MIST0158")]
    [InlineData("enum-variant-out-of-range", "Int32", "Byte", 300L, "MIST0157")]
    [InlineData("enum-variant-value", "String", "Int32", null, "MIST0157")]
    [InlineData("field-initializer", "String", "Int32", null, "MIST0157")]
    [InlineData("foreach-element-ambiguous", "Tally", "Double", null, "MIST0156")]
    [InlineData("foreach-element", "Int32", "String", null, "MIST0157")]
    [InlineData("getter-block-return", "String", "Int32", null, "MIST0157")]
    [InlineData("getter-expression-body", "String", "Int32", null, "MIST0157")]
    [InlineData("implicit-conversion-ambiguous-between-signed-and-unsigned", "Byte", "Gauge", null, "MIST0156")]
    [InlineData("implicit-conversion-ambiguous", "Tally", "Double", null, "MIST0156")]
    [InlineData("index-argument", "String", "Int32", null, "MIST0157")]
    [InlineData("index-into-a-read-only-span-with-a-double", "Double", "Int32", null, "MIST0157")]
    [InlineData("index-into-a-span-with-a-ulong", "UInt64", "Int32", null, "MIST0157")]
    [InlineData("index-into-a-string-with-a-long", "Int64", "Int32", null, "MIST0157")]
    [InlineData("index-into-a-string-with-a-string", "String", "Int32", null, "MIST0157")]
    [InlineData("index-into-an-array-with-a-long", "Int64", "Int32", null, "MIST0157")]
    [InlineData("index-into-an-array-with-a-string", "String", "Int32", null, "MIST0157")]
    [InlineData("index-into-selected-indexer", "Int32", "Byte", 300L, "MIST0157")]
    [InlineData("index-write-into-an-array-with-a-uint", "UInt32", "Int32", null, "MIST0157")]
    [InlineData("inferred-int-enum-into-a-byte", "Flag", "Byte", null, "MIST0157")]
    [InlineData("int-into-an-enum", "Int32", "Color", null, "MIST0157")]
    [InlineData("lambda-block-return", "String", "Int32", null, "MIST0157")]
    [InlineData("lambda-result", "Int32", "String", null, "MIST0157")]
    [InlineData("local-initializer", "String", "Int32", null, "MIST0157")]
    [InlineData("negative-constant-into-unsigned", "Int32", "UInt32", -1L, "MIST0157")]
    [InlineData("newtype-literal-as-an-argument", "Double", "Meters", null, "MIST0158")]
    [InlineData("numeric-narrowing", "Int64", "Int32", null, "MIST0157")]
    [InlineData("object-initializer-value", "String", "Int32", null, "MIST0157")]
    [InlineData("optional-into-its-inner", "Int32?", "Int32", null, "MIST0157")]
    [InlineData("optional-narrowing-lost-on-void-reassignment", "Int32?", "Int32", null, "MIST0157")]
    [InlineData("optional-read-before-coalescing-assignment", "Int32?", "Int32", null, "MIST0157")]
    [InlineData("property-initializer", "String", "Int32", null, "MIST0157")]
    [InlineData("regex-from-a-string-variable", "String", "Regex", null, "MIST0158")]
    [InlineData("return-value", "String", "Int32", null, "MIST0157")]
    [InlineData("struct-newtype-from-its-source", "Point", "Position", null, "MIST0158")]
    [InlineData("switch-arm", "Int32", "String", 1L, "MIST0157")]
    [InlineData("tuple-deconstruction-element", "Int32", "String", null, "MIST0157")]
    [InlineData("top-level-return-of-text", "String", "Int32", null, "MIST0157")]
    [InlineData("top-level-return-of-a-long", "Int64", "Int32", null, "MIST0157")]
    [InlineData("top-level-return-of-unit", "Void", "Int32", null, "MIST0157")]
    [InlineData("tuple-literal", "String", "Int32", null, "MIST0157")]
    [InlineData("union-into-a-constituent", "IntOrString", "Int32", null, "MIST0157")]
    [InlineData("union-not-covered", "IntOrString", "StringOrBool", null, "MIST0157")]
    [InlineData("unknown-into-a-local", "unknown", "Int32", null, "MIST0157")]
    [InlineData("unknown-into-a-parameter", "unknown", "Int32", null, "MIST0157")]
    [InlineData("value-across-two-instantiations-of-a-refinement-alias", "Ne<Int32>", "Ne<String>", null, "MIST0157")]
    [InlineData("value-outside-a-refined-parameter-base", "Int32", "RefinedByte", null, "MIST0157")]
    [InlineData("void-body-return-of-a-constant", "Int32", "Void", 1L, "MIST0157")]
    [InlineData("void-body-return-of-an-optional", "Int32?", "Void", null, "MIST0157")]
    [InlineData("void-body-return-of-a-reference", "Dog", "Void", null, "MIST0157")]
    public void RefusedSlotKeepsItsDiagnostic(string caseName, string from, string to, long? constant, string expected)
    {
        var input = new AssignabilityTypes();
        var diagnostic = caseName.Contains("ambiguous", StringComparison.Ordinal)
            ? Assignability.JudgeSelection(2)
            : Assignability.Judge(input[from], input[to], input.Model.Interner, constant);

        Assert.True(diagnostic?.Number.Value == expected, $"{caseName}: expected {expected}, got {diagnostic?.Number.Value ?? "silence"}");
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AnUnambiguousBoundConversionRemainsSilent(int selected)
    {
        Assert.Null(Assignability.JudgeSelection(selected));
    }
}