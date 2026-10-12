using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Resolves the base type list (inheritance and abstract implementations)
/// for any member-bearing type declaration. Shared by
/// <see cref="ConcreteTypeBuilder"/> and <see cref="AbstractTypeBuilder"/>.
/// </summary>
internal sealed class BaseTypeResolver
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;

    public BaseTypeResolver(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

    /// <summary>
    /// Resolves the base type list for any member-bearing type declaration.
    /// Extracts the <see cref="BaseListNode"/> from the declaration node.
    /// </summary>
    public void Resolve(TypeDeclarationNode typeDecl, MemberType typeDef)
    {
        var baseList = typeDecl switch
        {
            BodyTypeTypeDeclarationNode body => body.TypeBody.BaseList,
            _ => null
        };

        if(baseList is null)
        {
            return;
        }

        // Collect all entries: first + rest.
        var allEntries = new List<BaseTypeRefNode>(baseList.BaseTypeRefs.Length + 1) { baseList.First };
        foreach(var entry in baseList.BaseTypeRefs)
        {
            allEntries.Add(entry);
        }

        var startIndex = 0;

        // For concrete types, check if the first entry is a concrete base type.
        if(typeDef is ConcreteType concreteTypeDef)
        {
            var firstHandled = TryResolveConcreteBase(allEntries[0], concreteTypeDef);
            if(firstHandled)
            {
                startIndex = 1;
            }
        }

        // Remaining entries must all be abstracts.
        for(var i = startIndex; i < allEntries.Count; i++)
        {
            ResolveAbstractBase(allEntries[i], typeDef);
        }
    }

    /// <summary>
    /// Attempts to resolve the first base list entry as a concrete base type
    /// (class inheriting from class, struct inheriting from struct).
    /// Returns true if the entry was consumed as a concrete base, false if it
    /// should be treated as an abstract.
    /// </summary>
    private bool TryResolveConcreteBase(BaseTypeRefNode baseTypeRef, ConcreteType typeDef)
    {
        var resolved = ResolveBaseTypeRef(baseTypeRef, typeDef);
        if(resolved is ErrorType)
        {
            // Diagnostic already emitted by ResolveNamedType.
            typeDef.BaseUnresolved = true;
            return true;
        }

        // Unwrap ConstructedType to check the underlying definition kind.
        var definition = resolved is ConstructedType ct ? ct.Definition : resolved;

        // Not a concrete type — let ResolveAbstractBase handle it.
        if(definition is not ConcreteType resolvedConcrete)
        {
            return false;
        }

        // Verify kind match: class must inherit from class, struct from struct.
        var isSameKind = (typeDef, resolvedConcrete) switch
        {
            (ClassType, ClassType) => true,
            (StructType, StructType) => true,
            _ => false
        };

        if(!isSameKind)
        {
            var ownerKind = typeDef is ClassType ? "class" : "struct";
            var baseKind = resolvedConcrete is ClassType ? "class" : "struct";
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.BaseTypeKindMismatch, baseTypeRef, null, ownerKind, baseKind, QualifiedNameHelper.Build(baseTypeRef.Name));
            return true;
        }

        typeDef.BaseType = (MistType)resolved;
        return true;
    }

    /// <summary>
    /// Resolves a single base type entry that is expected to be an abstract.
    /// Emits a diagnostic if it resolves to a concrete type.
    /// </summary>
    private void ResolveAbstractBase(BaseTypeRefNode baseTypeRef, MemberType typeDef)
    {
        var resolved = ResolveBaseTypeRef(baseTypeRef, typeDef);
        if(resolved is ErrorType)
        {
            // Diagnostic already emitted by ResolveNamedType.
            return;
        }

        // Unwrap ConstructedType to check the underlying definition kind.
        var definition = resolved is ConstructedType ct ? ct.Definition : resolved;

        if(definition is ConcreteType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.ConcreteInAbstractList, baseTypeRef, null, QualifiedNameHelper.Build(baseTypeRef.Name));
            return;
        }

        if(definition is not AbstractType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.InvalidBaseType, baseTypeRef, null, QualifiedNameHelper.Build(baseTypeRef.Name));
            return;
        }

        typeDef.BaseAbstractTypes.Add((MistType)resolved);
    }

    /// <summary>
    /// Resolves a <see cref="BaseTypeRefNode"/> to its type, accounting for arity
    /// when type arguments are present. Returns a <see cref="ConstructedType"/> if
    /// type arguments exist, otherwise the raw definition.
    /// </summary>
    private IMistType ResolveBaseTypeRef(BaseTypeRefNode baseTypeRef, MemberType typeDef)
    {
        return baseTypeRef.Enclosing is { } enclosing
            ? scopeResolver.ResolveNestedGeneric(baseTypeRef.Name, enclosing.OuterArgs, enclosing.Nested, baseTypeRef.TypeArgs, typeDef.Scope, baseTypeRef)
            : scopeResolver.ResolveNamed(baseTypeRef.Name, baseTypeRef.TypeArgs, typeDef.Scope, baseTypeRef);
    }
}