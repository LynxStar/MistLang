// Tier 1 (Unit): an enum is spelled as every declared type is, through its namespace and the types it is
// nested in, because C# resolves a bare name against the scope it is written in and a nested enum named
// from another type is not in that scope. No sample names a nested enum from outside -- classic refuses
// the qualification -- so this is where it is held.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

public sealed class EnumTypeNameEmissionTests
{
    private static readonly MistAssembly Source = new("Probe", MistAssemblyKind.Self);

    [Fact]
    public void ATopLevelEnum_IsSpelledThroughItsNamespace()
    {
        Assert.Equal("Probe.Mode", TypeNameEmitter.Emit(Enum("Mode", declaring: null)));
    }

    [Fact]
    public void ANestedEnum_IsSpelledThroughItsDeclaringType()
    {
        Assert.Equal("Probe.Gauge.Mode", TypeNameEmitter.Emit(Enum("Mode", Class("Gauge", declaring: null))));
    }

    [Fact]
    public void AnEnumNestedTwoDeep_IsSpelledThroughEveryDeclaringType()
    {
        var inner = Class("Inner", Class("Outer", declaring: null));

        Assert.Equal("Probe.Outer.Inner.Mode", TypeNameEmitter.Emit(Enum("Mode", inner)));
    }

    private static ClassType Class(string name, MistType? declaring)
    {
        return new ClassType(name, "Probe", parentScope: null, declaring, declarationNode: null) { Assembly = Source };
    }

    private static EnumType Enum(string name, MistType? declaring)
    {
        return new EnumType(name, "Probe", parentScope: null, declaring, declarationNode: null) { Assembly = Source };
    }
}