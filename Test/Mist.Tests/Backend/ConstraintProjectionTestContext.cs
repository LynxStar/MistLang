using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using ProgramModelType = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

internal sealed class ConstraintProjectionTestContext
{
    public ConstraintProjectionTestContext(params Type[] types)
    {
        Aliases = new Dictionary<string, IMistType>
        {
            ["System.Int32"] = new StructType("Int32", "Standard", null, null, null),
            ["System.Void"] = new StructType("Void", "Standard", null, null, null),
            ["System.Object"] = new ClassType("Object", "Standard", null, null, null),
            ["System.Array"] = new ClassType("Array<,>", "Standard", null, null, null),
        };
        var comparable = new AbstractType("Comparable<>", "Standard", null, null, null);
        Assert.True(TypeParameterProjector.Initialize(comparable, typeof(IComparable<>)));
        Aliases["System.IComparable`1"] = comparable;

        foreach(var type in types)
        {
            Add(type);
        }

        Resolver = new AmbientTypeResolver(Shells, Aliases, Model.TypeInterner, Diagnostics);
    }

    public ProgramModelType Model { get; } = new();

    public DiagnosticBag Diagnostics { get; } = new();

    public Dictionary<string, DeclaredType> Shells { get; } = [];

    public Dictionary<string, Type> ClrTypes { get; } = [];

    public Dictionary<string, IMistType> Aliases { get; }

    public AmbientTypeResolver Resolver { get; }

    public void Project()
    {
        AmbientTypeProviderV2.ProjectTypes(Shells, ClrTypes, Resolver, Aliases);
    }

    public MemberType Type(Type clrType)
    {
        return (MemberType)Shells[AmbientTypeResolver.BuildFQN(clrType)];
    }

    private void Add(Type clrType)
    {
        var name = AmbientTypeResolver.BuildFQN(clrType);
        if(Shells.ContainsKey(name))
        {
            return;
        }

        DeclaredType? parent = null;
        if(clrType.DeclaringType is { } declaring)
        {
            Add(declaring);
            parent = Shells[AmbientTypeResolver.BuildFQN(declaring)];
        }

        var shell = TypeProjector.CreateShell(clrType, clrType.Namespace ?? "", parent, parent?.Scope);
        Shells[name] = shell;
        ClrTypes[name] = clrType;
        parent?.NestedTypes.Add(shell);
    }
}