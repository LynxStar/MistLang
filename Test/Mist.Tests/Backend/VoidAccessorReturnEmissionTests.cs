using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class VoidAccessorReturnEmissionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly HandBuiltModel built = new();
    private readonly StructType unit = new("Void", "Standard", null, null, null);
    private readonly StructType integer = new("Int32", "Standard", null, null, null);
    private readonly ClassType holder = Class("Holder");
    private readonly Method sideEffect;

    public VoidAccessorReturnEmissionTests()
    {
        holder.Assembly = built.Model.AddSelfAssembly("Probe", isLibrary: true);
        unit.Assembly = built.Model.AddStandardAssembly();
        sideEffect = new Method("Log", unit, [], Public with { IsStatic = true }) { DeclaringType = holder };
        holder.MethodsList.Add(sideEffect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AVoidGetterReturningTheUnitLiteral_ReturnsTheUnitValue(bool isStatic)
    {
        var property = VoidProperty(isStatic, GetterBody(new ReturnStatement(Unit(), unit)));

        Assert.Contains("return Standard.Void.Instance;", EmitProperty(property));
    }

    [Fact]
    public void AVoidGetterFallingOffItsEnd_StillReturnsTheUnitValue()
    {
        var property = VoidProperty(isStatic: true, GetterBody());

        Assert.Contains("return Standard.Void.Instance;", EmitProperty(property));
    }

    [Fact]
    public void AVoidGetterReturningASideEffectingCall_RunsTheCallOnceThenReturnsTheUnitValue()
    {
        var property = VoidProperty(isStatic: true, GetterBody(new ReturnStatement(CallSideEffect(), unit)));

        Assert.Contains("Probe.Holder.Log();\nreturn Standard.Void.Instance;", EmitProperty(property));
    }

    [Fact]
    public void AVoidGetterReturningAVoidPropertyRead_FlowsTheReadThroughRatherThanDroppingIt()
    {
        var property = VoidProperty(isStatic: true, GetterBody(new ReturnStatement(ReadAmbient(), unit)));

        var emitted = EmitProperty(property);

        Assert.Contains("return Probe.Holder.Ambient;", emitted);
        Assert.DoesNotContain("Standard.Void.Instance", emitted);
    }

    [Fact]
    public void AVoidGetterReturningAVoidIndexerRead_FlowsTheReadThroughRatherThanDroppingIt()
    {
        var property = VoidProperty(isStatic: false, GetterBody(new ReturnStatement(ReadIndexer(), unit)));

        var emitted = EmitProperty(property);

        Assert.Contains("return this[0];", emitted);
        Assert.DoesNotContain("Standard.Void.Instance", emitted);
    }

    [Fact]
    public void AVoidSetter_KeepsABareReturn()
    {
        var setter = new PropertyAccessor { Body = Block(new ReturnStatement(null, null)) };

        var emitted = Emit(writer => AccessorEmitter.Emit(setter, isSetter: true, isReadonly: false, writer, new DiagnosticBag()));

        Assert.Contains("return;", emitted);
        Assert.DoesNotContain("Standard.Void.Instance", emitted);
    }

    [Fact]
    public void AnOrdinaryValueGetter_ReturnsItsValueRatherThanTheUnit()
    {
        var property = new Property("Count", integer, Public with { IsStatic = true })
        {
            DeclaringType = holder,
            Getter = GetterBody(new ReturnStatement(Literal(integer, 7L), integer)),
        };

        var emitted = EmitProperty(property);

        Assert.Contains("return 7;", emitted);
        Assert.DoesNotContain("Standard.Void.Instance", emitted);
    }

    [Fact]
    public void ANativeVoidMethod_KeepsABareReturn()
    {
        var method = new Method("DoWork", unit, [], Public with { IsStatic = true })
        {
            DeclaringType = holder,
            Body = Block(new ReturnStatement(Unit(), unit)),
        };

        var emitted = Emit(writer => MethodEmitter.Emit(method, emitReadonly: false, writer, new DiagnosticBag(), holder));

        Assert.Contains("void DoWork()", emitted);
        Assert.Contains("return;", emitted);
        Assert.DoesNotContain("Standard.Void.Instance", emitted);
    }

    private Property VoidProperty(bool isStatic, PropertyAccessor getter)
    {
        return new Property("Current", unit, Public with { IsStatic = isStatic })
        {
            DeclaringType = holder,
            Getter = getter,
        };
    }

    private static PropertyAccessor GetterBody(params Statement[] statements)
    {
        return new PropertyAccessor { Body = Block(statements) };
    }

    private VoidLiteralExpression Unit()
    {
        return new VoidLiteralExpression(Node(), VoidReading.Singleton, unit);
    }

    private MemberAccessExpression ReadAmbient()
    {
        return new MemberAccessExpression(Node(), new TypeReferenceExpression(Node(), holder), "Ambient", unit);
    }

    private IndexExpression ReadIndexer()
    {
        return new IndexExpression(Node(), new ThisExpression(Node(), holder), Literal(integer, 0L), unit);
    }

    private InvocationExpression CallSideEffect()
    {
        return new InvocationExpression(
            Node(),
            new MemberAccessExpression(Node(), new TypeReferenceExpression(Node(), holder), sideEffect.Name, unit),
            [],
            unit,
            sideEffect,
            []);
    }

    private static string EmitProperty(Property property)
    {
        return Emit(writer => PropertyEmitter.Emit(property, emitReadonly: false, readonlyGetter: false, writer, new DiagnosticBag()));
    }

    private static string Emit(Action<CSharpWriter> emit)
    {
        var writer = new CSharpWriter();
        BodyEmitter.ResetTemporaries();
        emit(writer);
        return string.Join("\n", writer.ToString().ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()));
    }
}