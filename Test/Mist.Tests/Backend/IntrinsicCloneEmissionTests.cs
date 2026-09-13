// Tier 3 (Component): the wrapper a newtype over an intrinsic Standard type emits (newtypes spec Requirement
// 5.2), over a model built with the real Standard sources. Asserted on the emitted text, since a forwarding
// member that forgot to wrap its result would still compile wherever C# found a conversion back, and print
// the backing value either way.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class IntrinsicCloneEmissionTests
{
    private const string Newtypes = """
        type Age = new int;
        type Label = new string;
        type Flag = new bool;
        type Id<T> = new int;
        """;

    [Fact]
    public void AnIntrinsicClone_HoldsTheBackingValueAndPrintsIt()
    {
        var emitted = EmitDeclaration("Age");

        Assert.Contains("readonly struct Age", emitted);
        Assert.Contains("public readonly int Value;", emitted);
        Assert.Contains("public Age(int value)", emitted);
        Assert.Contains("public override string ToString() => Value.ToString();", emitted);
    }

    [Fact]
    public void AnIntrinsicMember_ForwardsToTheBackingValueAndWrapsTheNewtype()
    {
        var age = EmitDeclaration("Age");
        var label = EmitDeclaration("Label");

        Assert.Contains("=> new Probe.Age((a.Value + b.Value));", age);
        Assert.Contains("=> (a.Value < b.Value);", age);
        Assert.Contains("=> new Probe.Age(Value);", age);
        Assert.Contains("=> new Probe.Label(Value.ToUpper());", label);
        Assert.Contains("Length => Value.Length;", label);
    }

    [Fact]
    public void AGenericClone_WrapsAResultAsItselfOverItsOwnParameters()
    {
        var emitted = EmitDeclaration("Id<>");

        Assert.Contains("readonly struct Id<T>", emitted);
        Assert.Contains("=> new Probe.Id<T>((a.Value + b.Value));", emitted);
        Assert.Contains("explicit operator Probe.Id<T>(int value)", emitted);
    }

    [Fact]
    public void TheCopyConversions_UnwrapAndWrap()
    {
        var emitted = EmitDeclaration("Age");

        Assert.Contains("implicit operator int(Probe.Age value)", emitted);
        Assert.Contains("=> value.Value;", emitted);
        Assert.Contains("explicit operator Probe.Age(int value)", emitted);
        Assert.Contains("=> new Probe.Age(value);", emitted);
    }

    [Fact]
    public void ShortCircuitingOperators_EmitAsBitwiseBesideTrueAndFalse()
    {
        var emitted = EmitDeclaration("Flag");

        Assert.Contains("operator &(Probe.Flag a, Probe.Flag b)", emitted);
        Assert.Contains("operator |(Probe.Flag a, Probe.Flag b)", emitted);
        Assert.Contains("public static bool operator true(Probe.Flag value) => value.Value;", emitted);
        Assert.DoesNotContain("operator &&", emitted);
    }

    private static string EmitDeclaration(string typeName)
    {
        var model = StandardModelProbe.Build("", out var buildDiagnostics, Newtypes);
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