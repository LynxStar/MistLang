using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class StackallocLengthWideningTests
{
    private const string Types = """
        public struct Pair<T> { public int value; }
        type PairAlias<T> = Pair<T>;
        type ClosedPairAlias = Pair<int>;
        """;

    [Theory]
    [InlineData("Span<int>", "int")]
    [InlineData("ReadOnlySpan<int>", "int")]
    [InlineData("Span<Pair<int>>", "Pair<int>")]
    [InlineData("ReadOnlySpan<Pair<int>>", "Pair<int>")]
    [InlineData("Span<Pair<int>>", "PairAlias<int>")]
    [InlineData("ReadOnlySpan<Pair<int>>", "ClosedPairAlias")]
    [InlineData("Span<Pair<int>, unknown>", "Pair<int>")]
    [InlineData("ReadOnlySpan<Pair<int>, unknown>", "Pair<int>")]
    public void ProgramModelToAnalysis_KnownLengthsWidenIntoStandardUnknownLengthAliases(
        string target, string element)
    {
        var model = StandardModelProbe.Build(
            $"{target} actual = stackalloc {element}[2];", out var diagnostics, Types);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var declaration = Assert.Single(Declarations(model));
        var allocation = Assert.IsType<StackallocExpression>(declaration.Initializer);
        var targetType = Assert.IsType<ConstructedType>(declaration.Local.Type);

        _ = Assert.IsType<UnknownType>(targetType.TypeArguments[1]);
        Assert.Equal(ConversionKind.Implicit,
            ConversionClassifier.Classify(allocation.Type, targetType, model.TypeInterner));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        Assert.Empty(findings.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error
            && d.Location.Path == "Probe.mist"
            && d.Number.Value is "MIST0157" or "MIST0158"));
    }

    [Theory]
    [InlineData("Span<int, true>", "Span<int>")]
    [InlineData("Span<int, 2>", "Span<int, 3>")]
    [InlineData("Span<Pair<int>, 2>", "Span<Pair<long>>")]
    [InlineData("Span<int, 2>", "Span<unknown>")]
    public void UnknownDependentSlotsPreserveDomainAndOrdinaryTypeArgumentRestrictions(
        string source, string target)
    {
        var model = StandardModelProbe.Build($$"""
            {{source}} actual = default({{source}});
            {{target}} expected = default({{target}});
            """, out var diagnostics, Types);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var declarations = Declarations(model);

        Assert.Equal(ConversionKind.None,
            ConversionClassifier.Classify(declarations[0].Local.Type, declarations[1].Local.Type, model.TypeInterner));
    }

    [Fact]
    public void ExistingUnknownValueDomainsKeepTheirRefinementRestrictions()
    {
        var integer = new StructType("Int32", "Standard", null, null, null);
        var refined = new RefinedType(integer, new AndPatternNode(), "NonNegative");
        var definition = new StructType("Values<>", "Probe", null, null, null);
        var source = new ConstructedType(definition, [new LiftedType(integer, 2L)]);
        var unknownInteger = new ConstructedType(definition, [new UnknownValueType(integer)]);
        var unknownRefined = new ConstructedType(definition, [new UnknownValueType(refined)]);

        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(source, unknownInteger));
        Assert.Equal(ConversionKind.None, ConversionClassifier.Classify(source, unknownRefined));
    }

    private static LocalDeclarationStatement[] Declarations(ProgramModelClass model)
    {
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var body = probe.MethodsList.Single(method => method.Name == "Run").Body;
        Assert.NotNull(body);

        return body.Statements.OfType<LocalDeclarationStatement>().ToArray();
    }
}