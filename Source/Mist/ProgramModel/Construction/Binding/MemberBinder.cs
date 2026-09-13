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
/// Order is irrelevant, within a type and across them, with one exception. Phase 2 resolved every
/// signature already, so no body, chain, initializer or accessor depends on which member was bound
/// first -- but an enum's base may be inferred from its variants' values, and a body converting to or
/// from that enum reads the base, so enums bind before everything else.
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
        foreach(var enumType in sourceTypes.OfType<EnumType>())
        {
            EnumMemberBinder.BindAll(enumType, context);
        }

        foreach(var memberType in sourceTypes.OfType<MemberType>())
        {
            BindDelegates(memberType, context);
            BindInitializers(memberType, context);
            BindAccessors(memberType, context);
        }
    }

    /// <summary>
    /// Extension block bodies, which the walks above structurally cannot reach: they range over
    /// TYPES, and a block is not one. Without this a body binds to nothing at all -- a member could
    /// name an identifier that does not exist and the file still compiled clean.
    /// <para>
    /// Source blocks only. An ambient block is projected from assembly metadata and has no body to
    /// bind, which is exactly what a null declaration node means.
    /// </para>
    /// </summary>
    public static void BindExtensions(IEnumerable<ExtensionBlock> blocks, BuilderContext context)
    {
        foreach(var block in blocks)
        {
            if(block.DeclarationNode is null)
            {
                continue;
            }

            foreach(var method in block.MethodsList)
            {
                if(method.Body is not null)
                {
                    BodyBinder.BindDelegate(method, context);
                }
            }

            foreach(var property in block.PropertiesList)
            {
                BodyBinder.BindAccessors(
                    property.Getter, property.Setter, property.Type, [],
                    property.Modifiers.IsStatic, property, block.Scope, context);
            }
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

        foreach(var entry in concreteType.DefaultBlock.Entries)
        {
            switch(entry)
            {
                case DefaultValueEntry value:
                    BindEntryValue(value, concreteType, concreteType, context);
                    break;

                case DefaultBindingEntry { BoundType: ConcreteType bound } binding:
                    foreach(var nested in binding.NestedEntries.OfType<DefaultValueEntry>())
                    {
                        BindEntryValue(nested, bound, concreteType, context);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// The field is looked up on the type that DECLARES it -- the bound type for a nested override --
    /// while the scope stays the enclosing type's, which is where the expression was written.
    /// </summary>
    private static void BindEntryValue(
        DefaultValueEntry entry, ConcreteType declaring, ConcreteType enclosing, BuilderContext context)
    {
        if(declaring.FieldsList.Find(f => f.Name == entry.FieldName) is { } field)
        {
            entry.Value = BodyBinder.BindInitializer(
                entry.Value, field.Type, enclosing.Scope, context)!;
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