using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A class of UnsafeAccessors over one generic definition's own parameters, and where it is written. The
/// runtime matches an accessor against its target by the position of each type parameter in the accessor's
/// DECLARING type, a nested type counting its enclosing types' parameters first: MEASURED on .NET 10, a
/// holder nested in a generic clone compiles and throws MissingFieldException for every member whose
/// signature mentions a parameter, at every instantiation, while the same holder outside every generic type
/// reaches a permuted, repeated or partly closed construction alike. So a clone that is a C# generic, or
/// sits inside one, hands its holders to the nearest type enclosing it that no generic encloses -- else to
/// the namespace, where `file` keeps them from widening anything -- and any other clone keeps them private
/// beside the conversion that calls them. A holder waits for the type it is bound for, since a sibling
/// nested beside the clone closes first and the clone cannot see into it.
/// </summary>
internal sealed record AccessorHolder(string Name, MemberType Definition, IReadOnlyList<string> Members)
{
    [ThreadStatic]
    private static List<(AccessorHolder Holder, MemberType? Home)>? pending;

    public static bool IsInsideAGeneric(MemberType type)
    {
        return Enclosing(type).Any(enclosing => TypeEmitter.Parameters(enclosing).Count > 0);
    }

    public static string NameFor(MemberType clone, string stem)
    {
        return IsInsideAGeneric(clone)
            ? $"__{Flat(clone)}_{string.Join("_", Enclosing(clone).Select(enclosing => enclosing.TypeParametersList.Count))}{stem}"
            : stem;
    }

    public void Place(MemberType clone, CSharpWriter writer)
    {
        if(!IsInsideAGeneric(clone))
        {
            writer.WriteBlankLine();
            Write("private", writer);
            return;
        }

        var home = Enclosing(clone).Skip(1).FirstOrDefault(enclosing => !IsInsideAGeneric(enclosing));

        (pending ??= []).Add((this, home));
    }

    public static void WritePending(MemberType? home, CSharpWriter writer)
    {
        if(pending is null)
        {
            return;
        }

        foreach(var (holder, _) in pending.FindAll(waiting => ReferenceEquals(waiting.Home, home)))
        {
            writer.WriteBlankLine();
            holder.Write(home is null ? "file" : "private", writer);
        }

        _ = pending.RemoveAll(waiting => ReferenceEquals(waiting.Home, home));
    }

    public static void Forget()
    {
        pending = null;
    }

    public static string Flat(IMistType type)
    {
        var stem = type.FullyQualifiedName.Where(character => character is not ('<' or '>' or ',' or ' '));

        return new string([.. stem.Select(character => char.IsLetterOrDigit(character) ? character : '_')]);
    }

    private static IEnumerable<MemberType> Enclosing(MemberType type)
    {
        for(MistType? enclosing = type; enclosing is not null; enclosing = enclosing.DeclaringType)
        {
            if(enclosing is MemberType member)
            {
                yield return member;
            }
        }
    }

    private void Write(string accessibility, CSharpWriter writer)
    {
        writer.WriteLine($"{accessibility} static class {Name}<{string.Join(", ", Definition.TypeParametersList.Select(TypeNameEmitter.Emit))}>");
        ConstraintEmitter.Emit(Definition.TypeParametersList, writer);

        using(writer.Block())
        {
            foreach(var member in Members)
            {
                writer.WriteLine(member);
            }
        }
    }
}