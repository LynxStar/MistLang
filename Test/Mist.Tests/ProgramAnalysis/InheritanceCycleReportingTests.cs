using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class InheritanceCycleReportingTests
{
    [Fact]
    public void AnAmbientClosingTypeReportsTheCycleWithoutBorrowingTheBaseProjectLocation()
    {
        var built = new HandBuiltModel();
        var mistBase = InheritanceCycleDecisionTests.Type("class", "MistBase");
        mistBase.Assembly = built.Model.AddReferenceAssembly("Library", "lib/mist.toml");
        var owner = new ClassType("ClrDerived", "Bridge", null, null, null) { Assembly = MistAssembly.Ambient };
        var diagnostics = new DiagnosticBag();
        var context = built.Context(new InheritanceCycleAnalyzer(), diagnostics);

        InheritanceCycleAnalyzer.Report(context, [new InheritanceCycle(owner, mistBase, null)]);

        var finding = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0163", finding.Number.Value);
        Assert.Equal(new Absent(AbsenceReason.Unavailable), finding.Location);
        Assert.Equal<SlotValue>(
            [new TypeValue("<ambient>:Bridge.ClrDerived", "ClrDerived"), new TypeValue("lib/mist.toml:Probe.MistBase", "MistBase")],
            finding.Arguments);
        Assert.Equal("The base 'MistBase' of 'ClrDerived' forms an inheritance cycle.", finding.Message);
    }
}