using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class TypeInspectorDiagnosticDrainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectionDeliversEverySeverityBeforeReturningItsErrorGate(bool includeError)
    {
        var bag = new DiagnosticBag();
        foreach(var severity in new[] { DiagnosticSeverity.Info, DiagnosticSeverity.Warning, DiagnosticSeverity.Error })
        {
            if(severity != DiagnosticSeverity.Error || includeError)
            {
                bag.Add(DiagnosticFixtures.Occurrence("PROBE", severity, PipelineStage.Driver, "detail", new Absent(AbsenceReason.TestOrigin), null));
            }
        }

        var received = new List<Diagnostic>();
        var failed = TypeInspector.Drain(bag.Admitted, received.Add);

        Assert.Equal(includeError, failed);
        Assert.Equal(bag.Admitted, received);
    }
}