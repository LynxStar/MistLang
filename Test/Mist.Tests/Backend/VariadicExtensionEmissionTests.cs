using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

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
}