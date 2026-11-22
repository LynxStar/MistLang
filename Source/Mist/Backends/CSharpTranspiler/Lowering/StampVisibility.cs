using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Where a stamped default can be registered. The generated registrar sits at the top level, so it can
/// name only types whose every declaring link is public or internal; a type behind a private or
/// protected link is registered instead by a body that both mentions it and lies inside the declaring
/// type that hides it, because only there could the source have named it. A body whose enclosing type
/// the emitter does not track, an accessor's, is trusted on its mention alone for the same reason.
/// </summary>
internal static class StampVisibility
{
    public static bool Nameable(IMistType type)
    {
        return Hidden(type).Count == 0;
    }

    public static IReadOnlyList<ConcreteType> Hidden(IMistType type)
    {
        var found = new List<ConcreteType>();
        Collect(type, found, new HashSet<IMistType>(ReferenceEqualityComparer.Instance));
        return found;
    }

    public static HashSet<ConcreteType> Mentioned(object body)
    {
        var found = new List<ConcreteType>();
        var seen = new HashSet<IMistType>(ReferenceEqualityComparer.Instance);

        foreach(var expression in BodyWalker.DescendantsAndSelf(body).OfType<Expression>())
        {
            Collect(expression.Type, found, seen);

            var arguments = expression switch
            {
                MemberAccessExpression access => access.TypeArguments,
                MethodReferenceExpression reference => reference.TypeArguments,
                _ => [],
            };

            foreach(var argument in arguments)
            {
                Collect(argument, found, seen);
            }
        }

        return new HashSet<ConcreteType>(found, ReferenceEqualityComparer.Instance);
    }

    public static bool Reaches(IMistType? enclosing, ConcreteType hidden)
    {
        if(enclosing is null)
        {
            return true;
        }

        for(MistType? link = hidden; link is not null; link = link.DeclaringType)
        {
            if(Hides(link) && !Encloses(link.DeclaringType, enclosing))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Encloses(MistType? outer, IMistType? enclosing)
    {
        var inner = enclosing is ConstructedType { Definition: MistType definition } ? definition : enclosing as MistType;

        for(; inner is not null; inner = inner.DeclaringType)
        {
            if(ReferenceEquals(inner, outer))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Hides(MistType type)
    {
        return type is ConcreteType { Modifiers.Accessibility: Accessibility.Private or Accessibility.Protected };
    }

    private static void Collect(IMistType type, List<ConcreteType> found, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return;
        }

        switch(type)
        {
            case ConstructedType constructed:
                Collect(constructed.Definition, found, seen);
                constructed.TypeArguments.ToList().ForEach(argument => Collect(argument, found, seen));
                break;

            case ITupleType tuple:
                tuple.ElementTypes.ToList().ForEach(element => Collect(element, found, seen));
                break;

            case TypeAlias { AliasedType: { } aliased }:
                Collect(aliased, found, seen);
                break;

            case ConcreteType declaration:
                for(MistType? link = declaration; link is not null; link = link.DeclaringType)
                {
                    if(Hides(link))
                    {
                        found.Add(declaration);
                        break;
                    }
                }

                break;
        }
    }
}