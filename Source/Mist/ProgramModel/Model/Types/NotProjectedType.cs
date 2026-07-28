using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Sentinel type representing a type that exists but was deliberately not projected.
/// Used for known limitations (closed generics, nested types, etc.) where the ambient
/// provider recognizes the type but cannot yet represent it. Unlike <see cref="ErrorType"/>,
/// this does not indicate a failure — no diagnostic is emitted. Downstream code should
/// treat this as "not available" without cascading errors.
/// </summary>
internal sealed class NotProjectedType : MistType, IMistType
{
    public static readonly NotProjectedType Instance = new();

    private NotProjectedType()
        : base("<not-projected>", "", new Scope(null), declaringType: null)
    {
    }
}