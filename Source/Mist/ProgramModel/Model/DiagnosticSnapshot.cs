using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

internal static class DiagnosticSnapshot
{
    public static TypeValue Type(IMistType type)
    {
        return Type(type, TypeDisplay.Mist(type));
    }

    public static TypeValue Type(IMistType type, string spelling)
    {
        return new TypeValue(TypeIdentity(type), spelling);
    }

    public static SymbolValue Symbol(IOverload member, string spelling)
    {
        var owner = member.DeclaringType is { } type ? TypeIdentity(type) : "<local>";
        var name = member is Method method ? method.Name : member.GetType().Name;
        var parameters = string.Join(",", member.ParametersList.Select(parameter => TypeIdentity(parameter.Type)));
        return new SymbolValue($"{owner}.{name}`{member.TypeParametersList.Count}({parameters})", spelling);
    }

    private static string TypeIdentity(IMistType type)
    {
        if(type is TypeParameter { DeclaringMethod: { } method } parameter)
        {
            var owner = method.DeclaringType is { } declaring ? declaring.FullyQualifiedName : "<local>";
            var signature = string.Join(",", method.ParametersList.Select(item => item.Type.FullyQualifiedName));
            return $"{type.Assembly.Name}:{owner}.{method.Name}({signature}):{parameter.Name}";
        }

        return $"{type.Assembly.ProjectPath ?? type.Assembly.Name}:{type.FullyQualifiedName}";
    }
}