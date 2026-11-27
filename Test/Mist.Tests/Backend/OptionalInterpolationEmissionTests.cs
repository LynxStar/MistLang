using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class OptionalInterpolationEmissionTests
{
    private readonly Scope scope = new(null);

    [Fact]
    public void OptionalDecimal_EmitsVoidForAbsenceAndUsesInvariantFormatting()
    {
        var number = new StructType("Decimal", "Standard", scope, null, null);

        Assert.Equal("System.FormattableString.Invariant($\"{(object?)value ?? \"Void\"}\")", EmitOptional(number));
    }

    [Fact]
    public void OptionalString_EmitsVoidForAbsenceWithoutSelectingAProvider()
    {
        var text = new ClassType("String", "Standard", scope, null, null);

        Assert.Equal("$\"{(object?)value ?? \"Void\"}\"", EmitOptional(text));
    }

    private string EmitOptional(IMistType present)
    {
        var text = new ClassType("String", "Standard", scope, null, null);
        var absence = new StructType("Void", "Standard", scope, null, null);
        var optional = new UnionType([present, absence]);
        var value = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", optional));
        var interpolated = new InterpolatedStringExpression(HandBuiltModel.Node(), [new InterpolatedStringHole(value)], text);

        return InterpolationEmitter.Emit(interpolated, _ => "value");
    }
}