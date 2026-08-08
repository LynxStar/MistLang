using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Phase 3, which produces the resolved AST-free bound tree.
/// <para>
/// Three walks rather than one, because <see cref="MemberType.EnumerateDelegates"/> reaches only
/// what is shaped like a method. An initializer is an expression rather than a body, and an
/// accessor is not a <see cref="Model.Members.Method"/> at all, so each needs finding separately.
/// </para>
/// <para>
/// Order is irrelevant, within a type and across them. Phase 2 resolved every signature already,
/// so no body, chain, initializer or accessor depends on which member was bound first.
/// </para>
/// </summary>
internal static class MemberBinder
{
    /// <summary>
    /// Safe to run over any source program even where the binder is unfinished: a construct it does
    /// not yet handle binds to a sentinel node rather than throwing or reporting.
    /// </summary>
    public static void BindAll(IEnumerable<IMistType> sourceTypes, BuilderContext context)
    {
        foreach(var memberType in sourceTypes.OfType<MemberType>())
        {
            BindDelegates(memberType, context);
            BindInitializers(memberType, context);
            BindAccessors(memberType, context);
        }
    }

    private static void BindDelegates(MemberType memberType, BuilderContext context)
    {
        foreach(var boundDelegate in memberType.EnumerateDelegates())
        {
            // Abstract members and intrinsics legitimately have no body to bind.
            if(boundDelegate.Body is not null)
            {
                BodyBinder.BindDelegate(boundDelegate, context);
            }
        }
    }

    private static void BindInitializers(MemberType memberType, BuilderContext context)
    {
        if(memberType is ConcreteType concreteType)
        {
            foreach(var field in concreteType.FieldsList)
            {
                field.Initializer = BodyBinder.BindInitializer(
                    field.Initializer, field.Type, memberType.Scope, context);
            }

            BindDefaultBlock(concreteType, context);
        }

        foreach(var property in memberType.PropertiesList)
        {
            property.Initializer = BodyBinder.BindInitializer(
                property.Initializer, property.Type, memberType.Scope, context);
        }
    }

    /// <summary>
    /// Binds a default block's overrides against the type of the field each names. An entry naming no
    /// declared field is left unbound rather than reported: whether a default block may name something
    /// the type does not declare is a rule about a finished model, and nothing here refuses one.
    /// </summary>
    private static void BindDefaultBlock(ConcreteType concreteType, BuilderContext context)
    {
        if(concreteType.DefaultBlock is null)
        {
            return;
        }

        foreach(var entry in concreteType.DefaultBlock.Entries.OfType<DefaultValueEntry>())
        {
            var field = concreteType.FieldsList.Find(f => f.Name == entry.FieldName);

            if(field is not null)
            {
                entry.Value = BodyBinder.BindInitializer(
                    entry.Value, field.Type, concreteType.Scope, context)!;
            }
        }
    }

    private static void BindAccessors(MemberType memberType, BuilderContext context)
    {
        foreach(var property in memberType.PropertiesList)
        {
            BodyBinder.BindAccessors(
                property.Getter, property.Setter, property.Type, [],
                property.Modifiers.IsStatic, property, memberType.Scope, context);
        }

        foreach(var indexer in memberType.IndexersList)
        {
            BodyBinder.BindAccessors(
                indexer.Getter, indexer.Setter, indexer.Type, indexer.ParametersList,
                indexer.Modifiers.IsStatic, owningProperty: null, memberType.Scope, context);
        }
    }
}