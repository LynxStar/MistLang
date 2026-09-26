using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
        return Admits(context.OwnerOf(use), context.AssemblyOf(use), accessibility, declaring);
    }

    public static bool Admits(MistType? owner, MistAssembly? assembly, Accessibility accessibility, MistType declaring)
    {
        return accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Private => Encloses(declaring, owner),
            Accessibility.Protected => DerivesFrom(owner, declaring),
            _ => declaring.Assembly == assembly,
        };
    }

    public static bool Reaches(AnalysisContext context, object use, IMistType type)
    {
        return Reaches(context.OwnerOf(use), context.AssemblyOf(use), type);
    }

    public static bool Reaches(MistType? owner, MistAssembly? assembly, IMistType type)
    {
        return type is DeclaredType declared
            ? Blocking(owner, assembly, declared) is null
            : Declarations.Parts(type).All(part => Reaches(owner, assembly, part));
    }

    /// <summary>
    /// The outermost type along <paramref name="type"/>'s nesting whose own accessibility shuts the site out,
    /// or null where every level admits it: a nested type's domain is its own intersected with its enclosing
    /// type's (the accessibility spec's Requirement 11.1), and the outermost is the one C# names. A top-level
    /// type has no enclosing type to be private or protected within, so anything but public is judged
    /// against the compilation, and the declaration itself is left to be judged as a declaration.
    /// </summary>
    public static MistType? Blocking(MistType? owner, MistAssembly? assembly, MistType type)
    {
        MistType? blocking = null;

        for(var current = type; current is not null; current = current.DeclaringType)
        {
            var accessibility = Declarations.ModifiersOf(current)?.Accessibility ?? Accessibility.Public;
            var admitted = current.DeclaringType is { } declaring
                ? Admits(owner, assembly, accessibility, declaring)
                : accessibility == Accessibility.Public || current.Assembly == assembly;

            if(!admitted)
            {
                blocking = current;
            }
        }

        return blocking;
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