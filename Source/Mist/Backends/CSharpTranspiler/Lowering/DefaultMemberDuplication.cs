using Mist.ProgramModel.Construction.Binding;
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
    public static List<RestatedDefault> For(ConcreteType type, IReadOnlyList<Method> declared)
    {
        var restated = new List<RestatedDefault>();

        foreach(var (abstractType, path) in Conformances(type))
        {
            foreach(var method in abstractType.MethodsList)
            {
                if(method.Body is null)
                {
                    continue;
                }

                var written = ValuePack.Expand(method.ParametersList, TypeSubstitution.ForPath(receiver: null, path, interner: null), interner: null);

                if(!Claims(declared, method.Name, written.Count)
                    && !Claims(restated.ConvertAll(entry => entry.Method), method.Name, method.ParametersList.Count))
                {
                    restated.Add(new RestatedDefault(method, path));
                }
            }
        }

        return restated;
    }

    private static bool Claims(IReadOnlyList<Method> methods, string name, int parameterCount)
    {
        foreach(var candidate in methods)
        {
            if(candidate.Name == name && candidate.ParametersList.Count == parameterCount)
            {
                return true;
            }
        }

        return false;
    }

    private static List<(AbstractType Type, IReadOnlyList<ConstructedType> Path)> Conformances(ConcreteType type)
    {
        var found = new List<(AbstractType, IReadOnlyList<ConstructedType>)>();
        var seen = new HashSet<AbstractType>();
        var queue = new Queue<(MistType Type, IReadOnlyList<ConstructedType> Path)>();

        foreach(var declared in type.BaseAbstractTypes)
        {
            queue.Enqueue((declared, []));
        }

        while(queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();
            var constructed = current as ConstructedType;
            var definition = constructed?.Definition ?? current;
            var reached = constructed is null ? path : [.. path, constructed];

            if(definition is not AbstractType abstractType || !seen.Add(abstractType))
            {
                continue;
            }

            found.Add((abstractType, reached));

            foreach(var inherited in abstractType.BaseAbstractTypes)
            {
                queue.Enqueue((inherited, reached));
            }
        }

        return found;
    }
}

/// <summary>
/// A default restated into a conformer, with the constructed abstracts it was reached through --
/// the substitution its signature and body are spelled under, since the abstract's own parameters
/// are not in scope in the conformer and its arguments are.
/// </summary>
internal sealed record RestatedDefault(Method Method, IReadOnlyList<ConstructedType> Path);