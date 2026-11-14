using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Classes, structs and abstracts.
/// </summary>
public interface IMemberType : IMistType, IGenericDeclaration
{
    MemberModifiers Modifiers { get; }

    IReadOnlyList<IMistType> BaseAbstractTypes { get; }

    IReadOnlyList<IMethod> Methods { get; }

    IReadOnlyList<IProperty> Properties { get; }

    IReadOnlyList<IIndexer> Indexers { get; }

    IReadOnlyList<IOperator> Operators { get; }
}