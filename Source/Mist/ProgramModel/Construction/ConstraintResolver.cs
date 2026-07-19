using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves <see cref="WhereClauseNode"/> arrays from the AST into
/// <see cref="TypeParameterConstraint"/> instances on the target type parameters.
/// Shared between type-level (Phase 2 type builders) and method-level
/// (MethodBuilder) constraint resolution.
/// </summary>
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
    /// Resolves all generic constraint where clauses and adds the resulting
    /// constraints to the corresponding type parameters.
    /// Adhoc refinement constraints target dependent type parameters only.
    /// After resolution, validates for conflicting and duplicate constraints.
    /// </summary>
    /// <param name="whereClauses">The where clause array from the AST node.</param>
    /// <param name="typeParameters">The type parameters to match against by name.</param>
    /// <param name="scope">The scope for resolving type references in constraints.</param>
    public void Resolve(
        ImmutableArray<WhereClauseNode> whereClauses,
        IReadOnlyList<TypeParameter> typeParameters,
        Scope scope)
    {
        Resolve(whereClauses, typeParameters, parameters: null, scope);
    }

    /// <summary>
    /// Resolves all where clauses: generic constraint clauses are added to type
    /// parameters; adhoc refinement clauses are stashed on matching type parameters
    /// or method parameters.
    /// </summary>
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

        // Validate constraints after all clauses are resolved.
        foreach(var typeParam in typeParameters)
        {
            if(typeParam.ConstraintsList.Count > 0)
            {
                ValidateConstraints(typeParam);
            }
        }
    }

    /// <summary>
    /// Resolves a generic constraint clause (where T : class, IFoo).
    /// </summary>
    private void ResolveGenericConstraintClause(
        GenericConstraintClauseWhereClauseNode generic,
        IReadOnlyList<TypeParameter> typeParameters,
        Scope scope)
    {
        var name = generic.Name.Lexeme;
        var target = FindTypeParameter(name, typeParameters);

        if(target is null)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.UnresolvedConstraintTarget, generic, null, name);
            return;
        }

        ResolveConstraintList(generic.Constraints, target.ConstraintsList, scope);
    }

    /// <summary>
    /// Resolves an adhoc refinement constraint (where x >= 0). Stashes the predicate
    /// AST node on the matching dependent type parameter or method parameter.
    /// </summary>
    private void ResolveAdhocRefinement(
        AdhocRefinementConstraintWhereClauseNode adhoc,
        IReadOnlyList<TypeParameter> typeParameters,
        IReadOnlyList<Parameter>? parameters)
    {
        var name = adhoc.Name.Lexeme;

        // First, try to match a dependent type parameter.
        var typeParam = FindTypeParameter(name, typeParameters);
        if(typeParam is not null)
        {
            typeParam.RefinementNode = adhoc.Predicate;
            return;
        }

        // Second, try to match a method parameter.
        var param = FindParameter(name, parameters);
        if(param is not null)
        {
            param.RefinementNode = adhoc.Predicate;
            return;
        }

        // Name doesn't match any type parameter or method parameter.
        context.Diagnostics.ReportAtNode(
            ProgramModelDiagnostics.UnresolvedConstraintTarget, adhoc, null, name);
    }

    /// <summary>
    /// Finds a parameter by name in the provided list.
    /// </summary>
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
    /// Resolves a <see cref="ConstraintListNode"/> and adds all constraints to the
    /// given target collection. Shared by type-parameter <c>where</c> clauses and by
    /// a type slot's own (<c>:</c>) bound.
    /// </summary>
    internal void ResolveConstraintList(ConstraintListNode constraintList, List<TypeParameterConstraint> target, Scope scope)
    {
        ResolveConstraint(constraintList.Constraint, target, scope);

        foreach(var constraint in constraintList.Constraints)
        {
            ResolveConstraint(constraint, target, scope);
        }
    }

    /// <summary>
    /// Resolves a single constraint node and adds it to the target collection.
    /// </summary>
    private void ResolveConstraint(ConstraintNode constraint, List<TypeParameterConstraint> target, Scope scope)
    {
        TypeParameterConstraint resolved = constraint switch
        {
            ClassConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Class),
            StructConstraintConstraintNode => new KeywordConstraint(TypeParameterConstraintKind.Struct),
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

    /// <summary>
    /// Resolves a constructor constraint, including its parameter types if any.
    /// </summary>
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

    /// <summary>
    /// Resolves a type constraint by resolving the type reference.
    /// </summary>
    private TypeConstraint ResolveTypeConstraint(TypeConstraintConstraintNode typeNode, Scope scope)
    {
        var resolved = scopeResolver.Resolve(typeNode.Type, scope);
        return new TypeConstraint(resolved);
    }

    /// <summary>
    /// Finds a type parameter by name in the provided list.
    /// </summary>
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

    /// <summary>
    /// Validates constraints on a type parameter after resolution:
    /// - Reports errors for mutually exclusive constraint pairs.
    /// - Reports warnings for duplicate keyword constraints.
    /// </summary>
    private void ValidateConstraints(TypeParameter typeParam)
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
                // Duplicate keyword constraint.
                context.Diagnostics.Report(
                    ProgramModelDiagnostics.DuplicateConstraint,
                    default,
                    "",
                    typeParam.Name,
                    GetConstraintKeyword(constraint.Kind));
            }
        }

        // Check mutually exclusive pairs.
        CheckConflict(typeParam, seen, TypeParameterConstraintKind.Class, TypeParameterConstraintKind.Struct);
        CheckConflict(typeParam, seen, TypeParameterConstraintKind.Readonly, TypeParameterConstraintKind.Mutable);
        CheckSubstructuralConflicts(typeParam, seen);
    }

    /// <summary>
    /// Reports an error if both constraint kinds are present on the same type parameter.
    /// </summary>
    private void CheckConflict(
        TypeParameter typeParam,
        HashSet<TypeParameterConstraintKind> seen,
        TypeParameterConstraintKind a,
        TypeParameterConstraintKind b)
    {
        if(seen.Contains(a) && seen.Contains(b))
        {
            context.Diagnostics.Report(
                ProgramModelDiagnostics.ConflictingConstraints,
                default,
                "",
                typeParam.Name,
                GetConstraintKeyword(a),
                GetConstraintKeyword(b));
        }
    }

    /// <summary>
    /// Reports an error if more than one substructural discipline constraint
    /// (linear, affine, relevant) is present on the same type parameter.
    /// </summary>
    private void CheckSubstructuralConflicts(TypeParameter typeParam, HashSet<TypeParameterConstraintKind> seen)
    {
        var hasLinear = seen.Contains(TypeParameterConstraintKind.Linear);
        var hasAffine = seen.Contains(TypeParameterConstraintKind.Affine);
        var hasRelevant = seen.Contains(TypeParameterConstraintKind.Relevant);

        if(hasLinear && hasAffine)
        {
            context.Diagnostics.Report(
                ProgramModelDiagnostics.ConflictingConstraints, default, "",
                typeParam.Name, "linear", "affine");
        }

        if(hasLinear && hasRelevant)
        {
            context.Diagnostics.Report(
                ProgramModelDiagnostics.ConflictingConstraints, default, "",
                typeParam.Name, "linear", "relevant");
        }

        if(hasAffine && hasRelevant)
        {
            context.Diagnostics.Report(
                ProgramModelDiagnostics.ConflictingConstraints, default, "",
                typeParam.Name, "affine", "relevant");
        }
    }

    /// <summary>
    /// Returns the Mist keyword string for a constraint kind.
    /// </summary>
    private static string GetConstraintKeyword(TypeParameterConstraintKind kind)
    {
        return kind switch
        {
            TypeParameterConstraintKind.Class => "class",
            TypeParameterConstraintKind.Struct => "struct",
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