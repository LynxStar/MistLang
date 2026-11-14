using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// An unresolved or invalid type, standing in for null so downstream code proceeds without
/// cascading diagnostics.
/// </summary>
internal sealed class ErrorType : MistType, IMistType
{
    public static readonly ErrorType Instance = new();

    private ErrorType()
        : base("<error>", new Scope(null), declaringType: null)
    {
    }
}