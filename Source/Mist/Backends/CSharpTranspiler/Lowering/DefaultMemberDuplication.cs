using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The defaults a conformer has to restate as its own members, so a default is callable on a
/// concrete receiver and not only through an abstract-typed one.
/// <para>
/// RESTATED RATHER THAN FORWARDED, which is forced rather than chosen. A stub reading
/// <c>((IShape)this).Summary()</c> would not terminate: the public method it sits in IS the implicit
/// implementation of <c>IShape.Summary</c>, so the cast dispatches straight back into it. Nothing
/// else in C# reaches an interface's default body from outside the interface, so the body is emitted
/// twice -- which is also what Mist means, a default being monomorphized into each conformer the way
/// Rust does it rather than inherited.
/// </para>
/// <para>
/// SUPPRESSED ONLY BY THE CONFORMER'S OWN MEMBERS, matched on name and parameter count. A member
/// reached through a concrete BASE does not suppress it, so the restatement hides that base member.
/// That reads wrong and is deliberate: it is what the classic pipeline does, and it is what Mist
/// means, since conformance is the conformer's own claim and the default is the abstract's answer
/// for anyone who does not answer for themselves. <c>declared</c> is passed in rather than read off
/// the type for the same reason a duplicate member would otherwise reach C#: a struct emits a
/// FLATTENED set, so only what is actually being emitted can decide what is already answered.
/// </para>
/// <para>
/// The walk is seeded from the type's OWN base list and proceeds breadth-first, so where two
/// abstracts default one name the nearest wins and exactly one member is emitted -- which is also
/// what keeps C# from seeing the name declared twice.
/// </para>
/// </summary>
internal static class DefaultMemberDuplication
{
    public static List<Method> For(ConcreteType type, IReadOnlyList<Method> declared)
    {
        var restated = new List<Method>();

        foreach(var abstractType in Conformances(type))
        {
            foreach(var method in abstractType.MethodsList)
            {
                if(method.Body is null || NamesATypeParameter(method))
                {
                    continue;
                }

                if(!Claims(declared, method) && !Claims(restated, method))
                {
                    restated.Add(method);
                }
            }
        }

        return restated;
    }

    /// <summary>
    /// A signature mentioning one of its abstract's type parameters is skipped, because restating it
    /// verbatim would put that parameter where the conformer has nothing to bind it to. Closing it
    /// needs the type arguments applied to the BODY as well, which nothing here can do -- so the
    /// member is left out rather than emitted as C# that names an unresolvable type.
    /// </summary>
    private static bool NamesATypeParameter(Method method)
    {
        if(method.ReturnType is TypeParameter)
        {
            return true;
        }

        foreach(var parameter in method.ParametersList)
        {
            if(parameter.Type is TypeParameter)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Claims(IReadOnlyList<Method> methods, Method method)
    {
        foreach(var candidate in methods)
        {
            if(candidate.Name == method.Name
                && candidate.ParametersList.Count == method.ParametersList.Count)
            {
                return true;
            }
        }

        return false;
    }

    private static List<AbstractType> Conformances(ConcreteType type)
    {
        var found = new List<AbstractType>();
        var seen = new HashSet<AbstractType>();
        var queue = new Queue<MistType>();

        foreach(var declared in type.BaseAbstractTypes)
        {
            queue.Enqueue(declared);
        }

        while(queue.Count > 0)
        {
            var current = queue.Dequeue();
            var definition = current is ConstructedType constructed ? constructed.Definition : current;

            if(definition is not AbstractType abstractType || !seen.Add(abstractType))
            {
                continue;
            }

            found.Add(abstractType);

            foreach(var inherited in abstractType.BaseAbstractTypes)
            {
                queue.Enqueue(inherited);
            }
        }

        return found;
    }
}