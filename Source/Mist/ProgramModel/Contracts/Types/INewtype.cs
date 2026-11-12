namespace Mist.ProgramModel.Contracts.Types;

public interface INewtype : IMistType, IGenericDeclaration
{
    IMistType? SourceType { get; }
}