using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class UnionCarrierOpenTypeTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void AUnionCarrierIsOpenWhenItsFunctionArmContainsATypeParameter(
        bool openParameter, bool openResult, bool expected)
    {
        var model = new HandBuiltModel();
        var text = model.Type("String");
        var typeParameter = new TypeParameter("T", 0, new Scope(null), null);
        IMistType parameter = openParameter ? typeParameter : text;
        IMistType result = openResult ? typeParameter : text;
        var function = new FunctionType([parameter], result);
        var union = new UnionType([function, model.Type("Boolean")]);

        Assert.Equal(expected, CSharpEmitterV2.IsOpen(union));
    }
}