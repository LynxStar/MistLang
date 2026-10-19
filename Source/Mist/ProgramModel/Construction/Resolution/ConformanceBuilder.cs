using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Phase 2c builder for retroactive conformance declarations (with blocks).
/// Processes all stashed <see cref="PendingConformance"/> entries after types
/// and extensions have been fully resolved.
/// </summary>
internal sealed class ConformanceBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;

    public ConformanceBuilder(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

    /// <summary>
    /// Resolves all stashed with-declarations into <see cref="ConformanceDeclaration"/> instances.
    /// </summary>
    public void BuildAll()
    {
        foreach(var pending in context.PendingConformances)
        {
            Build(pending);
        }
    }

    private void Build(PendingConformance pending)
    {
        var decl = pending.Declaration;

        // Resolve the conforming type.
        var conformingType = scopeResolver.ResolveNamedType(decl.TypeName.Lexeme, pending.Scope, decl);

        // Resolve the abstract type, accounting for arity if type args are present.
        var abstractName = decl.AbstractName.Lexeme;
        IMistType abstractType;

        if(decl.AbstractTypeArgs is { } typeArgList)
        {
            var arity = 1 + typeArgList.TypeArguments.Length;
            var arityName = ScopeResolver.BuildArityName(abstractName, arity);

            var definition = scopeResolver.ResolveNamedType(arityName, pending.Scope, decl);
            var typeArgs = scopeResolver.ResolveTypeArguments(typeArgList, pending.Scope, definition);
            context.ProgramModel.ConstructorConstraintClosures.RecordType(definition, typeArgs, decl, boundIn: pending.Scope);
            abstractType = context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, typeArgs);
        }
        else
        {
            abstractType = scopeResolver.ResolveNamedType(abstractName, pending.Scope, decl);
        }

        if(conformingType is ErrorType || abstractType is ErrorType)
        {
            return;
        }

        if(conformingType is not ConcreteType conformer)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ConformanceSourceNotConcrete, decl, null,
                TypeDisplay.Mist(conformingType));
            return;
        }

        var target = abstractType is ConstructedType constructed ? constructed.Definition : abstractType;

        if(target is not AbstractType)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ConformanceTargetNotAbstract, decl, null,
                TypeDisplay.Mist(abstractType), TypeDisplay.Mist(conformingType));
            return;
        }

        var mappings = new List<MemberMapping>();
        var unresolved = new List<string>();

        foreach(var mapping in decl.Mappings)
        {
            var resolved = Resolve(conformer, abstractType, mapping, decl);

            if(resolved.Count > 0)
            {
                mappings.AddRange(resolved);
            }
            else
            {
                unresolved.Add(mapping.TargetName.Lexeme);
            }
        }

        var conformance = new ConformanceDeclaration(
            conformer, abstractType, pending.NamespaceName, mappings, unresolved, decl);

        // Dropping the flat registration silently disables CSharpEmitterV2.ReportForeignConformances.
        conformer.ConformancesList.Add(conformance);
        context.ProgramModel.ConformancesList.Add(conformance);
    }

    private List<MemberMapping> Resolve(
        ConcreteType conformer,
        IMistType named,
        MemberMappingNode mapping,
        SyntaxNode node)
    {
        var targetName = mapping.TargetName.Lexeme;
        var sourceName = mapping.SourceName.Lexeme;

        var slots = FindSlots(named, targetName);

        if(slots.Count == 0)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ConformanceUnresolvedSlot, node, null,
                TypeDisplay.Mist(named), targetName);
            return [];
        }

        var source = TypeMemberLookup.Find(
            conformer, LookupRequest.Member(sourceName, MemberVisibility.InstanceAndStatic, node));

        var resolved = Pair(slots, source);

        if(resolved.Count == 0)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ConformanceMappingUnsatisfied, node, null,
                sourceName, TypeDisplay.Mist(conformer), targetName, TypeDisplay.Mist(named));
        }

        return resolved;
    }

    private List<MemberMapping> Pair(IReadOnlyList<Slot> slots, LookupResult source)
    {
        var mappings = new List<MemberMapping>();
        var interner = context.ProgramModel.TypeInterner;

        foreach(var slot in slots)
        {
            if(slot.Property is { } propertySlot && source is PropertyResult found)
            {
                var substitution = TypeSubstitution.ForMember(null, found, interner);

                if(OverloadSignature.TypeOf(propertySlot.Type) is { } type
                    && type == OverloadSignature.TypeOf(substitution.Apply(found.Property.Type, interner)))
                {
                    mappings.Add(new PropertyMapping(found.Property, propertySlot, slot.Declarer));
                }
            }
            else if(slot.Method is { } methodSlot && source is MethodGroupResult sources)
            {
                var matching = sources.Methods.Where(candidate =>
                {
                    var path = sources.Paths?.GetValueOrDefault(candidate);
                    var substitution = TypeSubstitution.ForPath(null, path, interner);

                    return OverloadSignature.Of(methodSlot, TypeSubstitution.Empty, interner) is { } signature
                        && signature == OverloadSignature.Of(candidate, substitution, interner)
                        && OverloadSignature.TypeOf(methodSlot.ReturnType, methodSlot.TypeParametersList) is { } returnType
                        && returnType == OverloadSignature.TypeOf(substitution.Apply(candidate.ReturnType, interner), candidate.TypeParametersList);
                }).Take(2).ToList();

                if(matching.Count > 1)
                {
                    return [];
                }

                if(matching.Count == 1)
                {
                    mappings.Add(new MethodMapping(matching[0], methodSlot, slot.Declarer));
                }
            }
        }

        return mappings;
    }

    private List<Slot> FindSlots(IMistType named, string name)
    {
        var slots = new List<Slot>();

        foreach(var (declarer, definition, substitution) in AbstractInheritance.Reached([named], context.ProgramModel.TypeInterner))
        {
            foreach(var method in definition.MethodsList.Where(method => method.Name == name))
            {
                slots.Add(new Slot(declarer, Close(method, substitution), null));
            }

            foreach(var property in definition.PropertiesList.Where(property => property.Name == name))
            {
                slots.Add(new Slot(declarer, null, Close(property, substitution)));
            }
        }

        return slots;
    }

    private Method Close(Method method, TypeSubstitution substitution)
    {
        var interner = context.ProgramModel.TypeInterner;
        var returnType = substitution.Apply(method.ReturnType, interner);
        var parameters = method.ParametersList.ConvertAll(parameter => Close(parameter, substitution));

        if(ReferenceEquals(returnType, method.ReturnType)
            && parameters.Zip(method.ParametersList).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return method;
        }

        var closed = new Method(method.Name, returnType, parameters, method.Modifiers)
        {
            Scope = method.Scope,
            SequenceLengthOperation = method.SequenceLengthOperation,
            ReturnOwnership = method.ReturnOwnership,
            DeclaredThisTier = method.DeclaredThisTier,
            DeclaringType = method.DeclaringType,
            DeclarationNode = method.DeclarationNode,
        };

        closed.TypeParametersList.AddRange(method.TypeParametersList);
        closed.ContextBindingsList.AddRange(method.ContextBindingsList);
        closed.AttributesList.AddRange(method.AttributesList);
        closed.DeclaredAttributesList.AddRange(method.DeclaredAttributesList);
        return closed;
    }

    private Parameter Close(Parameter parameter, TypeSubstitution substitution)
    {
        var type = substitution.Apply(parameter.Type, context.ProgramModel.TypeInterner);

        if(ReferenceEquals(type, parameter.Type))
        {
            return parameter;
        }

        var closed = new Parameter(parameter.Name, type, parameter.Modifier)
        {
            IsFlattened = parameter.IsFlattened,
            IsFailed = parameter.IsFailed,
            HasNativeDefault = parameter.HasNativeDefault,
            ClrDeclaresNullable = parameter.ClrDeclaresNullable,
        };

        closed.AttributesList.AddRange(parameter.AttributesList);
        closed.DeclaredAttributesList.AddRange(parameter.DeclaredAttributesList);
        return closed;
    }

    private Property Close(Property property, TypeSubstitution substitution)
    {
        var type = substitution.Apply(property.Type, context.ProgramModel.TypeInterner);

        if(ReferenceEquals(type, property.Type))
        {
            return property;
        }

        var closed = new Property(property.Name, type, property.Modifiers)
        {
            DeclaringType = property.DeclaringType,
            DeclarationNode = property.DeclarationNode,
            Getter = property.Getter,
            Setter = property.Setter,
        };

        closed.AttributesList.AddRange(property.AttributesList);
        closed.DeclaredAttributesList.AddRange(property.DeclaredAttributesList);
        return closed;
    }

    private sealed record Slot(IMistType Declarer, Method? Method, Property? Property);
}