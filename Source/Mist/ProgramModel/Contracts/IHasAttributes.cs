using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A declaration that can carry applied attributes.
/// </summary>
public interface IHasAttributes
{
    /// <summary>The attributes applied to this declaration.</summary>
    IReadOnlyList<AttributeData> Attributes { get; }
}