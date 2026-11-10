using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

internal sealed class BaseTypeResolver
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;

    public BaseTypeResolver(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

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

        var allEntries = new List<BaseTypeRefNode>(baseList.BaseTypeRefs.Length + 1) { baseList.First };
        foreach(var entry in baseList.BaseTypeRefs)
        {
            allEntries.Add(entry);
        }

        var startIndex = 0;

        if(typeDef is ConcreteType concreteTypeDef)
        {
            var firstHandled = TryResolveConcreteBase(allEntries[0], concreteTypeDef);
            if(firstHandled)
            {
                startIndex = 1;
            }
        }

        for(var i = startIndex; i < allEntries.Count; i++)
        {
            ResolveAbstractBase(allEntries[i], typeDef);
        }
    }

    /// <summary>
    /// False when the entry should be treated as an abstract; true when it is consumed, including
    /// when it is refused.
    /// </summary>
    private bool TryResolveConcreteBase(BaseTypeRefNode baseTypeRef, ConcreteType typeDef)
    {
        var resolved = ResolveBaseTypeRef(baseTypeRef, typeDef);
        if(resolved is ErrorType)
        {
            // Already reported by the scope resolver.
            typeDef.BaseUnresolved = true;
            return true;
        }

        var definition = resolved is ConstructedType ct ? ct.Definition : resolved;

        if(definition is not ConcreteType resolvedConcrete)
        {
            return false;
        }

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
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.BaseTypeKindMismatch, baseTypeRef, null, new EnumValue(ownerKind), new EnumValue(baseKind), new NameValue(QualifiedNameHelper.Build(baseTypeRef.Name)));
            return true;
        }

        typeDef.BaseType = (MistType)resolved;
        return true;
    }

    private void ResolveAbstractBase(BaseTypeRefNode baseTypeRef, MemberType typeDef)
    {
        var resolved = ResolveBaseTypeRef(baseTypeRef, typeDef);
        if(resolved is ErrorType)
        {
            // Already reported by the scope resolver.
            return;
        }

        var definition = resolved is ConstructedType ct ? ct.Definition : resolved;

        if(definition is ConcreteType)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.ConcreteInAbstractList, baseTypeRef, null, new NameValue(QualifiedNameHelper.Build(baseTypeRef.Name)));
            return;
        }

        if(definition is not AbstractType)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.InvalidBaseType, baseTypeRef, null, new NameValue(QualifiedNameHelper.Build(baseTypeRef.Name)));
            return;
        }

        typeDef.BaseAbstractTypes.Add((MistType)resolved);
    }

    private IMistType ResolveBaseTypeRef(BaseTypeRefNode baseTypeRef, MemberType typeDef)
    {
        return baseTypeRef.Enclosing is { } enclosing
            ? scopeResolver.ResolveNestedGeneric(baseTypeRef.Name, enclosing.OuterArgs, enclosing.Nested, baseTypeRef.TypeArgs, typeDef.Scope, baseTypeRef)
            : scopeResolver.ResolveNamed(baseTypeRef.Name, baseTypeRef.TypeArgs, typeDef.Scope, baseTypeRef);
    }
}