using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// Phase 2 builder for newtypes. Resolves modifiers and the source type reference
/// from the <c>new T</c> annotation on the declaration.
/// </summary>
internal sealed class NewtypeBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;

    public NewtypeBuilder(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

    /// <summary>
    /// Resolves the newtype's modifiers and source type reference.
    /// </summary>
    public void Build(Newtype typeDef)
    {
        var decl = typeDef.DeclarationNode;
        var modifiers = ModifierBuilder.Build(decl, context.Diagnostics, typeDef.DeclaringType);

        typeDef.Modifiers = modifiers;
        typeDef.SourceType = scopeResolver.Resolve(decl.SourceType, typeDef.Scope);

        if(typeDef.SourceType is ConcreteType { IsIntrinsic: true } intrinsicSource)
        {
            typeDef.ValueField = new Field(
                "Value",
                intrinsicSource,
                OwnershipKind.Unrestricted,
                new MemberModifiers { Accessibility = Accessibility.Public, IsReadonly = true });
        }
    }
}