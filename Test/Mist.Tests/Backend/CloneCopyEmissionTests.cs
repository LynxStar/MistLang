// Tier 3 (Component): what a newtype's copy conversions emit. Asserted on the emitted text, since a copy
// that skipped a private field or an inherited one would still compile and print whatever the fields it did
// copy hold.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class CloneCopyEmissionTests : ProgramModelProbe
{
    public CloneCopyEmissionTests()
        : base("MistCloneCopyEmission_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Point
        {
            private int hidden;
            public int X { get; set; }

            public Point(int x)
            {
                this.X = x;
                this.hidden = x;
            }
        }

        public class Ledger
        {
            private int serial;

            public Ledger(int serial)
            {
                this.serial = serial;
            }
        }

        public readonly class Money : Ledger
        {
            private string currency;

            public Money(string currency) : base(1)
            {
                this.currency = currency;
            }
        }

        public struct Pair<A, B>
        {
            private A first;
            public B Second { get; set; }

            public Pair(A first, B second)
            {
                this.first = first;
                this.Second = second;
            }
        }

        type Position = new Point;
        type Cash = new Money;
        type IntPair = new Pair<int, string>;
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void AStructClone_CopiesEveryFieldBothWaysThroughAccessors()
    {
        var emitted = EmitDeclaration("Position");

        Assert.Contains("public static implicit operator Probe.Point(Probe.Position value)", emitted);
        Assert.Contains("public static explicit operator Probe.Position(Probe.Point value)", emitted);
        Assert.Contains("__Probe_Point_hidden(ref copy) = __Probe_Position_hidden(ref value);", emitted);
        Assert.Contains("__Probe_Position_X_BackingField(ref copy) = __Probe_Point_X_BackingField(ref value);", emitted);
        Assert.Contains("Name = \"<X>k__BackingField\")]", emitted);
    }

    [Fact]
    public void AReadonlyClassClone_AllocatesWithoutAConstructorAndCopiesItsBasesFields()
    {
        var emitted = EmitDeclaration("Cash");

        Assert.Contains("GetUninitializedObject(typeof(Probe.Cash))", emitted);
        Assert.Contains("__Probe_Ledger_serial(copy) = __Probe_Ledger_serial(value);", emitted);
        Assert.Contains("__Probe_Cash_currency(copy) = __Probe_Money_currency(value);", emitted);
    }

    [Fact]
    public void AConstructedSource_IsReachedThroughAHolderOverItsDefinitionsParameters()
    {
        var emitted = EmitDeclaration("IntPair");

        Assert.Contains("__Probe_Pair_Access<int, string>.__first(ref copy) = __Probe_IntPair_first(ref value);", emitted);
        Assert.Contains("__Probe_IntPair_Second_BackingField(ref copy) = __Probe_Pair_Access<int, string>.__Second_BackingField(ref value);", emitted);
        Assert.Contains("private static class __Probe_Pair_Access<A, B>", emitted);
        Assert.Contains("public static extern ref A __first(ref Probe.Pair<A, B> target);", emitted);
    }

    private string EmitDeclaration(string typeName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(
            type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);

        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}