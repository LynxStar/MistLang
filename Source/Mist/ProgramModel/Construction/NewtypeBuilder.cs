using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for newtypes. Resolves the source type reference
/// from the <c>new T</c> annotation on the declaration.
/// </summary>
internal sealed class NewtypeBuilder
{
    private readonly TypeReferenceResolver typeResolver;

    public NewtypeBuilder(TypeReferenceResolver typeResolver)
    {
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// Resolves the newtype's source type reference.
    /// </summary>
    public void Build(Newtype typeDef)
    {
        var decl = typeDef.DeclarationNode;
        typeDef.SourceType = typeResolver.Resolve(decl.SourceType, typeDef.Scope);
    }
}