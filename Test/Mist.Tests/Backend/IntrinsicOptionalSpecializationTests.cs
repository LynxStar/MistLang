using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class IntrinsicOptionalSpecializationTests
{
    [Fact]
    public void IntrinsicOptionalResultsKeepBackingStorageWhileTheirCallerSpecializes()
    {
        var built = new HandBuiltModel();
        var array = new ClassType("Array<,>", "Standard", null, null, null)
        { Modifiers = new MemberModifiers { IsIntrinsic = true } };
        var element = new TypeParameter("TValue", 0, new Scope(null), array);
        array.TypeParametersList.Add(element);
        array.TypeParametersList.Add(new TypeParameter("Length", 1, new Scope(null), array));
        _ = built.Model.RegisterType(array);
        _ = built.Interner.GetOrCreateUnionType([element, built.Type("Void")]);
        _ = built.Interner.GetOrCreateConstructedType(array, [built.Type("Int32"), built.Type("Int32")]);
        var caller = HandBuiltModel.Class("Caller");
        var parameter = new TypeParameter("T", 0, new Scope(null), caller);
        caller.TypeParametersList.Add(parameter);
        _ = built.Model.RegisterType(caller);
        _ = built.Interner.GetOrCreateUnionType([parameter, built.Type("Void")]);
        _ = built.Interner.GetOrCreateConstructedType(caller, [built.Type("Int32")]);
        var diagnostics = new DiagnosticBag();
        using var scope = UnionSpecialization.Enter(built.Model, diagnostics);

        Assert.False(UnionSpecialization.Specializes(array));
        Assert.True(UnionSpecialization.Specializes(caller));
        _ = Assert.Single(UnionSpecialization.InstancesOf(caller));
        Assert.Empty(diagnostics.ToImmutableArray());
    }
}