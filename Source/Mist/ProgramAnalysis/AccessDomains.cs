using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a use sits inside a declaration's domain, from the full-accessibility-model spec: its declaring
/// type for private, that type or a derivation for protected, the same compilation for internal, with a
/// nested type counting as inside its enclosing one.
/// </summary>
internal static class AccessDomains
{
    public static bool Admits(AnalysisContext context, object use, Accessibility accessibility, MistType declaring)
    {
        var owner = context.OwnerOf(use);

        return accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Private => Encloses(declaring, owner),
            Accessibility.Protected => DerivesFrom(owner, declaring),
            _ => declaring.Assembly == context.AssemblyOf(use),
        };
    }

    public static string Where(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Private => "within the declaring type",
            Accessibility.Protected => "within the declaring type or a type derived from it",
            _ => "within the same compilation",
        };
    }

    public static MistType? Definition(IMistType? type)
    {
        return type switch
        {
            ConstructedType constructed => constructed.Definition as MistType,
            MistType mist => mist,
            _ => null,
        };
    }

    private static bool Encloses(MistType declaring, MistType? owner)
    {
        for(var type = owner; type is not null; type = type.DeclaringType)
        {
            if(ReferenceEquals(type, declaring))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(MistType? owner, MistType declaring)
    {
        for(var type = owner; type is not null; type = type.DeclaringType)
        {
            for(IMistType? current = type; current is not null; current = (Definition(current) as ConcreteType)?.BaseType)
            {
                if(ReferenceEquals(Definition(current), declaring))
                {
                    return true;
                }
            }
        }

        return false;
    }
}