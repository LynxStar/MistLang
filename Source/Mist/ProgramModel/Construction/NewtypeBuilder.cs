using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for newtypes. Resolves modifiers and the source type reference
/// from the <c>new T</c> annotation on the declaration.
/// </summary>
internal sealed class NewtypeBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;

    public NewtypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// Resolves the newtype's modifiers and source type reference.
    /// </summary>
    public void Build(Newtype typeDef)
    {
        var decl = typeDef.DeclarationNode;
        var modifiers = ModifierBuilder.Build(decl, context.Diagnostics, typeDef.DeclaringType);

        typeDef.Modifiers = modifiers;
        typeDef.SourceType = typeResolver.Resolve(decl.SourceType, typeDef.Scope);
    }
}