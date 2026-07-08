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
    private readonly TypeReferenceResolver typeResolver;

    public ConstraintResolver(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// Resolves all generic constraint where clauses and adds the resulting
    /// constraints to the corresponding type parameters.
    /// Adhoc refinement constraints are skipped (handled separately).
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
        if(whereClauses.IsDefaultOrEmpty)
        {
            return;
        }

        foreach(var clause in whereClauses)
        {
            if(clause is not GenericConstraintClauseWhereClauseNode generic)
            {
                // Adhoc refinement constraints are not handled yet.
                continue;
            }

            var name = generic.Name.Lexeme;
            var target = FindTypeParameter(name, typeParameters);

            if(target is null)
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.UnresolvedConstraintTarget, generic, null, name);
                continue;
            }

            ResolveConstraintList(generic.Constraints, target, scope);
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
    /// Resolves a <see cref="ConstraintListNode"/> and adds all constraints
    /// to the target type parameter.
    /// </summary>
    private void ResolveConstraintList(ConstraintListNode constraintList, TypeParameter target, Scope scope)
    {
        ResolveConstraint(constraintList.Constraint, target, scope);

        foreach(var constraint in constraintList.Constraints)
        {
            ResolveConstraint(constraint, target, scope);
        }
    }

    /// <summary>
    /// Resolves a single constraint node and adds it to the target type parameter.
    /// </summary>
    private void ResolveConstraint(ConstraintNode constraint, TypeParameter target, Scope scope)
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

        target.ConstraintsList.Add(resolved);
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
            typeResolver.Resolve(ctor.ParamTypes.TypeReference, scope)
        };

        foreach(var typeRef in ctor.ParamTypes.TypeReferences)
        {
            paramTypes.Add(typeResolver.Resolve(typeRef, scope));
        }

        return new ConstructorConstraint(paramTypes);
    }

    /// <summary>
    /// Resolves a type constraint by resolving the type reference.
    /// </summary>
    private TypeConstraint ResolveTypeConstraint(TypeConstraintConstraintNode typeNode, Scope scope)
    {
        var resolved = typeResolver.Resolve(typeNode.Type, scope);
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