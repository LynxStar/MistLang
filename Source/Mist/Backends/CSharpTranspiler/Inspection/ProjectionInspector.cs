using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Inspection;

internal static class ProjectionInspector
{
    public const string EventReason = "HIDDEN: event";

    public const string NotInheritedReason = "HIDDEN: not inherited in Mist";

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static InspectionReport Inspect(
        AmbientTypeProviderV2 provider, string typeName, Type clrType, DeclaredType projection)
    {
        if(projection.Assembly.Kind != MistAssemblyKind.Ambient)
        {
            return Aliased(typeName, projection);
        }

        var lineage = Lineage(projection);
        var visible = new List<VisibleMember>();
        var hidden = new List<HiddenMember>();

        foreach(var member in ClrMembers(clrType))
        {
            var declaringType = member.DeclaringType!;
            var from = declaringType == clrType ? null : ClrName(Open(declaringType));

            if(member is EventInfo)
            {
                hidden.Add(new HiddenMember(member.Name, EventReason, from));
            }
            else if(provider.ProjectionOf(declaringType) is not MemberType { Assembly.Kind: MistAssemblyKind.Ambient } declaring
                || !lineage.Contains(declaring))
            {
                hidden.Add(new HiddenMember(member.Name, NotInheritedReason, from));
            }
            else
            {
                Judge(provider, Definition(member), declaring, from, visible, hidden);
            }
        }

        return new InspectionReport(typeName, KindOf(projection), null, visible, hidden);
    }

    public static InspectionReport Aliased(string typeName, DeclaredType projection)
    {
        var visible = new List<VisibleMember>();

        if(projection is MemberType declared)
        {
            visible.AddRange(declared.PropertiesList.Where(property => Exposed(property.Modifiers)).Select(property => Shown(property, null)));
            visible.AddRange(declared.IndexersList.Where(indexer => Exposed(indexer.Modifiers)).Select(indexer => Shown(indexer, null)));
            visible.AddRange(declared.MethodsList.Where(method => Exposed(method.Modifiers)).Select(method => Shown(method, null)));
        }

        var note = $"alias of {MistType.StripArity(projection.FullyQualifiedName)}; its members are the Standard library's declarations";
        return new InspectionReport(typeName, KindOf(projection), note, visible, []);
    }

    public static string Describe(Withholding? withheld)
    {
        return withheld switch
        {
            { Reason: WithholdingReason.NonPublic } => "HIDDEN: non-public",
            { Reason: WithholdingReason.AccessorOrOperator } => "HIDDEN: accessor or operator",
            { Reason: WithholdingReason.Finalizer } => "HIDDEN: finalizer",
            { Reason: WithholdingReason.UnresolvableConstraint } => "HIDDEN: unresolvable generic constraint",
            { Reason: WithholdingReason.UnresolvableType, Offending: { } offending } => $"HIDDEN: signature uses {ClrName(offending)}",
            _ => "HIDDEN: unknown reason",
        };
    }

    public static string ClrName(Type type)
    {
        if(type.IsByRef)
        {
            return ClrName(type.GetElementType()!);
        }

        if(type.IsPointer)
        {
            return $"{ClrName(type.GetElementType()!)}*";
        }

        if(type.IsArray)
        {
            return $"{ClrName(type.GetElementType()!)}[]";
        }

        if(type.IsFunctionPointer)
        {
            return "delegate*";
        }

        if(type.IsGenericParameter)
        {
            return type.Name;
        }

        var arity = type.Name.IndexOf('`');
        var name = arity < 0 ? type.Name : type.Name[..arity];
        var qualifier = type.IsNested ? $"{ClrName(type.DeclaringType!)}." : type.Namespace is null ? "" : $"{type.Namespace}.";
        var arguments = type.IsGenericType && !type.IsGenericTypeDefinition
            ? $"<{string.Join(", ", type.GetGenericArguments().Select(ClrName))}>"
            : "";

        return qualifier + name + arguments;
    }

    private static void Judge(
        AmbientTypeProviderV2 provider,
        MemberInfo definition,
        MemberType declaring,
        string? from,
        List<VisibleMember> visible,
        List<HiddenMember> hidden)
    {
        Withholding? withheld;

        switch(definition)
        {
            case MethodInfo method:
                if(provider.Judge(method, declaring, out withheld) is { } projectedMethod)
                {
                    visible.Add(Shown(projectedMethod, from));
                    return;
                }

                break;

            case PropertyInfo indexer when indexer.GetIndexParameters().Length > 0:
                if(provider.JudgeIndexer(indexer, declaring, out withheld) is { } projectedIndexer)
                {
                    visible.Add(Shown(projectedIndexer, from));
                    return;
                }

                break;

            case PropertyInfo property:
                if(provider.Judge(property, declaring, out withheld) is { } projectedProperty)
                {
                    visible.Add(Shown(projectedProperty, from));
                    return;
                }

                break;

            default:
                return;
        }

        hidden.Add(new HiddenMember(definition.Name, Describe(withheld), from));
    }

    private static VisibleMember Shown(Method method, string? from)
    {
        var typeParameters = method.TypeParametersList.Count == 0
            ? ""
            : $"<{string.Join(", ", method.TypeParametersList.Select(parameter => parameter.Name))}>";
        var parameters = string.Join(", ", method.ParametersList.Select(Spelled));
        var signature = $"{typeParameters}({parameters}) -> {Result(method.Modifiers, method.ReturnType)}";

        return new VisibleMember(VisibleMemberKind.Method, method.Name, signature, IsProtected(method.Modifiers), from);
    }

    private static VisibleMember Shown(Property property, string? from)
    {
        return new VisibleMember(
            VisibleMemberKind.Property, property.Name, Result(property.Modifiers, property.Type), IsProtected(property.Modifiers), from);
    }

    private static VisibleMember Shown(Indexer indexer, string? from)
    {
        var parameters = string.Join(", ", indexer.ParametersList.Select(Spelled));
        var signature = $"[{parameters}]: {Result(indexer.Modifiers, indexer.Type)}";

        return new VisibleMember(VisibleMemberKind.Indexer, "this", signature, IsProtected(indexer.Modifiers), from);
    }

    internal static string Result(MemberModifiers modifiers, IMistType type)
    {
        var passing = modifiers switch
        {
            { ReturnsReadonlyRef: true } => "readonly ref ",
            { IsRef: true } => "ref ",
            _ => "",
        };

        return passing + TypeDisplay.Mist(type);
    }

    private static string Spelled(Parameter parameter)
    {
        var passing = parameter.Modifier.IsRef ? "ref " : "";
        return passing + TypeDisplay.Mist(parameter.Type);
    }

    private static bool IsProtected(MemberModifiers modifiers)
    {
        return modifiers.Accessibility == Accessibility.Protected;
    }

    private static bool Exposed(MemberModifiers modifiers)
    {
        return modifiers.Accessibility is Accessibility.Public or Accessibility.Protected;
    }

    private static string KindOf(DeclaredType projection)
    {
        return projection switch
        {
            EnumType => "enum",
            AbstractType => "interface",
            StructType => "struct",
            _ => "class",
        };
    }

    private static HashSet<MistType> Lineage(DeclaredType projection)
    {
        var lineage = new HashSet<MistType>(ReferenceEqualityComparer.Instance);
        IMistType? current = projection;

        while(current is MistType type && lineage.Add(type))
        {
            current = (type as ConcreteType)?.BaseType;

            if(current is ConstructedType constructed)
            {
                current = constructed.Definition;
            }
        }

        return lineage;
    }

    private static IEnumerable<MemberInfo> ClrMembers(Type clrType)
    {
        foreach(var property in clrType.GetProperties(Everything))
        {
            yield return property;
        }

        foreach(var method in clrType.GetMethods(Everything))
        {
            if(!method.IsSpecialName)
            {
                yield return method;
            }
        }

        foreach(var clrEvent in clrType.GetEvents(Everything))
        {
            yield return clrEvent;
        }
    }

    private static Type Open(Type type)
    {
        return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }

    private static MemberInfo Definition(MemberInfo member)
    {
        if(member.DeclaringType is not { IsGenericType: true, IsGenericTypeDefinition: false } constructed)
        {
            return member;
        }

        return constructed.GetGenericTypeDefinition()
            .GetMembers(Everything | BindingFlags.DeclaredOnly)
            .FirstOrDefault(candidate => candidate.MetadataToken == member.MetadataToken)
            ?? member;
    }
}