using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// Resolves associated type slot declarations into <see cref="TypeSlot"/>
/// instances. A slot is a type parameter closed by conformance rather than by a
/// type argument. This builder fully resolves the slot: its own type parameters,
/// its constraint bound (the <c>:</c> clause, which constrains the slot itself),
/// and its <c>where</c> clauses (which constrain the slot's type parameters). The
/// slot is registered as a type in the abstract's scope so members can reference
/// it by simple name.
/// </summary>
internal sealed class TypeSlotBuilder
{
    private readonly BuilderContext context;
    private readonly ConstraintResolver constraintResolver;

    public TypeSlotBuilder(BuilderContext context, ConstraintResolver constraintResolver)
    {
        this.context = context;
        this.constraintResolver = constraintResolver;
    }

    /// <summary>
    /// Resolves an associated type slot declaration, validating slot-level rules
    /// and registering the fully-built slot on the abstract type and in its scope.
    /// </summary>
    public void Resolve(AssociatedTypeSlotDeclarationNode slotDecl, AbstractType typeDef)
    {
        var name = slotDecl.Name.Lexeme;

        // Modifiers are not permitted on associated type slots.
        if(!slotDecl.Modifiers.IsDefaultOrEmpty)
        {
            context.Diagnostics.ReportAtNode(CommonDiagnostics.ModifiersOnAssociatedTypeSlot, slotDecl, null);
        }

        // Slot names must be unique within their declaring abstract type.
        if(typeDef.TypeSlotsList.Any(existing => existing.Name == name))
        {
            context.Diagnostics.ReportAtNode(CommonDiagnostics.DuplicateAssociatedTypeSlot, slotDecl, null, typeDef.Name, name);
            return;
        }

        // A slot gets its own scope (child of the abstract's scope) so its own
        // type parameters are visible while resolving its bound and where clauses.
        var slotScope = new Scope(typeDef.Scope)
        {
            Namespace = typeDef.NamespaceName
        };

        var slot = new TypeSlot(name, typeDef.TypeSlotsList.Count, slotScope, typeDef);

        // The slot's own type parameters (e.g. T in `type Foo<T>;`).
        TypeParameterPopulator.Populate(slot.TypeParametersList, slotScope, slot, slotDecl.TypeParams);

        // The `:` bound constrains the slot itself; store it on the slot's own
        // constraint list (inherited from TypeParameter).
        if(slotDecl.ConstraintClause is { } bound)
        {
            constraintResolver.ResolveConstraintList(bound, slot.ConstraintsList, slotScope);
        }

        // The `where` clauses constrain the slot's own type parameters.
        constraintResolver.Resolve(slotDecl.Group0?.SlotWhereConstraints ?? default, slot.TypeParametersList, slotScope);

        typeDef.TypeSlotsList.Add(slot);
        typeDef.Scope.AddLocalType(name, slot);
    }
}