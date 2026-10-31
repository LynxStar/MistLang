using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

/// <summary>
/// Projected classes whose members carry a slot kind, and source classes deriving them, built by hand.
/// </summary>
internal sealed class NativeSlotFragments
{
    private int offset = 10;

    public HandBuiltModel Built { get; } = new();

    public List<MistType> Types { get; } = [];

    public ClassType Int => Built.Type("Int32");

    public ClassType Text => Built.Type("String");

    public ClassType Void => Built.Type("Void");

    public static ClassType Projected(string name, MistType? baseType = null)
    {
        return new ClassType(name, "Clr", new Scope(null), null, null) { BaseType = baseType, Assembly = MistAssembly.Ambient };
    }

    public ClassType Source(string name, MistType? baseType)
    {
        var type = new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(Next()))
        {
            BaseType = baseType,
            Assembly = new MistAssembly("Probe", MistAssemblyKind.Self),
            Modifiers = DeclarationFragments.Public,
        };
        Types.Add(type);
        return type;
    }

    public static Method Slot(
        MemberType owner, string name, NativeSlotKind kind, IMistType returns, Accessibility access = Accessibility.Public,
        params IMistType[] parameters)
    {
        var method = new Method(name, returns, Parameters(parameters), Projected(access))
        {
            DeclaringType = owner,
            Scope = new Scope(owner.Scope),
            SlotKind = kind,
        };
        owner.MethodsList.Add(method);
        return method;
    }

    public Method Member(
        MemberType owner, string name, IMistType returns, Accessibility access = Accessibility.Public, bool isStatic = false,
        params IMistType[] parameters)
    {
        var method = new Method(name, returns, Parameters(parameters), new MemberModifiers { Accessibility = access, IsStatic = isStatic })
        {
            DeclarationNode = HandBuiltModel.Node(Next()),
            DeclaringType = owner,
            Scope = new Scope(owner.Scope),
        };
        owner.MethodsList.Add(method);
        return method;
    }

    public static Property SlotProperty(MemberType owner, string name, NativeSlotKind kind, IMistType type, bool settable)
    {
        var property = new Property(name, type, Projected(Accessibility.Public))
        {
            DeclaringType = owner,
            SlotKind = kind,
            Getter = new PropertyAccessor { Accessibility = Accessibility.Public },
            Setter = settable ? new PropertyAccessor { Accessibility = Accessibility.Public } : null,
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    public Property MemberProperty(MemberType owner, string name, IMistType type, bool settable, bool bodied = false)
    {
        var property = new Property(name, type, DeclarationFragments.Public)
        {
            DeclarationNode = HandBuiltModel.Node(Next()),
            DeclaringType = owner,
            Getter = new PropertyAccessor { Accessibility = Accessibility.Public, Body = bodied ? HandBuiltModel.Block() : null },
            Setter = settable ? new PropertyAccessor { Accessibility = Accessibility.Public } : null,
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    public IReadOnlyList<Diagnostic> Run(IProgramAnalyzer analyzer)
    {
        return Built.Analyze(analyzer, types: Types);
    }

    private static MemberModifiers Projected(Accessibility access)
    {
        return new MemberModifiers { Accessibility = access, IsClrProjected = true, ClrResultIsNonNull = true };
    }

    private static List<Parameter> Parameters(IMistType[] types)
    {
        return [.. types.Select((type, i) => new Parameter($"p{i}", type))];
    }

    private int Next()
    {
        offset += 10;
        return offset;
    }
}