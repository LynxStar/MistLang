using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ProjectedExtensionCallTypeArgumentTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void ProjectedCallsRestoreTheClrOrderOfReceiverAndMethodTypeArguments(int receiverOrdinal, int resultOrdinal)
    {
        var built = new HandBuiltModel();
        var block = new ExtensionBlock("self", "System.Linq", new Scope(null), null);
        var receiver = new TypeParameter("Source", receiverOrdinal, block.Scope, null);
        block.TypeParametersList.Add(receiver);
        var method = new Method("Select", built.Type("Int32"), [], new MemberModifiers { IsClrProjected = true })
        {
            ClosedBlock = block,
            BlockClosing = TypeSubstitution.ForParameters([receiver], [built.Type("String")]),
        };
        var result = new TypeParameter("Result", resultOrdinal, block.Scope, null);
        method.TypeParametersList.Add(result);

        var (arguments, parameters) = ExtensionBlockEmitter.CallTypeArguments(method, [built.Type("Int32")]);

        Assert.Same(built.Type("String"), arguments[receiverOrdinal]);
        Assert.Same(built.Type("Int32"), arguments[resultOrdinal]);
        Assert.Same(receiver, parameters![receiverOrdinal]);
        Assert.Same(result, parameters[resultOrdinal]);
    }
}