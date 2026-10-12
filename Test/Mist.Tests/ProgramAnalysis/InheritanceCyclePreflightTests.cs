using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "3")]
public sealed class InheritanceCyclePreflightTests
{
    [Fact]
    public void ACyclicModelIsRefusedBeforeNormalAnalysisScopesAndHierarchyConsumers()
    {
        var first = Assert.IsType<ClassType>(InheritanceCycleDecisionTests.Type("class", "First"));
        var second = Assert.IsType<ClassType>(InheritanceCycleDecisionTests.Type("class", "Second"));
        first.BaseType = second;
        second.BaseType = first;
        var model = ModelOf(first, second);
        var diagnostics = new DiagnosticBag();
        var timings = new AnalysisTimings();

        ProgramAnalysisRunner.Run(model, diagnostics, timings);

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PA_CYCLIC_INHERITANCE", diagnostic.Id.Value);
        Assert.Equal("inheritance-cycle-checks", diagnostic.AnalyzerName);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(31, diagnostic.Location.Start.Column);
        Assert.Equal("The base 'First' of 'Second' forms an inheritance cycle.", diagnostic.Message);
        Assert.Equal("InheritanceCycleAnalyzer", Assert.Single(timings.Analyzers).Name);
        Assert.Equal(0, timings.Scopes);
        Assert.Same(second, first.BaseType);
        Assert.Same(first, second.BaseType);
    }

    [Fact]
    public void AnAcyclicModelStillAggregatesAnalysisWithExistingBindingRefusals()
    {
        var parent = Assert.IsType<ClassType>(InheritanceCycleDecisionTests.Type("class", "Parent"));
        parent.Modifiers = new MemberModifiers { IsSealed = true };
        var derived = Assert.IsType<ClassType>(InheritanceCycleDecisionTests.Type("class", "Derived"));
        derived.BaseType = parent;
        var model = ModelOf(parent, derived);
        var diagnostics = new DiagnosticBag();
        var binding = new Diagnostic(new DiagnosticId("MIST_PM_PROBE"), DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction, "existing binding refusal", default, "hand-built.mist", null);
        diagnostics.Add(binding);
        var timings = new AnalysisTimings();

        ProgramAnalysisRunner.Run(model, diagnostics, timings);

        Assert.Contains(binding, diagnostics.ToImmutableArray());
        Assert.Contains(diagnostics.ToImmutableArray(), diagnostic => diagnostic.Id.Value == "MIST_PA_SEALED_BASE_TYPE");
        Assert.DoesNotContain(diagnostics.ToImmutableArray(), diagnostic => diagnostic.Id.Value == "MIST_PA_CYCLIC_INHERITANCE");
        Assert.Equal(1, timings.ProgramScopes);
        Assert.Contains(timings.Analyzers, analyzer => analyzer.Name == "ConformanceAnalyzer");
    }

    private static Model ModelOf(params DeclaredType[] types)
    {
        var model = new Model();
        var assembly = model.AddSelfAssembly("Cycles", isLibrary: true);
        var unit = new CompilationUnit { SourcePath = "hand-built.mist" };
        foreach(var type in types)
        {
            type.Assembly = assembly;
            unit.Types.Add(type);
            _ = model.RegisterType(type);
        }

        assembly.CompilationUnits.Add(unit);
        return model;
    }
}