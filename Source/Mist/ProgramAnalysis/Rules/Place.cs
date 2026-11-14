using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Field state requires a tracked holder; shared field reads belong to <see cref="TierAnalyzer"/>.
/// Copied or immutable field values leave no ownership state behind when read (owned-fields Requirement 16).
/// </summary>
internal sealed class Place : IEquatable<Place>
{
    private Place(LocalVariable root, ImmutableArray<Field> path)
    {
        Root = root;
        Path = path;
    }

    public LocalVariable Root { get; }

    public ImmutableArray<Field> Path { get; }

    public bool IsRoot => Path.IsEmpty;

    public string Display => IsRoot ? Root.Name : $"{Root.Name}.{string.Join(".", Path.Select(member => member.Name))}";

    public static Place Of(LocalVariable local)
    {
        return new Place(local, []);
    }

    public static Place? Of(Expression expression)
    {
        return OwnershipAnalyzer.Resolved(expression) switch
        {
            ResultIdentityExpression identity => Of(identity.Operand),
            LocalReferenceExpression { Local: var local } => Of(local),
            MemberAccessExpression { Receiver: var receiver, Member: FieldResult { Field: var field } }
                when Holds(field) && Of(receiver) is { } holder && (Tracked(holder) || holder.Root.InfersOwnership) => holder.Child(field),
            _ => null,
        };
    }

    public static Place? Nearest(Expression expression)
    {
        return Of(expression) ?? (expression is MemberAccessExpression access ? Nearest(access.Receiver) : null);
    }

    public static bool Tracked(Place place)
    {
        return OwnershipAnalyzer.Tracked(place.Root);
    }

    public static bool Holds(Field field)
    {
        return ThisTiers.IsOwned(field) && !ThisTiers.IsValue(field.Type) && !Origins.IsUnchangeable(field.Type);
    }

    public Place Child(Field field)
    {
        return new Place(Root, Path.Add(field));
    }

    public bool IsAncestorOf(Place other)
    {
        return ReferenceEquals(Root, other.Root)
            && other.Path.Length > Path.Length
            && Path.SequenceEqual(other.Path.RemoveRange(Path.Length, other.Path.Length - Path.Length));
    }

    public IEnumerable<Place> AncestorsAndSelf()
    {
        for(var length = Path.Length; length >= 0; length--)
        {
            yield return length == Path.Length ? this : new Place(Root, Path.RemoveRange(length, Path.Length - length));
        }
    }

    public string RelativeTo(Place ancestor)
    {
        return string.Join(".", Path.Skip(ancestor.Path.Length).Select(field => field.Name));
    }

    public bool Equals(Place? other)
    {
        return other is not null && ReferenceEquals(Root, other.Root) && Path.SequenceEqual(other.Path);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Place);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RuntimeHelpers.GetHashCode(Root));

        foreach(var field in Path)
        {
            hash.Add(RuntimeHelpers.GetHashCode(field));
        }

        return hash.ToHashCode();
    }
}