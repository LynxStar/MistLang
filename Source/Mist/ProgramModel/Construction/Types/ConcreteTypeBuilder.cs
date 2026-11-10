using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// Phase 2 builder for classes and structs.
/// </summary>
internal sealed class ConcreteTypeBuilder : MemberTypeBuilder
{
    private readonly ConstructorBuilder constructorBuilder;
    private readonly CastOperatorBuilder castOperatorBuilder;

    public ConcreteTypeBuilder(BuilderContext context, ScopeResolver scopeResolver)
        : base(context, scopeResolver)
    {
        constructorBuilder = new ConstructorBuilder(context, DelegateResolver, ConstraintResolver);
        castOperatorBuilder = new CastOperatorBuilder(context, scopeResolver, DelegateResolver);
    }

    public void Build(ConcreteType typeDef)
    {
        BuildDeclaration(typeDef);
        BuildMembers(typeDef);

        // After the members, because what a spread reserves can only be compared against names that
        // exist -- and a spread declared last reserves just as much as one declared first.
        SpreadMember.ValidateStems(typeDef, typeDef.DeclarationNode!, Context.Diagnostics);
    }

    private void BuildDeclaration(ConcreteType typeDef)
    {
        BaseTypeResolver.Resolve(typeDef.DeclarationNode!, typeDef);

        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode!;
        var modifiers = ModifierBuilder.Build(body.TypeBody, Context.Diagnostics, typeDef.DeclaringType);
        Context.Attributes.Build(body.TypeBody.Attributes, typeDef.DeclaredAttributesList, typeDef.Scope);

        if(typeDef.NewtypeDeclaration is { } newtypeDeclaration)
        {
            modifiers = modifiers with
            {
                Accessibility = ModifierBuilder.Build(newtypeDeclaration, Context.Diagnostics, typeDef.DeclaringType).Accessibility,
                IsReadonly = modifiers.IsReadonly || NewtypeCloner.DeclaresReadonly(typeDef),
            };
        }

        typeDef.Modifiers = modifiers;

        // Refused here rather than left to emission, which drops an intrinsic type and everything it
        // declares, so a call to one of its ordinary members fails against generated C#. The
        // member-level marker needs no separate check, the pairing rule already requiring an
        // intrinsic container (spec Req 5.6, 5.13 and 5.15).
        if(modifiers.IsIntrinsic && typeDef.Assembly.Kind != MistAssemblyKind.Standard && typeDef.CloneSource is null)
        {
            Context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IntrinsicReservedForStandard, body.TypeBody, null, new NameValue(typeDef.Name));
        }

        if(typeDef.Scope.Parent is CloneScope { Arguments: { } arguments } cloneScope)
        {
            var targets = cloneScope.Source.TypeParametersList
                .Where(parameter => arguments[parameter.Ordinal] is TypeParameter bound && typeDef.TypeParametersList.Contains(bound))
                .ToDictionary(parameter => parameter.Name, parameter => (TypeParameter)arguments[parameter.Ordinal]);

            ConstraintResolver.ResolveRenamed(body.TypeBody.Constraints, targets, typeDef.Scope);
            return;
        }

        ConstraintResolver.Resolve(body.TypeBody.Constraints, typeDef.TypeParametersList, typeDef.Scope);
    }

    private void BuildMembers(ConcreteType typeDef)
    {
        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode!;

        foreach(var member in body.TypeBody.Members)
        {
            ResolveMember(member, typeDef);
        }
    }

    private void ResolveMember(MemberDeclarationNode member, ConcreteType typeDef)
    {
        switch(member)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                ResolveField(fieldMember.Field, typeDef);
                break;

            case PropertyMemberMemberDeclarationNode propertyMember:
                PropertyBuilder.Resolve(propertyMember.Property, typeDef);
                break;

            case IndexerMemberMemberDeclarationNode indexerMember:
                IndexerBuilder.Resolve(indexerMember.Indexer, typeDef);
                break;

            case MethodMemberMemberDeclarationNode methodMember:
                MethodBuilder.Resolve(methodMember.Method, typeDef);
                RecordEntryPointIfPresent(typeDef);
                break;

            case ConstructorMemberMemberDeclarationNode ctorMember:
                constructorBuilder.Resolve(ctorMember.Constructor, typeDef);
                break;

            case OperatorMemberMemberDeclarationNode opMember:
                OperatorBuilder.Resolve(opMember.Operator, typeDef);
                break;

            case CastOperatorMemberMemberDeclarationNode castOpMember:
                castOperatorBuilder.Resolve(castOpMember.CastOperator, typeDef);
                break;

            case NestedBodyTypeMemberMemberDeclarationNode:
            case NestedEnumMemberMemberDeclarationNode:
            case NestedNewtypeMemberMemberDeclarationNode:
            case NestedTypeAliasMemberMemberDeclarationNode:
                // Nested types are already registered.
                break;

            case DefaultBlockMemberMemberDeclarationNode defaultBlockMember:
                ResolveDefaultBlock(defaultBlockMember.DefaultBlock, typeDef);
                break;

            case AssociatedTypeMemberMemberDeclarationNode:
                Context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.TypeSlotNotAllowedOnConcrete, member, null);
                break;

            default:
                throw new InvalidOperationException($"Unrecognized member declaration node: {member.GetType().Name}");
        }
    }

    private void ResolveField(FieldDeclarationNode fieldDecl, ConcreteType typeDef)
    {
        var name = fieldDecl.Name.Lexeme;
        var type = SpreadMember.AggregateType(
            ScopeResolver.Resolve(fieldDecl.Type, typeDef.Scope), ScopeResolver.Interner);
        var ownership = ResolveOwnership(fieldDecl.OwnershipSpecifier);
        var modifiers = ModifierBuilder.ForMember(fieldDecl, Context.Diagnostics, typeDef);

        MemberNameValidation.Validate(name, typeDef.Name, fieldDecl, Context.Diagnostics);
        StorageTypeValidation.Validate(type, $"Field '{typeDef.Name}.{name}'", fieldDecl, Context);

        var initializer = fieldDecl.Initializer is { } initExpr ? new UnresolvedExpression(initExpr) : null;

        if(modifiers.IsAsync)
        {
            Context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AsyncOnlyOnMethods, fieldDecl, null);
        }

        if(modifiers.IsIntrinsic)
        {
            // Backend-provided storage, so only meaningful when public: nothing outside the type
            // could otherwise observe it.
            IntrinsicMemberValidation.Validate(typeDef.IsIntrinsic, typeDef.Name, modifiers.Accessibility == Accessibility.Public, fieldDecl, Context.Diagnostics);
        }

        var field = new Field(name, type, ownership, modifiers)
        {
            DeclaringType = typeDef,
            DeclarationNode = fieldDecl,
            Initializer = initializer,
        };
        Context.Attributes.Build(fieldDecl.Attributes, field.DeclaredAttributesList, typeDef.Scope);
        typeDef.FieldsList.Add(field);
    }

    private void ResolveDefaultBlock(DefaultBlockDeclarationNode blockDecl, ConcreteType typeDef)
    {
        if(typeDef.DefaultBlock is not null)
        {
            Context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.DuplicateDefaultBlock, blockDecl, null, new NameValue(typeDef.Name));
            return;
        }

        var entries = new List<DefaultEntry>();

        foreach(var entry in blockDecl.Entries)
        {
            switch(entry)
            {
                case DefaultFieldOverrideDefaultFieldInitializerNode fieldOverride:
                    var name = fieldOverride.FieldName.Lexeme;
                    var value = new UnresolvedExpression(fieldOverride.Value);
                    entries.Add(new DefaultValueEntry(name, value));
                    break;

                case DefaultTypeBindingDefaultFieldInitializerNode typeBinding:
                    var bindingName = typeBinding.FieldName.Lexeme;
                    var nestedEntries = ResolveNestedOverrides(typeBinding);
                    var boundType = ScopeResolver.Resolve(typeBinding.TypeName, typeDef.Scope);
                    entries.Add(new DefaultBindingEntry(bindingName, boundType, nestedEntries, typeBinding));
                    break;
            }
        }

        typeDef.DefaultBlock = new DefaultBlock(entries, blockDecl);
    }

    private static List<DefaultEntry> ResolveNestedOverrides(DefaultTypeBindingDefaultFieldInitializerNode typeBinding)
    {
        var nested = new List<DefaultEntry>();

        foreach(var nestedOverride in typeBinding.NestedEntries)
        {
            var nestedName = nestedOverride.FieldName.Lexeme;
            var nestedValue = new UnresolvedExpression(nestedOverride.Value);
            nested.Add(new DefaultValueEntry(nestedName, nestedValue));
        }

        return nested;
    }

}