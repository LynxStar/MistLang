using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

public interface IHasAttributes
{
    IReadOnlyList<AttributeData> Attributes { get; }
}