using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Model = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.ProgramAnalysis;

public sealed class AnalysisTimingsTests
{
    [Fact]
    [Trait("Tier", "1")]
    public void RepeatedRecordsForOneAnalyzerSumIntoOneEntry()
    {
        var timings = new AnalysisTimings();
        var analyzer = new First();

        timings.Record(analyzer, TimeSpan.FromSeconds(1));
        timings.Record(analyzer, TimeSpan.FromSeconds(2));

        var entry = Assert.Single(timings.Analyzers);
        Assert.Equal(new AnalyzerTiming("shared", nameof(First), TimeSpan.FromSeconds(3)), entry);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnalyzersSharingAnIdStayApartInTheOrderTheyFirstRan()
    {
        var timings = new AnalysisTimings();
        var first = new First();
        var second = new Second();

        timings.Record(second, TimeSpan.FromSeconds(1));
        timings.Record(first, TimeSpan.FromSeconds(2));
        timings.Record(second, TimeSpan.FromSeconds(4));

        Assert.Equal(
            [new AnalyzerTiming("shared", nameof(Second), TimeSpan.FromSeconds(5)), new AnalyzerTiming("shared", nameof(First), TimeSpan.FromSeconds(2))],
            timings.Analyzers);
        Assert.Equal(TimeSpan.FromSeconds(7), timings.InAnalyzers);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ScopesAreCountedByKind()
    {
        var timings = new AnalysisTimings();

        timings.RecordScope(AnalysisScopeKind.CloneConversion);
        timings.RecordScope(AnalysisScopeKind.Program);
        timings.RecordScope(AnalysisScopeKind.ClonedType);
        timings.RecordScope(AnalysisScopeKind.CloneConversion);

        Assert.Equal((1, 2, 1, 4), (timings.ProgramScopes, timings.CloneConversionScopes, timings.ClonedTypeScopes, timings.Scopes));
    }

    // Only the runner knows which conversion bodies are judged and which clones are analysed, so this is the whole phase over a hand-built model.
    [Fact]
    [Trait("Tier", "3")]
    public void TheRunnerTimesEachAnalyzerOnceAcrossTheScopesItActuallyAnalyses()
    {
        var model = new Model();
        var assembly = model.AddSelfAssembly("Probe");
        var declaration = new BodyTypeTypeDeclarationNode { Span = new Storm.Runtime.SourceSpan(0, 100), Origin = Origin("source.mist") };
        var source = new ClassType("Tiger", "Probe", new Scope(null), null, declaration) { Modifiers = Public, Assembly = assembly };
        var clone = new ClassType("Jaguar", "Probe", new Scope(null), null, declaration)
        {
            CloneSource = source,
            CloneSourceType = source,
            NewtypeDeclaration = new NewtypeDeclarationNode { Span = new Storm.Runtime.SourceSpan(0, 10), Origin = Origin("newtype.mist") },
            Modifiers = Public,
            Assembly = assembly,
        };
        clone.CastOperatorsList.Add(Conversion(clone, isImplicit: false));
        var unbound = Conversion(clone, isImplicit: true);
        unbound.BindingFailure = new Diagnostic(
            new DiagnosticId("MIST_PROBE"), DiagnosticSeverity.Error, PipelineStage.SemanticModelConstruction, "unbound", default, "source.mist", null);
        clone.CastOperatorsList.Add(unbound);
        var unit = new CompilationUnit { SourcePath = "source.mist" };
        unit.Types.AddRange([source, clone]);
        assembly.CompilationUnits.Add(unit);
        assembly.CompilationUnits.Add(new CompilationUnit { SourcePath = "newtype.mist" });
        _ = model.RegisterType(source);
        _ = model.RegisterType(clone);
        var timings = new AnalysisTimings();

        ProgramAnalysisRunner.Run(model, new DiagnosticBag(), timings);

        Assert.Equal((1, 1, 1), (timings.ProgramScopes, timings.CloneConversionScopes, timings.ClonedTypeScopes));
        Assert.Equal(timings.Analyzers.Count, timings.Analyzers.Select(analyzer => analyzer.Name).Distinct().Count());
        Assert.True(timings.Analyzers.Select(analyzer => analyzer.Id).Distinct().Count() < timings.Analyzers.Count);
    }

    private static MemberModifiers Public { get; } = new() { Accessibility = Accessibility.Public };

    private static CastOperator Conversion(ClassType clone, bool isImplicit)
    {
        var within = new ConversionScope(clone.Scope);
        var value = new LiteralExpression(Node(), null, clone);
        var conversion = new CastOperator(clone, new Parameter("value", clone.CloneSource!), isImplicit, Public with { IsStatic = true })
        {
            DeclaringType = clone,
            ClonesThrough = new Method("Clone", clone, [], Public) { DeclaringType = clone },
            Body = new Block(new BlockNode { Span = new Storm.Runtime.SourceSpan(10, 50), Origin = Origin("source.mist") })
            {
                Statements = [new ReturnStatement(value, clone)],
            },
            Scope = new MethodScope(within),
        };
        within.Conversion = conversion;

        return conversion;
    }

    private static SyntaxNode Node()
    {
        return new TrueLiteralExpressionPrimaryNode { Span = new Storm.Runtime.SourceSpan(20, 1), Origin = Origin("source.mist") };
    }

    private static ParsedOrigin Origin(string path)
    {
        return new ParsedOrigin(new SourceText(path, new string(' ', 200)));
    }

    private sealed class First : IProgramAnalyzer
    {
        public string Id => "shared";

        public void Analyze(AnalysisContext context)
        {
        }
    }

    private sealed class Second : IProgramAnalyzer
    {
        public string Id => "shared";

        public void Analyze(AnalysisContext context)
        {
        }
    }
}