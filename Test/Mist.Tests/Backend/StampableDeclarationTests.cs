using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class StampableDeclarationTests : ProgramModelProbe
{
    public StampableDeclarationTests()
        : base("MistStampableDeclaration_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public stampable struct Value { public string Item; }", "Item")]
    [InlineData("public stampable struct Value<T> { public T Item; }", "Item")]
    [InlineData("public stampable struct Value<T> where T : struct { public T Item; }", "Item")]
    [InlineData("public struct Parent { public string Item; } public stampable struct Value : Parent { }", "Item")]
    [InlineData("public stampable struct Value { public string Item { get; set; } }", "Item")]
    [InlineData("public stampable struct Value { public string Item { get { return field; } set { field = value; } } }", "Item")]
    public void ProgramModelToAnalysis_DeclarationGuaranteeIsCheckedWithoutAnAllocation(string declaration, string field)
    {
        var model = BuildModel("", declaration, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        var finding = Assert.Single(findings.ToImmutableArray(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("MIST_PA_STAMPABLE_DECLARATION_NOT_SATISFIED", finding.Id.Value);
        Assert.Contains(field, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public stampable struct Value { public int Item; }")]
    [InlineData("public stampable struct Value<T> where T : stampable { public T Item; }")]
    [InlineData("public stampable struct Value<T> { public int Item; }")]
    [InlineData("public stampable struct Value { public static string Shared = \"text\"; public int Item; }")]
    [InlineData("public stampable struct Value { public int Item { get; set; } }")]
    [InlineData("public stampable struct Value { public string Description => \"text\"; public int Item; }")]
    [InlineData("public struct Parent<T> { public T Item; } public stampable struct Value : Parent<int> { }")]
    [InlineData("public struct Value<T> { public T Item; }")]
    public void ProgramModelToAnalysis_DeclarationUsesStorageAndConstraintProof(string declaration)
    {
        var model = BuildModel("", declaration, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        Assert.Empty(findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("public stampable class Value { }")]
    [InlineData("public stampable abstract Value { }")]
    [InlineData("public stampable ref struct Value { }")]
    [InlineData("public stampable type Value = int;")]
    [InlineData("public struct Value { public stampable int Item; }")]
    [InlineData("public struct Value { public stampable int Get() { return 0; } }")]
    public void ProgramModel_StampableModifierRequiresANonRefStruct(string declaration)
    {
        var diagnostics = BuildRun("", declaration);

        var finding = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("MIST_PM_STAMPABLE_REQUIRES_STRUCT", finding.Id.Value);
    }
}