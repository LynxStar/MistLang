using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics;

/// <summary>
/// Infers IsOwnedType for struct declarations that satisfy the owned-type
/// field restriction without an explicit 'owned' modifier.
/// Runs after the semantic model is built and before the OwnershipAnalyzer.
/// </summary>
public static class OwnershipInferenceEngine
{
    /// <summary>
    /// Tracks the inference state of a struct during the resolution pass.
    /// Used for cycle detection.
    /// </summary>
    private enum InferenceState
    {
        /// <summary>Not yet visited.</summary>
        Unknown,

        /// <summary>Currently being resolved (cycle detection).</summary>
        InProgress,

        /// <summary>Resolution complete.</summary>
        Resolved,
    }

    /// <summary>
    /// Runs ownership inference on all struct types in the model.
    /// Sets IsOwnedType = true on structs where all fields are owned types.
    /// Handles cycles by using a three-state visited set (Unknown, InProgress, Resolved).
    /// </summary>
    public static void RunInference(ISemanticModel model)
    {
        var stateMap = new Dictionary<StructType, InferenceState>();

        // Collect all struct type symbols from the model.
        var structTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Struct)
            .Select(s => s.DeclaredType)
            .OfType<StructType>()
            .ToList();

        foreach (var structType in structTypes)
        {
            if (!stateMap.ContainsKey(structType))
                ResolveStruct(structType, stateMap);
        }
    }

    /// <summary>
    /// Resolves whether a struct qualifies as an owned type.
    /// Returns true if the struct is (or was inferred as) an owned type.
    /// </summary>
    private static bool ResolveStruct(StructType structType, Dictionary<StructType, InferenceState> stateMap)
    {
        // Already explicitly marked as owned (e.g., by the 'owned' modifier).
        if (structType.IsOwnedType)
        {
            stateMap[structType] = InferenceState.Resolved;
            return true;
        }

        // Check current state for cycle detection.
        if (stateMap.TryGetValue(structType, out var state))
        {
            return state switch
            {
                // Cycle detected — conservatively not owned.
                InferenceState.InProgress => false,
                // Already resolved — return current status.
                InferenceState.Resolved => structType.IsOwnedType,
                _ => false,
            };
        }

        // Mark as in-progress before examining fields.
        stateMap[structType] = InferenceState.InProgress;

        // Skip generic structs with type-parameter fields — cannot infer at declaration site.
        if (HasTypeParameterField(structType))
        {
            stateMap[structType] = InferenceState.Resolved;
            return false;
        }

        // Check all fields. Zero fields means vacuously owned.
        var allFieldsOwned = true;
        var fields = structType.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Field);

        foreach (var field in fields)
        {
            if (!IsFieldTypeOwned(field, stateMap))
            {
                allFieldsOwned = false;
                break;
            }
        }

        // Set the inference result.
        if (allFieldsOwned)
        {
            structType.IsOwnedType = true;
        }

        stateMap[structType] = InferenceState.Resolved;
        return structType.IsOwnedType;
    }

    /// <summary>
    /// Determines whether a struct has any field whose type is a type parameter.
    /// Generic structs with type-parameter fields are not eligible for inference.
    /// </summary>
    private static bool HasTypeParameterField(StructType structType)
    {
        return structType.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Field)
            .Any(field => field.DeclaredType is TypeParameterType);
    }

    /// <summary>
    /// Determines whether a field's type qualifies as an owned type.
    /// </summary>
    private static bool IsFieldTypeOwned(ISymbol field, Dictionary<StructType, InferenceState> stateMap)
    {
        var fieldType = field.DeclaredType;
        if (fieldType is null)
            return false;

        // If the field itself has the 'owned' specifier, it satisfies the requirement.
        if (field is Symbol symbol && symbol.OwnershipTier == OwnershipTier.Owned)
            return true;

        return IsTypeOwned(fieldType, stateMap);
    }

    /// <summary>
    /// Determines whether a type is an owned type (explicitly or by inference).
    /// </summary>
    private static bool IsTypeOwned(IType type, Dictionary<StructType, InferenceState> stateMap)
    {
        switch (type)
        {
            case StructType st:
                // Recursively resolve the struct.
                return ResolveStruct(st, stateMap);

            case ClassType ct:
                // Classes must be explicitly marked — no inference.
                return ct.IsOwnedType;

            case TypeParameterType:
                // Type parameters are not owned at the declaration site.
                return false;

            case EnumType:
                // Enums are value types with no reference fields — treat as owned.
                return true;

            default:
                // Other types (interfaces, traits, unknown, etc.) are not owned.
                return false;
        }
    }
}
