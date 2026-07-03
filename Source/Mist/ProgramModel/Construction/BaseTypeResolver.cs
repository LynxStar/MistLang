using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves the base type list (inheritance and abstract implementations)
/// for any member-bearing type declaration. Shared by
/// <see cref="ConcreteTypeBuilder"/> and <see cref="AbstractTypeBuilder"/>.
/// </summary>
internal sealed class BaseTypeResolver
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;

    public BaseTypeResolver(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// Resolves the base type list for any member-bearing type declaration.
    /// Extracts the <see cref="BaseListNode"/> from the declaration node.
    /// </summary>
    public void Resolve(TypeDeclarationNode typeDecl, MemberTypeDefinition typeDef)
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
        if(typeDef is ConcreteTypeDefinition concreteTypeDef)
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
    private bool TryResolveConcreteBase(BaseTypeRefNode baseTypeRef, ConcreteTypeDefinition typeDef)
    {
        var name = baseTypeRef.Name.Lexeme;

        var resolved = typeResolver.ResolveNamedType(name, typeDef.Scope, baseTypeRef);
        if(resolved is null)
        {
            // Diagnostic already emitted by ResolveNamedType.
            return true;
        }

        // Not a concrete type — let ResolveAbstractBase handle it.
        if(resolved is not ConcreteTypeDefinition resolvedConcrete)
        {
            return false;
        }

        // Verify kind match: class must inherit from class, struct from struct.
        var isSameKind = (typeDef, resolvedConcrete) switch
        {
            (ClassDefinition, ClassDefinition) => true,
            (StructDefinition, StructDefinition) => true,
            _ => false
        };

        if(!isSameKind)
        {
            var ownerKind = typeDef is ClassDefinition ? "class" : "struct";
            var baseKind = resolvedConcrete is ClassDefinition ? "class" : "struct";
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.BaseTypeKindMismatch, baseTypeRef, null, ownerKind, baseKind, name);
            return true;
        }

        typeDef.BaseType = resolvedConcrete;
        return true;
    }

    /// <summary>
    /// Resolves a single base type entry that is expected to be an abstract.
    /// Emits a diagnostic if it resolves to a concrete type.
    /// </summary>
    private void ResolveAbstractBase(BaseTypeRefNode baseTypeRef, MemberTypeDefinition typeDef)
    {
        var name = baseTypeRef.Name.Lexeme;

        var resolved = typeResolver.ResolveNamedType(name, typeDef.Scope, baseTypeRef);
        if(resolved is null)
        {
            // Diagnostic already emitted by ResolveNamedType.
            return;
        }

        if(resolved is ConcreteTypeDefinition)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.ConcreteInAbstractList, baseTypeRef, null, name);
            return;
        }

        if(resolved is not AbstractType abstractDef)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.InvalidBaseType, baseTypeRef, null, name);
            return;
        }

        typeDef.BaseAbstractTypes.Add(abstractDef);
    }
}