using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class CarrierVisibilityTests
{
    [Fact]
    public void InternalPayloadsLimitBothTheUnionCarrierAndItsResultSpecialization()
    {
        var shapes = new ResultModel();
        var hidden = HandBuiltModel.Class("Hidden");
        hidden.Modifiers = new() { Accessibility = Accessibility.Internal };
        var tuple = shapes.Built.Interner.GetOrCreateTupleType([shapes.Boolean, hidden]);
        var union = new UnionType([tuple, shapes.Integer]);
        var writer = new CSharpWriter();
        UnionCarrierEmitter.Emit("InternalCarrier", union, new Dictionary<string, UnionType>(), writer);
        Assert.Contains("internal readonly struct InternalCarrier", writer.ToString());
        Assert.Equal("internal", CarrierVisibility.Restrict("public", shapes.Result(hidden, shapes.Text)));
    }

    [Fact]
    public void PublicPayloadsKeepTheirPublicContract()
    {
        var shapes = new ResultModel();
        Assert.True(CarrierVisibility.IsPublic(shapes.Result(shapes.Integer, shapes.Text)));
        Assert.Equal("public", CarrierVisibility.Restrict("public", shapes.Result(shapes.Integer, shapes.Text)));
    }
}