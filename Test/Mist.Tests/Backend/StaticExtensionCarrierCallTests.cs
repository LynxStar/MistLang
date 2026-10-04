using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class StaticExtensionCarrierCallTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StaticCallsUseTheCarrierWithoutPassingAReceiver(bool promoted, bool generic)
    {
        var built = new HandBuiltModel();
        var result = built.Type("Int32");
        var holder = HandBuiltModel.Class("Holder");
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode())
        { ResolvedReceiverType = holder, CarrierName = "HolderExtensions" };
        var source = new Method("Read", result, [new Parameter("value", result)], new MemberModifiers { IsStatic = true })
        { DeclaringBlock = block };
        var method = source;

        if(generic)
        {
            var receiverParameter = new TypeParameter("T", 0, block.Scope, null);
            block.TypeParametersList.Add(receiverParameter);
            source.TypeParametersList.Add(new TypeParameter("U", 1, block.Scope, null));
            method = new Method("Read", result, source.ParametersList, source.Modifiers)
            {
                SourceDeclaration = source,
                DeclaringBlock = block,
                ClosedBlock = block,
                BlockClosing = TypeSubstitution.ForParameters([receiverParameter], [result]),
            };
            method.TypeParametersList.AddRange(source.TypeParametersList);
        }

        if(promoted)
        {
            built.Model.TaskDrops.RecordDrop(source);
        }

        var receiver = new TypeReferenceExpression(HandBuiltModel.Node(), holder);
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Read", result,
            generic ? [built.Type("String")] : []);
        var call = new InvocationExpression(HandBuiltModel.Node(), access, [HandBuiltModel.Literal(result, 42)], result, method, [result]);
        using var promotion = AsyncPromotion.Enter(built.Model);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitExpression(call, writer, diagnostics);

        Assert.Equal($"{(promoted ? "await " : "")}Probe.HolderExtensions.Read{(generic ? "<int, string>" : "")}(42)", writer.ToString());
        Assert.Empty(diagnostics.ToImmutableArray());
    }
}