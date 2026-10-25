// Tier 2 (Functional): the judge's, the inference's and the readonly amendment's edge cases -- a struct copy's
// owned field, a Clone returning this, an initializer under a pure constructor, captures as inputs, a
// captured struct, an extension setter -- each a hand-built fragment judged where the promise is read. The
// judge table is PurityJudgeTests; the linear drop, the frozen method group and the laundered write are
// rows of PurityRefusalTests.

using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PuritySoundnessTests
{
    private const string Writes = "MIST0137";
    private const string ReadsMutable = "MIST0138";
    private const string CallsImpure = "MIST0139";
    private const string FunctionRequired = "MIST0142";
    private static readonly MistDiagnostic[] PureScope =
    [
        ProgramAnalysisDiagnostics.PureScopeWrites,
        ProgramAnalysisDiagnostics.PureScopeReadsMutable,
        ProgramAnalysisDiagnostics.PureScopeCallsImpure,
        ProgramAnalysisDiagnostics.PureScopeStoresUnowned,
    ];

    [Fact]
    public void AnOwnedFieldBehindAStructCopy_IsNotTheCopys()
    {
        var finding = Assert.Single(Analyze("pure-method-writes-through-an-owned-field-of-a-struct-parameter"));

        Assert.Equal(Writes, finding.Number.Value);
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACloneThatReturnsThis_DoesNotMakeItsResultOwned()
    {
        var finding = Assert.Single(Analyze("pure-method-writes-the-result-of-a-clone-returning-this"));

        Assert.Contains(finding.Descriptor, PureScope);
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldInitializer_IsJudgedUnderAPureConstructor()
    {
        var finding = Assert.Single(Analyze("field-initializer-judged-under-a-pure-constructor"));

        Assert.Contains(finding.Descriptor, PureScope);
        Assert.Contains("Box", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnmarkedConstructorWhoseInitializerReadsAStatic_IsImpureToItsCaller()
    {
        var finding = Assert.Single(Analyze("pure-method-allocates-through-an-unmarked-constructor-with-an-impure-initializer"));

        Assert.Equal(CallsImpure, finding.Number.Value);
        Assert.Contains("'Make'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APureLocalFunction_TreatsItsCapturesAsInputs()
    {
        var finding = Assert.Single(Analyze("pure-local-function-reads-a-captured-mutable-object"));

        Assert.Equal(ReadsMutable, finding.Number.Value);
        Assert.Contains("'F'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalFunctionReadingACapturedMutableObject_DoesNotConvertToAPureFunctionType()
    {
        var finding = Assert.Single(Analyze("local-function-reading-a-captured-mutable-object-to-pure-func"));

        Assert.Contains(finding.Number.Value, new[] { FunctionRequired, ReadsMutable });
    }

    [Theory]
    [InlineData("pure-lambda-captures-a-struct-assigned-after-the-capture")]
    [InlineData("pure-lambda-captures-a-struct-assigned-before-the-capture")]
    public void APureLambdaCapturingAStructWhoseFieldIsAssigned_IsRefused(string fragment)
    {
        Assert.Equal(ReadsMutable, Assert.Single(Analyze(fragment)).Number.Value);
    }

    [Fact]
    public void APureLambdaCapturingAStructNothingAssigns_IsAdmitted()
    {
        Assert.Empty(Analyze("pure-lambda-captures-a-struct-nothing-assigns"));
    }

    [Fact]
    public void AnExtensionSetterThatWritesAStatic_IsRefusedInAPureScope()
    {
        var finding = Assert.Single(Analyze("pure-method-sets-an-extension-property-whose-setter-writes-a-static"));

        Assert.Contains(finding.Descriptor, PureScope);
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAllocationThroughAnImplicitConstructor_KeepsTheAllocatingMemberPure()
    {
        Assert.Empty(Analyze("pure-caller-of-an-unmarked-method-allocating-through-an-implicit-constructor"));
    }

    [Fact]
    public void AReadonlyBlockInsideAPureBody_IsReportedOnce()
    {
        Assert.Equal(Writes, Assert.Single(Analyze("readonly-block-inside-a-pure-body")).Number.Value);
        Assert.Empty(Analyze("readonly-block-inside-a-pure-body-under-the-readonly-rules"));
    }

    private static IReadOnlyList<Diagnostic> Analyze(string fragment)
    {
        return new PuritySoundnessFragments().Analyze(fragment);
    }
}