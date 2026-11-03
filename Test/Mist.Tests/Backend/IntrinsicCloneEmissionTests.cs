using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
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
        type Tiny = new byte;
        type Offset = new sbyte;
        """;

    [Fact]
    public void AnIntrinsicClone_HoldsTheBackingValueAndPrintsIt()
    {
        var emitted = EmitDeclaration("Age");

        Assert.Contains("readonly struct Age", emitted);
        Assert.Contains("public readonly int Value;", emitted);
        Assert.Contains("public Age(int value)", emitted);
        Assert.Contains("public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);", emitted);
    }

    [Fact]
    public void AnIntrinsicMember_ForwardsToTheBackingValueAndWrapsTheNewtype()
    {
        var age = EmitDeclaration("Age");
        var label = EmitDeclaration("Label");

        Assert.Contains("=> new Probe.Age((a.Value + b.Value));", age);
        Assert.Contains("=> (a.Value < b.Value);", age);
        Assert.Contains("=> new Probe.Age(Value);", age);
        Assert.Contains("=> new Probe.Label(Value.ToUpperInvariant());", label);
        Assert.Contains("Length => Value.Length;", label);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ArithmeticOverANarrowIntegral_IsSpelledBackIntoTheSourceBeforeItIsWrapped()
    {
        var tiny = emit("Byte", "Tiny", OperatorKind.Plus);
        var compared = emit("Byte", "Tiny", OperatorKind.LessThan);
        var offset = emit("SByte", "Offset", OperatorKind.Minus);
        Assert.Contains("((byte)((a.Value + b.Value)))", tiny);
        Assert.Matches(@"new (?:Probe\.)?Tiny\(", tiny);
        Assert.Contains("=> (a.Value < b.Value);", compared);
        Assert.Contains("((sbyte)((-a.Value)))", offset);

        static string emit(string sourceName, string cloneName, OperatorKind kind)
        {
            var model = new HandBuiltModel();
            var source = model.Type(sourceName);
            source.Modifiers = new() { IsIntrinsic = true };
            var clone = new StructType(cloneName, "Probe", null, null, null) { CloneSource = source };
            var parameters = new List<Parameter> { new("a", clone) };
            if(kind != OperatorKind.Minus)
            {
                parameters.Add(new Parameter("b", clone));
            }

            var returns = kind == OperatorKind.LessThan ? model.Type("Boolean") : (IMistType)clone;
            var op = new Operator(kind, returns, parameters, new() { IsStatic = true, IsIntrinsic = true }) { DeclaringType = clone };
            var writer = new CSharpWriter();
            IntrinsicCloneEmitter.EmitOperator(op, writer, new DiagnosticBag());
            return writer.ToString();
        }
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
    public void AClassWrappersCopyOut_LeavesAnAbsentValueAbsent()
    {
        Assert.Contains("=> value is null ? null! : value.Value;", EmitDeclaration("Label"));
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