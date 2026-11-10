using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

internal sealed class TypeSlotBuilder
{
    private readonly BuilderContext context;
    private readonly ConstraintResolver constraintResolver;

    public TypeSlotBuilder(BuilderContext context, ConstraintResolver constraintResolver)
    {
        this.context = context;
        this.constraintResolver = constraintResolver;
    }

    public void Resolve(AssociatedTypeSlotDeclarationNode slotDecl, AbstractType typeDef)
    {
        var name = slotDecl.Name.Lexeme;

        if(!slotDecl.Modifiers.IsDefaultOrEmpty)
        {
            context.Diagnostics.ReportAtNodeTyped(CommonDiagnostics.ModifiersOnAssociatedTypeSlot, slotDecl, null);
        }

        // Compared without the arity marker, so two slots differing only in arity still collide;
        // whether a slot may be overloaded on arity like an ordinary generic is an open language
        // question.
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

        TypeParameterPopulator.Populate(
            slot, slotScope, slot, slotDecl.TypeParams, context.Diagnostics, context.Domains);

        if(slotDecl.ConstraintClause is { } bound)
        {
            constraintResolver.ResolveConstraintList(bound, slot.ConstraintsList, slotScope);
        }

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