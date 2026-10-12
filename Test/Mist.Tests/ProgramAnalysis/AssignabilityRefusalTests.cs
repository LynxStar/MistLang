// Tier 1 (Unit): each refused slot is judged from its bound types and constant, without upstream stages.

using Mist.ProgramAnalysis.Rules;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class AssignabilityRefusalTests
{
    [Theory]
    [InlineData("across-newtypes", "Feet", "Meters", null, "MIST_PA_CAST_REQUIRED")]
    [InlineData("argument", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("array-literal-element", "Int32", "String", 1L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("assignment", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("async-return-value", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("coalesce-right-operand", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("coalescing-assignment", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constant-into-char", "Int32", "Char", 65L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constant-out-of-range-into-an-optional", "Int32", "Byte?", 300L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constant-out-of-range", "Int32", "Byte", 300L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constant-outside-a-refined-base", "Int32", "SmallAdult", -5L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constant-outside-a-refined-indexer-base", "Int32", "RefinedByte", -5L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constant-outside-a-refined-parameter-base", "Int32", "RefinedByte", -5L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constant-outside-a-refined-union-constituent", "Int32", "SmallOrName", -5L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constant-outside-a-substituted-refinement-base", "Int32", "SubstitutedByte", 300L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constructed-type-argument-mismatch", "Box<String>", "Box<Int32>", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constructor-argument", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constructor-chain-argument", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("declared-implicit-after-declared-implicit", "Reading", "FeetClass", null, "MIST_PA_CAST_REQUIRED")]
    [InlineData("deconstruction-element-ambiguous", "Tally", "Double", null, "MIST_PA_AMBIGUOUS_IMPLICIT_CONVERSION")]
    [InlineData("double-literal-into-float", "Double", "Single", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("downcast-by-assignment", "Animal", "Dog", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("enum-cast-result-into-the-enum", "Priority?", "Priority", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("enum-into-another-enum", "Color", "Priority", null, "MIST_PA_CAST_REQUIRED")]
    [InlineData("enum-variant-out-of-range", "Int32", "Byte", 300L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("enum-variant-value", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("field-initializer", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("foreach-element-ambiguous", "Tally", "Double", null, "MIST_PA_AMBIGUOUS_IMPLICIT_CONVERSION")]
    [InlineData("foreach-element", "Int32", "String", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("getter-block-return", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("getter-expression-body", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("implicit-conversion-ambiguous-between-signed-and-unsigned", "Byte", "Gauge", null, "MIST_PA_AMBIGUOUS_IMPLICIT_CONVERSION")]
    [InlineData("implicit-conversion-ambiguous", "Tally", "Double", null, "MIST_PA_AMBIGUOUS_IMPLICIT_CONVERSION")]
    [InlineData("index-argument", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("index-into-a-read-only-span-with-a-double", "Double", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("index-into-a-span-with-a-ulong", "UInt64", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("index-into-a-string-with-a-long", "Int64", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("index-into-a-string-with-a-string", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("index-into-an-array-with-a-long", "Int64", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("index-into-an-array-with-a-string", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("index-into-selected-indexer", "Int32", "Byte", 300L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("index-write-into-an-array-with-a-uint", "UInt32", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("inferred-int-enum-into-a-byte", "Flag", "Byte", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("int-into-an-enum", "Int32", "Color", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("lambda-block-return", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("lambda-result", "Int32", "String", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("local-initializer", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("negative-constant-into-unsigned", "Int32", "UInt32", -1L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("newtype-literal-as-an-argument", "Double", "Meters", null, "MIST_PA_CAST_REQUIRED")]
    [InlineData("numeric-narrowing", "Int64", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("object-initializer-value", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("optional-into-its-inner", "Int32?", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("optional-narrowing-lost-on-void-reassignment", "Int32?", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("optional-read-before-coalescing-assignment", "Int32?", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("property-initializer", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("regex-from-a-string-variable", "String", "Regex", null, "MIST_PA_CAST_REQUIRED")]
    [InlineData("return-value", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("struct-newtype-from-its-source", "Point", "Position", null, "MIST_PA_CAST_REQUIRED")]
    [InlineData("switch-arm", "Int32", "String", 1L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("tuple-deconstruction-element", "Int32", "String", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("top-level-return-of-text", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("top-level-return-of-a-long", "Int64", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("top-level-return-of-unit", "Void", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("tuple-literal", "String", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("union-into-a-constituent", "IntOrString", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("union-not-covered", "IntOrString", "StringOrBool", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("unknown-into-a-local", "unknown", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("unknown-into-a-parameter", "unknown", "Int32", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("value-across-two-instantiations-of-a-refinement-alias", "Ne<Int32>", "Ne<String>", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("value-outside-a-refined-parameter-base", "Int32", "RefinedByte", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("void-body-return-of-a-constant", "Int32", "Void", 1L, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("void-body-return-of-an-optional", "Int32?", "Void", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("void-body-return-of-a-reference", "Dog", "Void", null, "MIST_PA_NO_IMPLICIT_CONVERSION")]
    public void RefusedSlotKeepsItsDiagnostic(string caseName, string from, string to, long? constant, string expected)
    {
        var input = new AssignabilityTypes();
        var diagnostic = caseName.Contains("ambiguous", StringComparison.Ordinal)
            ? Assignability.JudgeSelection(2)
            : Assignability.Judge(input[from], input[to], input.Model.Interner, constant);

        Assert.True(diagnostic?.Id.Value == expected, $"{caseName}: expected {expected}, got {diagnostic?.Id.Value ?? "silence"}");
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AnUnambiguousBoundConversionRemainsSilent(int selected)
    {
        Assert.Null(Assignability.JudgeSelection(selected));
    }
}