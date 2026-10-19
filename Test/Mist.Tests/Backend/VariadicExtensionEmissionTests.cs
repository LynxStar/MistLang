using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class VariadicExtensionEmissionTests
{
    [Fact]
    public void CarrierEmitsOnlyRecordedMethodWidths()
    {
        var block = new ExtensionBlock("self", "Probe", new Scope(null), null)
        {
            CarrierName = "HolderExtensions",
            ResolvedReceiverType = new ClassType("Holder", "Probe", null, null, null),
        };
        var pack = new TypeParameter("Us", 0, block.Scope, null) { PackKind = TypePackKind.Unbracketed };
        var integer = new ClassType("Int32", "Standard", null, null, null);
        var method = new Method("Count", integer,
            [new Parameter("us", new TupleType([new SpreadType(pack)])) { IsFlattened = true }], default)
        {
            DeclaringBlock = block,
            Body = new Block(null!)
            {
                Statements = [new ReturnStatement(
                    new MemberAccessExpression(null!, new TypeReferenceExpression(null!, pack), "Length", integer), integer)],
            },
        };
        method.TypeParametersList.Add(pack);
        block.MethodsList.Add(method);
        var uses = new VariadicUseRegistry();
        _ = uses.Record(method, new int[] { 0 });
        _ = uses.Record(method, new int[] { 1 });
        _ = uses.Record(method, new int[] { 3 });
        _ = uses.Record(method, new int[] { 3 });
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        ExtensionBlockEmitter.Emit(block, writer, diagnostics, ref anyEmitted, uses);

        var text = writer.ToString();
        Assert.Contains("Count_0(this Holder self, System.ValueTuple us)", text);
        Assert.Contains("Count_1<T1>(this Holder self, System.ValueTuple<T1> us)", text);
        Assert.Contains("Count_3<T1, T2, T3>(this Holder self, (T1, T2, T3) us)", text);
        Assert.Contains("return 3;", text);
        Assert.Equal(3, text.Split("public static int Count_").Length - 1);
        Assert.DoesNotContain("Us", text);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedGenericBlockUsesSpecializeTheSourceDeclaration(bool forwarded)
    {
        var built = new HandBuiltModel();
        var block = new ExtensionBlock("self", "Probe", new Scope(null), null)
        {
            CarrierName = "HolderExtensions",
        };
        var item = new TypeParameter("T", 0, block.Scope, null);
        block.TypeParametersList.Add(item);
        var holder = HandBuiltModel.Class("Holder<>");
        holder.TypeParametersList.Add(item);
        block.ResolvedReceiverType = built.Interner.GetOrCreateConstructedType(holder, [item]);
        var pack = new TypeParameter("Us", 0, block.Scope, null) { PackKind = TypePackKind.Unbracketed };
        var integer = built.Type("Int32");
        var method = new Method("Count", integer,
            [new Parameter("us", new TupleType([new SpreadType(pack)])) { IsFlattened = true }], default)
        {
            DeclaringBlock = block,
            Body = HandBuiltModel.Block(new ReturnStatement(
                new MemberAccessExpression(null!, new TypeReferenceExpression(null!, pack), "Length", integer), integer)),
        };
        method.TypeParametersList.Add(pack);
        block.MethodsList.Add(method);
        var closed = new Method(method.Name, method.ReturnType, method.ParametersList, method.Modifiers)
        {
            SourceDeclaration = method,
            ClosedBlock = block,
            DeclaringBlock = block,
            BlockClosing = TypeSubstitution.ForParameters(block.TypeParametersList, [integer]),
        };
        closed.TypeParametersList.Add(pack);
        if(forwarded)
        {
            var forwarder = new Method("Forward", integer, [], default);
            var sourcePack = new TypeParameter("Ts", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
            forwarder.TypeParametersList.Add(sourcePack);
            _ = built.Model.VariadicUses.Record(forwarder, new int[] { 1 });
            _ = built.Model.VariadicUses.Record(forwarder, new int[] { 3 });
            built.Model.GenericCalls.Record(closed,
                [built.Interner.LiftPack(built.Type("Type"), new TypePackValue([new SpreadType(sourcePack)]))]);
        }
        else
        {
            built.Model.VariadicUses.Record(closed, [built.Interner.LiftPack(built.Type("Type"), new TypePackValue([integer]))]);
            built.Model.VariadicUses.Record(closed, [built.Interner.LiftPack(built.Type("Type"), new TypePackValue([integer, integer, integer]))]);
        }

        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;
        ExtensionBlockEmitter.Emit(block, writer, diagnostics, ref anyEmitted, MonomorphSet.Discover(built.Model).Methods);

        var text = writer.ToString();
        Assert.Contains("Count_1<T, T1>(this Probe.Holder<T> self, System.ValueTuple<T1> us)", text);
        Assert.Contains("Count_3<T, T1, T2, T3>(this Probe.Holder<T> self, (T1, T2, T3) us)", text);
        Assert.Contains("return 1;", text);
        Assert.Contains("return 3;", text);
        Assert.Equal(2, text.Split("public static int Count_").Length - 1);
        Assert.Empty(diagnostics.ToImmutableArray());
    }
}