using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

internal sealed class ConstraintResolver
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;

    public ConstraintResolver(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

    /// <summary>
    /// Adhoc refinement constraints target dependent type parameters only.
    /// </summary>
    public void Resolve(ImmutableArray<WhereClauseNode> whereClauses, IReadOnlyList<TypeParameter> typeParameters, Scope scope)
    {
        Resolve(whereClauses, typeParameters, parameters: null, scope);
    }

    public void Resolve(
        ImmutableArray<WhereClauseNode> whereClauses,
        IReadOnlyList<TypeParameter> typeParameters,
        IReadOnlyList<Parameter>? parameters,
        Scope scope)
    {
        if(whereClauses.IsDefaultOrEmpty)
        {
            return;
        }

        foreach(var clause in whereClauses)
        {
            switch(clause)
            {
                case GenericConstraintClauseWhereClauseNode generic:
                    ResolveGenericConstraintClause(generic, typeParameters, scope);
                    break;

                case AdhocRefinementConstraintWhereClauseNode adhoc:
                    ResolveAdhocRefinement(adhoc, typeParameters, parameters);
                    break;
            }
        }

        foreach(var typeParam in typeParameters)
        {
            if(typeParam.ConstraintsList.Count > 0)
            {
                ValidateConstraints(typeParam, whereClauses.OfType<GenericConstraintClauseWhereClauseNode>()
                    .FirstOrDefault(clause => clause.Name.Lexeme == typeParam.Name) ?? DeclarationOf(typeParam));
            }
        }
    }

    /// <summary>
    /// A clone's where clauses, which name its source's type parameters. Each constrains the clone's own
    /// parameter it is bound to; one over a parameter the newtype binds to a type is judged where the newtype
    /// names its source, as any construction's is, and has nothing here to constrain.
    /// </summary>
    public void ResolveRenamed(ImmutableArray<WhereClauseNode> whereClauses, IReadOnlyDictionary<string, TypeParameter> targets, Scope scope)
    {
        if(whereClauses.IsDefaultOrEmpty)
        {
            return;
        }

        foreach(var clause in whereClauses)
        {
            switch(clause)
            {
                case GenericConstraintClauseWhereClauseNode generic when targets.TryGetValue(generic.Name.Lexeme, out var target):
                    ResolveConstraintList(generic.Constraints, target.ConstraintsList, Constraining(target, scope));
                    break;

                case AdhocRefinementConstraintWhereClauseNode adhoc when targets.TryGetValue(adhoc.Name.Lexeme, out var target):
                    target.RefinementNode = adhoc.Predicate;
                    break;
            }
        }

        foreach(var target in targets.Values.Distinct().Where(target => target.ConstraintsList.Count > 0))
        {
            ValidateConstraints(target, whereClauses.OfType<GenericConstraintClauseWhereClauseNode>()
                .FirstOrDefault(clause => targets.TryGetValue(clause.Name.Lexeme, out var parameter) && ReferenceEquals(parameter, target)) ?? DeclarationOf(target));
        }
    }

    private void ResolveGenericConstraintClause(
        GenericConstraintClauseWhereClauseNode generic,
        IReadOnlyList<TypeParameter> typeParameters,
        Scope scope)
    {
        var name = generic.Name.Lexeme;
        var target = FindTypeParameter(name, typeParameters);

        if(target is null)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.UnresolvedConstraintTarget, generic, null, new NameValue(name));
            return;
        }

        ResolveConstraintList(generic.Constraints, target.ConstraintsList, Constraining(target, scope));
    }

    private static Scope Constraining(TypeParameter target, Scope scope)
    {
        return target.IsPack ? new PackConstraintScope(scope, target) : scope;
    }

    /// <summary>
    /// Resolves an adhoc refinement constraint (where x >= 0). On a dependent type parameter the
    /// predicate is stashed beside its domain; on a method parameter it BECOMES the parameter's type,
    /// an anonymous refinement of the declared one -- `Foo(int x) where x > 90` takes an unnamed
    /// `int > 90` -- so every reader of a parameter's type sees the constraint where a named
    /// refinement would sit, and nothing has to remember a second field.
    /// </summary>
    private void ResolveAdhocRefinement(
        AdhocRefinementConstraintWhereClauseNode adhoc,
        IReadOnlyList<TypeParameter> typeParameters,
        IReadOnlyList<Parameter>? parameters)
    {
        var name = adhoc.Name.Lexeme;

        var typeParam = FindTypeParameter(name, typeParameters);
        if(typeParam is not null)
        {
            typeParam.RefinementNode = adhoc.Predicate;
            return;
        }

        var param = FindParameter(name, parameters);
        if(param is not null)
        {
            param.Type = TypeInterner.CreateRefinedType(param.Type, adhoc.Predicate);
            return;
        }

        context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.UnresolvedConstraintTarget, adhoc, null, new NameValue(name));
    }

    private static Parameter? FindParameter(string name, IReadOnlyList<Parameter>? parameters)
    {
        if(parameters is null)
        {
            return null;
        }

        for(var i = 0; i < parameters.Count; i++)
        {
            if(parameters[i].Name == name)
            {
                return parameters[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Shared by type-parameter <c>where</c> clauses and by a type slot's own (<c>:</c>) bound.
    /// </summary>
    internal void ResolveConstraintList(ConstraintListNode constraintList, List<TypeParameterConstraint> target, Scope scope)
    {
        ResolveConstraint(constraintList.Constraint, target, scope);

        foreach(var constraint in constraintList.Constraints)
        {
            ResolveConstraint(constraint, target, scope);
        }
    }

    private void ResolveConstraint(ConstraintNode constraint, List<TypeParameterConstraint> target, Scope scope)
    {
        TypeParameterConstraint resolved = constraint switch
        {
            ClassConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Class),
            StructConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Struct),
            StampableConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Stampable),
            ReadonlyConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Readonly),
            MutableConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Mutable),
            OwnedConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Owned),
            LinearConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Linear),
            AffineConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Affine),
            RelevantConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Relevant),
            DefaultConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Default),
            ConstructorConstraintConstraintNode ctor => ResolveConstructorConstraint(ctor, scope),
            TypeConstraintConstraintNode type => ResolveTypeConstraint(type, scope),
            _ => throw new InvalidOperationException($"Unrecognized constraint node: {constraint.GetType().Name}")
        };

        target.Add(resolved);
    }

    private ConstructorConstraint ResolveConstructorConstraint(
        ConstructorConstraintConstraintNode ctor, Scope scope)
    {
        if(ctor.ParamTypes is null)
        {
            return new ConstructorConstraint([]);
        }

        var paramTypes = new List<IMistType>
        {
            scopeResolver.Resolve(ctor.ParamTypes.TypeReference, scope)
        };

        foreach(var typeRef in ctor.ParamTypes.TypeReferences)
        {
            paramTypes.Add(scopeResolver.Resolve(typeRef, scope));
        }

        return new ConstructorConstraint(paramTypes);
    }

    private TypeConstraint ResolveTypeConstraint(TypeConstraintConstraintNode typeNode, Scope scope)
    {
        var resolved = scopeResolver.Resolve(typeNode.Type, scope);
        return new TypeConstraint(resolved);
    }

    private static TypeParameter? FindTypeParameter(string name, IReadOnlyList<TypeParameter> typeParameters)
    {
        for(var i = 0; i < typeParameters.Count; i++)
        {
            if(typeParameters[i].Name == name)
            {
                return typeParameters[i];
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Validation
    // ═══════════════════════════════════════════════════════════════════════

    private void ValidateConstraints(TypeParameter typeParam, Storm.Runtime.SyntaxNode? declaration)
    {
        var seen = new HashSet<TypeParameterConstraintKind>();

        foreach(var constraint in typeParam.ConstraintsList)
        {
            // Only keyword constraints can be duplicated or conflict.
            if(constraint is not KeywordConstraint)
            {
                continue;
            }

            if(!seen.Add(constraint.Kind))
            {
                ReportConstraint(ProgramModelDiagnostics.DuplicateConstraint, declaration,
                    new NameValue(typeParam.Name), new EnumValue(GetConstraintKeyword(constraint.Kind)));
            }
        }

        CheckConflict(typeParam, declaration, seen, TypeParameterConstraintKind.Class, TypeParameterConstraintKind.Struct);
        CheckConflict(typeParam, declaration, seen, TypeParameterConstraintKind.Class, TypeParameterConstraintKind.Stampable);
        CheckConflict(typeParam, declaration, seen, TypeParameterConstraintKind.Readonly, TypeParameterConstraintKind.Mutable);
        CheckSubstructuralConflicts(typeParam, declaration, seen);
    }

    private void CheckConflict(
        TypeParameter typeParam,
        Storm.Runtime.SyntaxNode? declaration,
        HashSet<TypeParameterConstraintKind> seen,
        TypeParameterConstraintKind a,
        TypeParameterConstraintKind b)
    {
        if(seen.Contains(a) && seen.Contains(b))
        {
            ReportConstraint(ProgramModelDiagnostics.ConflictingConstraints, declaration,
                new NameValue(typeParam.Name), new EnumValue(GetConstraintKeyword(a)), new EnumValue(GetConstraintKeyword(b)));
        }
    }

    private void CheckSubstructuralConflicts(TypeParameter typeParam, Storm.Runtime.SyntaxNode? declaration, HashSet<TypeParameterConstraintKind> seen)
    {
        var hasLinear = seen.Contains(TypeParameterConstraintKind.Linear);
        var hasAffine = seen.Contains(TypeParameterConstraintKind.Affine);
        var hasRelevant = seen.Contains(TypeParameterConstraintKind.Relevant);

        if(hasLinear && hasAffine)
        {
            ReportConstraint(ProgramModelDiagnostics.ConflictingConstraints, declaration,
                new NameValue(typeParam.Name), new EnumValue("linear"), new EnumValue("affine"));
        }

        if(hasLinear && hasRelevant)
        {
            ReportConstraint(ProgramModelDiagnostics.ConflictingConstraints, declaration,
                new NameValue(typeParam.Name), new EnumValue("linear"), new EnumValue("relevant"));
        }

        if(hasAffine && hasRelevant)
        {
            ReportConstraint(ProgramModelDiagnostics.ConflictingConstraints, declaration,
                new NameValue(typeParam.Name), new EnumValue("affine"), new EnumValue("relevant"));
        }
    }

    private static Storm.Runtime.SyntaxNode? DeclarationOf(TypeParameter parameter)
    {
        return parameter.DeclaringMethod?.DeclarationNode ?? (parameter.DeclaringType switch
        {
            MemberType member => member.DeclarationNode,
            TypeAlias alias => alias.DeclarationNode,
            Newtype newtype => newtype.DeclarationNode,
            EnumType enumType => enumType.DeclarationNode,
            _ => null,
        });
    }

    private void ReportConstraint(MistDiagnostic descriptor, Storm.Runtime.SyntaxNode? declaration, params ReadOnlySpan<SlotValue> arguments)
    {
        if(declaration is not null)
        {
            context.Diagnostics.ReportAtNodeTyped(descriptor, declaration, null, arguments);
        }
        else
        {
            context.Diagnostics.ReportTyped(descriptor, new Absent(AbsenceReason.Unavailable), arguments);
        }
    }

    private static string GetConstraintKeyword(TypeParameterConstraintKind kind)
    {
        return kind switch
        {
            TypeParameterConstraintKind.Class => "class",
            TypeParameterConstraintKind.Struct => "struct",
            TypeParameterConstraintKind.Stampable => "stampable",
            TypeParameterConstraintKind.Readonly => "readonly",
            TypeParameterConstraintKind.Mutable => "mutable",
            TypeParameterConstraintKind.Owned => "owned",
            TypeParameterConstraintKind.Linear => "linear",
            TypeParameterConstraintKind.Affine => "affine",
            TypeParameterConstraintKind.Relevant => "relevant",
            TypeParameterConstraintKind.Constructor => "new()",
            TypeParameterConstraintKind.Default => "default",
            TypeParameterConstraintKind.Type => "type",
            _ => kind.ToString()
        };
    }
}