using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// The fact-bearing holders a write may reach besides the one it names (flow-sensitive refinement inference
/// Requirement 3.6). A by-ref parameter's storage is the caller's choice: any field or static, an array element, or a
/// member of an object the body can reach, so every write to such storage of its type may change it, and a write
/// through it may change any of them. Storage of one type cannot be another type's, so the type limits the reach.
/// </summary>
internal sealed record WriteReach(IMistType? Type, IReadOnlySet<Field> Fields, bool AnyField, bool ByRefParameters)
{
    public static readonly WriteReach Nothing = new(null, new HashSet<Field>(), false, false);

    public static WriteReach Storage(IMistType type, bool anyField = false)
    {
        return new(type, new HashSet<Field>(), anyField, true);
    }

    public static WriteReach Field(Field field)
    {
        return new(field.Type, new HashSet<Field>([field]), false, true);
    }

    public bool Covers(object holder)
    {
        return holder switch
        {
            Field field => Names(field),
            MemberHolder { Member: Field field } => Names(field),
            LocalVariable { Parameter.Modifier.IsRef: true } parameter => ByRefParameters && MayShare(Type, parameter.Type),
            _ => false,
        };
    }

    public WriteReach Or(WriteReach other)
    {
        if(ReferenceEquals(this, Nothing))
        {
            return other;
        }

        if(ReferenceEquals(other, Nothing))
        {
            return this;
        }

        var type = Type is not null && other.Type is not null && !MayShare(Type, other.Type) ? null : Type ?? other.Type;
        return new(type, new HashSet<Field>(Fields.Concat(other.Fields), ReferenceEqualityComparer.Instance), AnyField || other.AnyField, ByRefParameters || other.ByRefParameters);
    }

    public static bool MayShare(IMistType? first, IMistType? second)
    {
        if(first is null || second is null)
        {
            return true;
        }

        var (one, two) = (Requirement.Unrefined(first), Requirement.Unrefined(second));
        return !Closed(one) || !Closed(two) || one.FullyQualifiedName == two.FullyQualifiedName;
    }

    private bool Names(Field field)
    {
        return Fields.Contains(field) || (AnyField && MayShare(Type, field.Type));
    }

    private static bool Closed(IMistType type)
    {
        return Requirement.Unrefined(type) switch
        {
            ClassType or StructType or EnumType => true,
            ConstructedType constructed => constructed.TypeArguments.All(Closed),
            _ => false,
        };
    }
}