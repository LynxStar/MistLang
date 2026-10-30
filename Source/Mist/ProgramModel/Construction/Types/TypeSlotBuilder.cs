using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
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
            context.Diagnostics.ReportAtNodeTyped(CommonDiagnostics.ModifiersOnAssociatedTypeSlot, slotDecl, null);
        }

        // Slot names must be unique within their declaring abstract type. Compared without the
        // arity marker, so two slots differing only in arity still collide -- whether a slot may
        // be overloaded on arity the way an ordinary generic is, is a language question nobody
        // has asked.
        if(typeDef.TypeSlotsList.Any(existing => MistType.StripArity(existing.Name) == name))
        {
            context.Diagnostics.ReportAtNodeTyped(CommonDiagnostics.DuplicateAssociatedTypeSlot, slotDecl, null, new NameValue(typeDef.Name), new NameValue(name));
            return;
        }

        // A slot gets its own scope (child of the abstract's scope) so its own
        // type parameters are visible while resolving its bound and where clauses.
        var slotScope = new MemberScope(typeDef.Scope);

        // Registered under the ARITY-BEARING name, because a reference carrying type arguments is
        // mangled to `Converted<>` by ScopeResolver.BuildArityName before it is looked up, and
        // TypeMemberLookup matches a slot by string equality against that key. A slot is the only
        // generic-capable declaration in the model named like a type PARAMETER, so it is the only
        // one where the two conventions can disagree; sharing the reference side's formatter is
        // what keeps them from doing so.
        var slot = new TypeSlot(
            ArityName(name, slotDecl.TypeParams), typeDef.TypeSlotsList.Count, slotScope, typeDef);

        // The slot's own type parameters (e.g. T in `type Foo<T>;`).
        TypeParameterPopulator.Populate(
            slot, slotScope, slot, slotDecl.TypeParams, context.Diagnostics, context.Domains);

        // The `:` bound constrains the slot itself; store it on the slot's own
        // constraint list (inherited from TypeParameter).
        if(slotDecl.ConstraintClause is { } bound)
        {
            constraintResolver.ResolveConstraintList(bound, slot.ConstraintsList, slotScope);
        }

        // The `where` clauses constrain the slot's own type parameters.
        constraintResolver.Resolve(slotDecl.Group0?.SlotWhereConstraints ?? default, slot.TypeParametersList, slotScope);

        // Adding it to the abstract is also what makes it nameable by simple name in the
        // abstract's members: TypeScope reads TypeSlotsList rather than keeping its own table.
        typeDef.TypeSlotsList.Add(slot);
    }

    private static string ArityName(string name, TypeParameterListNode? typeParams)
    {
        if(typeParams is null)
        {
            return name;
        }

        return ScopeResolver.BuildArityName(name, 1 + typeParams.TypeParameters.Length);
    }
}