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
        type Swapped<X, Y> = new Pair<Y, X>;
        type Pair = new Pair<int, string>;

        public class Shelf
        {
            private type Own<K> = new Pair<K, int>;

            public class Beside
            {
            }
        }

        public class Rack<U>
        {
            public type Own = new Pair<U, int>;
        }
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

    [Fact]
    public void AGenericClone_DeclaresItsSourcesHolderOutsideItself()
    {
        var emitted = EmitDeclaration("Swapped<,>");
        var clone = emitted[..emitted.IndexOf("file static class", StringComparison.Ordinal)];

        Assert.Contains("__Probe_Swapped_first(ref copy) = __Probe_Swapped_2__Probe_Pair_Access<Y, X>.__first(ref value);", clone);
        Assert.Contains("private static extern ref Y __Probe_Swapped_first(ref Probe.Swapped<X, Y> target);", clone);
        Assert.DoesNotContain("static class", clone);
        Assert.Contains("file static class __Probe_Swapped_2__Probe_Pair_Access<A, B>", emitted);
        Assert.Contains("public static extern ref A __first(ref Probe.Pair<A, B> target);", emitted);
    }

    [Fact]
    public void AGenericCloneInsideAPlainClass_LeavesItsHolderToThatClassAndNoSiblingOfItself()
    {
        var emitted = EmitDeclaration("Shelf");
        var holder = emitted.IndexOf("private static class __Probe_Shelf_Own_1_0__Probe_Pair_Access<A, B>", StringComparison.Ordinal);
        var beside = emitted.IndexOf("public class Beside", StringComparison.Ordinal);

        Assert.True(beside > emitted.IndexOf("class Own<K>", StringComparison.Ordinal));
        Assert.True(holder > beside);
        Assert.Equal(emitted[beside..holder].Count(character => character == '{'), emitted[beside..holder].Count(character => character == '}'));
        Assert.DoesNotContain("file static class", emitted);
    }

    [Fact]
    public void ACloneInsideAGenericClass_HandsItsSourcesHolderToTheNamespace()
    {
        var emitted = EmitDeclaration("Rack<>");

        Assert.Contains("private static extern ref U __Probe_Rack_Own_first(ref Probe.Rack<U>.Own target);", emitted);
        Assert.Contains("__Probe_Rack_Own_0_1__Probe_Pair_Access<U, int>.__first(ref value);", emitted);
        Assert.Contains("file static class __Probe_Rack_Own_0_1__Probe_Pair_Access<A, B>", emitted);
    }

    [Fact]
    public void ACloneNamedLikeItsGenericSource_StillDeclaresTheSourcesHolder()
    {
        var emitted = EmitDeclaration("Pair");

        Assert.Contains("private static extern ref int __Probe_Pair_first(ref Probe.Pair target);", emitted);
        Assert.Contains("private static class __Probe_Pair_Access<A, B>", emitted);
    }

    [Fact]
    public void AClassClonesCopy_LeavesAnAbsentValueAbsent()
    {
        var emitted = EmitDeclaration("Cash");

        Assert.Contains("if(value is null)", emitted);
        Assert.Contains("return null!;", emitted);
        Assert.DoesNotContain("is null", EmitDeclaration("Position"));
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