using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A type the ambient provider recognizes but cannot represent (closed generics, nested types,
/// etc.). Unlike <see cref="ErrorType"/> it is not a failure: no diagnostic is emitted, and
/// downstream code treats it as unavailable without cascading errors.
/// </summary>
internal sealed class NotProjectedType : MistType, IMistType
{
    public static readonly NotProjectedType Instance = new();

    private NotProjectedType()
        : base("<not-projected>", new Scope(null), declaringType: null)
    {
    }
}