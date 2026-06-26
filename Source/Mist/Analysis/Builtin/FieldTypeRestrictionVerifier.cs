using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Verifies the field-type restriction for types declared <c>owned</c> (Requirement 4).
/// All instance fields of an owned type must have types that are:
///   <list type="bullet">
///     <item>Another owned type (Req 4 AC 1)</item>
///     <item>A type implementing <c>IClonable&lt;T&gt;</c> (Req 4 AC 2)</item>
///     <item>A value type — struct or enum (Req 4 AC 3)</item>
///   </list>
///
/// <para>
/// Runs after IClonable derivation so auto-derived implementations are considered (Req 4 AC 5).
/// Reports <c>MIST_OWN_FIELD_TYPE_VIOLATION</c> for each violating field independently
/// (Req 5 AC 3, Req 11 AC 6).
/// </para>
/// </summary>
public sealed class FieldTypeRestrictionVerifier
{
    /// <summary>
    /// The diagnostic ID for owned-type field-type restriction violations.
    /// Defined here and referenced from the orchestrator.
    /// </summary>
    internal static readonly DiagnosticId FieldTypeViolation = new("MIST_OWN_FIELD_TYPE_VIOLATION");

    /// <summary>
    /// Checks all instance fields of an owned type against the field-type restriction.
    /// Reports <c>MIST_OWN_FIELD_TYPE_VIOLATION</c> for each violating field.
    /// </summary>
    /// <param name="ownedType">The type declared <c>owned</c> whose fields are verified.</param>
    /// <param name="model">The semantic model (used for source location resolution).</param>
    /// <param name="diagnostics">The diagnostic bag violations are emitted into.</param>
    public static void Verify(
        IType ownedType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(ownedType);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var memberScope = GetMemberScope(ownedType);
        if(memberScope is null)
        {
            return;
        }

        foreach(var member in memberScope.Members)
        {
            if(member.Kind != SymbolKind.Field)
            {
                continue;
            }

            // Skip static fields — they are not part of the instance object graph.
            if(member is Symbol symbol && symbol.IsStatic)
            {
                continue;
            }

            var fieldType = member.DeclaredType;
            if(fieldType is null)
            {
                continue;
            }

            if(!SatisfiesFieldTypeRestriction(fieldType))
            {
                ReportViolation(member, fieldType, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Determines whether a field's type satisfies the field-type restriction.
    /// Returns <c>true</c> if the type is:
    ///   (1) a value type (struct/enum) — Req 4 AC 3,
    ///   (2) another owned type — Req 4 AC 1,
    ///   (3) implements <c>IClonable&lt;T&gt;</c> — Req 4 AC 2, or
    ///   (4) a type parameter constrained to owned or IClonable — Req 4 AC 6.
    /// </summary>
    private static bool SatisfiesFieldTypeRestriction(IType fieldType)
    {
        // Unwrap constructed generics to the underlying definition for classification.
        var resolved = Unwrap(fieldType);

        // (1) Value types (struct, enum) — always accepted (Req 4 AC 3).
        if(IsValueType(resolved))
        {
            return true;
        }

        // (2) Another owned type — accepted (Req 4 AC 1).
        if(IsOwnedType(resolved))
        {
            return true;
        }

        // (3) Implements IClonable<T> — accepted (Req 4 AC 2).
        if(ImplementsIClonable(resolved))
        {
            return true;
        }

        // (4) Generic type parameter — check constraints (Req 4 AC 6).
        if(fieldType is TypeParameterType typeParam)
        {
            return HasOwnedOrClonableConstraint(typeParam);
        }

        return false;
    }

    /// <summary>
    /// Checks whether a type parameter has constraints that satisfy the field-type
    /// restriction: either an <c>owned</c> constraint (restricts to owned types),
    /// a <c>struct</c> constraint (restricts to value types), or an abstract type
    /// constraint that is <c>IClonable</c> (Req 4 AC 6).
    /// </summary>
    private static bool HasOwnedOrClonableConstraint(TypeParameterType typeParam)
    {
        var constraints = typeParam.Constraints;
        if(constraints is null)
        {
            return false;
        }

        // 'owned' constraint → type argument must be an owned type.
        if(constraints.HasOwnedConstraint)
        {
            return true;
        }

        // 'struct' constraint → type argument must be a value type.
        if(constraints.HasStructConstraint)
        {
            return true;
        }

        // Base class constraint that is an owned type satisfies the restriction.
        if(constraints.BaseClassConstraint is not null && IsOwnedType(Unwrap(constraints.BaseClassConstraint)))
        {
            return true;
        }

        // Abstract type constraint that is IClonable satisfies the restriction.
        foreach(var abs in constraints.AbstractConstraints)
        {
            if(IsIClonableInterface(abs))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Emits a <c>MIST_OWN_FIELD_TYPE_VIOLATION</c> diagnostic for a violating field.
    /// </summary>
    private static void ReportViolation(
        ISymbol field,
        IType fieldType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        SyntaxNode? declaringNode = null;
        try { declaringNode = model.GetDeclaringNode(field); }
        catch { /* no source location available */ }

        var message = fieldType is TypeParameterType
            ? FieldTypeViolationMessageForTypeParam(field.Name, fieldType.DisplayName)
            : FieldTypeViolationMessage(field.Name, fieldType.DisplayName);

        if(declaringNode is not null)
        {
            diagnostics.Add(MakeDiagnostic(FieldTypeViolation, message, declaringNode, model));
        }
        else
        {
            // Fallback: emit without source location if the declaring node is unavailable.
            diagnostics.Add(new Diagnostic(
                FieldTypeViolation,
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                message,
                Location: default,
                CompilationUnitPath: "",
                AnalyzerName: "Ownership"));
        }
    }

    // ── Message builders ─────────────────────────────────────────────────

    /// <summary>
    /// Message for <c>MIST_OWN_FIELD_TYPE_VIOLATION</c>: a non-owned reference type
    /// field without <c>IClonable&lt;T&gt;</c> in an owned type (Req 4 AC 4, Req 11 AC 2).
    /// </summary>
    internal static string FieldTypeViolationMessage(string fieldName, string typeName)
    {
        return $"Field '{fieldName}' of type '{typeName}' violates the owned-type field-type restriction: " +
        $"all fields of an owned type must be owned types, implement 'IClonable<T>', or be value types " +
        $"to guarantee full graph ownership.";
    }

    /// <summary>
    /// Message for <c>MIST_OWN_FIELD_TYPE_VIOLATION</c> on a generic type parameter
    /// that is not constrained (Req 4 AC 6).
    /// </summary>
    internal static string FieldTypeViolationMessageForTypeParam(string fieldName, string typeParamName)
    {
        return $"Field '{fieldName}' has unconstrained type parameter '{typeParamName}': generic fields in " +
        $"an owned type must be constrained to be an owned type or to implement 'IClonable<T>' " +
        $"to satisfy the field-type restriction.";
    }

    // ── Type classification helpers ──────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the given type is a value type (struct or enum).
    /// </summary>
    private static bool IsValueType(IType type)
    {
        return type.Kind is TypeKind.Struct or TypeKind.Enum;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type is declared as an owned type.
    /// For constructed generic types, checks the generic definition.
    /// </summary>
    private static bool IsOwnedType(IType type)
    {
        var resolved = Unwrap(type);
        return resolved switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => false
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the given type implements <c>IClonable&lt;T&gt;</c>.
    /// For constructed generic types, checks the generic definition.
    /// </summary>
    private static bool ImplementsIClonable(IType type)
    {
        var resolved = Unwrap(type);
        return resolved switch
        {
            ClassType ct => ct.ImplementsIClonable,
            StructType st => st.ImplementsIClonable,
            _ => false
        };
    }

    /// <summary>
    /// Returns <c>true</c> when the given type reference denotes the stdlib
    /// <c>IClonable</c> abstract type (either constructed or bare).
    /// </summary>
    private static bool IsIClonableInterface(IType type)
    {
        return type switch
        {
            ConstructedType constructed => IsIClonableInterface(constructed.GenericDefinition),
            AbstractType abs => abs.DisplayName == "IClonable",
            _ => type.DisplayName == "IClonable" && type.Kind == TypeKind.Abstract,
        };
    }

    /// <summary>
    /// Unwraps a <see cref="ConstructedType"/> to its generic definition.
    /// Non-constructed types pass through unchanged.
    /// </summary>
    private static IType Unwrap(IType type)
    {
        return type is ConstructedType constructed ? constructed.GenericDefinition : type;
    }

    /// <summary>
    /// Returns the member scope of a class or struct type, or <c>null</c> for types
    /// that have no inspectable member scope.
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        var resolved = Unwrap(type);
        return resolved switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null
        };
    }

    /// <summary>
    /// Creates a diagnostic with source location information resolved from the AST node.
    /// Mirrors the pattern in <see cref="OwnershipAnalyzer"/>.
    /// </summary>
    private static Diagnostic MakeDiagnostic(
        DiagnosticId id,
        string message,
        SyntaxNode node,
        ISemanticModel model)
    {
        Common.Text.LinePositionSpan? location = null;
        string? compilationUnitPath = null;

        var span = node.Span;
        if(span.Length > 0)
        {
            var asts = model.Asts;
            var units = model.Units;
            var astIndex = -1;

            for(var i = 0; i < asts.Length; i++)
            {
                var astSpan = asts[i].Span;
                if(span.Start >= astSpan.Start && span.Start + span.Length <= astSpan.Start + astSpan.Length)
                {
                    astIndex = i;
                    break;
                }
            }

            if(astIndex >= 0 && astIndex < units.Count)
            {
                var source = units[astIndex].Source;
                compilationUnitPath = source.Path;

                var sourceSpan = new Common.Text.SourceSpan(span.Start, span.Length);
                location = source.ToLinePositionSpan(sourceSpan);
            }
        }

        return new Diagnostic(
            id,
            DiagnosticSeverity.Error,
            PipelineStage.Analysis,
            message,
            Location: location ?? default,
            CompilationUnitPath: compilationUnitPath ?? "",
            AnalyzerName: "Ownership");
    }
}