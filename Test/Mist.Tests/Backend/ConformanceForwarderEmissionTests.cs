using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 1: the explicit implementation ConformanceEmitter forwards a with block's mapping through, over a
// conformer and mapping constructed directly. It implements the interface member only when it repeats the
// slot's signature, which Roslyn alone would otherwise judge.
[Trait("Tier", "1")]
public sealed class ConformanceForwarderEmissionTests
{
    private static readonly MistAssembly Source = new("Probe", MistAssemblyKind.Self);

    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData("Void", ByRefDirection.None, false, "void Probe.IContract.Act(int x) => this.Poke(x);")]
    [InlineData("Int32", ByRefDirection.Ref, false, "int Probe.IContract.Act(ref int x) => this.Poke(ref x);")]
    [InlineData("Int32", ByRefDirection.In, false, "int Probe.IContract.Act(ref readonly int x) => this.Poke(in x);")]
    [InlineData("Int32", ByRefDirection.None, true, "ref int Probe.IContract.Act(int x) => ref this.Poke(x);")]
    public void AMappingIsForwardedWithTheSlotsReturnAndPassing(string returns, ByRefDirection passing, bool refReturn, string expected)
    {
        var integer = built.Type("Int32");
        var parameter = new Parameter("x", integer, new ParameterModifier(passing == ByRefDirection.In, passing != ByRefDirection.None, OwnershipKind.Unrestricted, passing));
        var modifiers = DeclarationFragments.Public with { IsRef = refReturn };
        var contract = new AbstractType("IContract", "Probe", parentScope: null, declaringType: null, declarationNode: null) { Assembly = Source };
        var slot = new Method("Act", built.Type(returns), [parameter], modifiers) { DeclaringType = contract };
        contract.MethodsList.Add(slot);
        var conformer = new ClassType("Thing", "Probe", parentScope: null, declaringType: null, declarationNode: null) { Assembly = Source };
        var source = new Method("Poke", built.Type(returns), [parameter], modifiers) { DeclaringType = conformer };
        conformer.MethodsList.Add(source);
        conformer.ConformancesList.Add(new ConformanceDeclaration(conformer, contract, "Probe", [new MethodMapping(source, slot, contract)], [], HandBuiltModel.Node()));

        var writer = new CSharpWriter();
        var emitted = false;
        ConformanceEmitter.Emit(conformer, writer, new DiagnosticBag(), ref emitted);

        Assert.Equal(expected, writer.ToString().Trim());
    }
}