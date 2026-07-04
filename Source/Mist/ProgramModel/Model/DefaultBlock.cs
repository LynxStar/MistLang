using Mist.Generated;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A default block declared on a concrete type. Contains a list of entries
/// that define explicit default values for the type's fields.
/// </summary>
internal sealed class DefaultBlock
{
    /// <summary>
    /// The entries in the default block.
    /// </summary>
    public IReadOnlyList<DefaultEntry> Entries { get; }

    /// <summary>
    /// The AST node for later expression evaluation during default value computation.
    /// </summary>
    public DefaultBlockDeclarationNode DeclarationNode { get; }

    public DefaultBlock(IReadOnlyList<DefaultEntry> entries, DefaultBlockDeclarationNode declarationNode)
    {
        Entries = entries;
        DeclarationNode = declarationNode;
    }
}