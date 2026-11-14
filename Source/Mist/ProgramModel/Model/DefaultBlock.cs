using Mist.Generated;

namespace Mist.ProgramModel.Model;

internal sealed class DefaultBlock
{
    public IReadOnlyList<DefaultEntry> Entries { get; }

    public DefaultBlockDeclarationNode DeclarationNode { get; }

    public DefaultBlock(IReadOnlyList<DefaultEntry> entries, DefaultBlockDeclarationNode declarationNode)
    {
        Entries = entries;
        DeclarationNode = declarationNode;
    }
}