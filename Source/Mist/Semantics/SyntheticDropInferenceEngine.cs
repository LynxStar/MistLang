using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics;

/// <summary>
/// Synthesizes a <c>Drop()</c> method for class/struct types that contain one or more
/// droppable fields but do not explicitly define their own <c>Drop()</c>
/// (drop-semantics Requirement 10, Property 12).
/// <para>
/// Runs as Stage 4d of <c>CompilerDriver.Compile</c>, after the semantic model is
/// built, because it reads the finalized <c>IsOwnedType</c> / <c>Discipline</c> state.
/// It exposes a single static <see cref="RunInference"/> entry point and is invoked as a
/// hard-coded sequential stage (there is no pluggable pass registry).
/// </para>
/// <para>
/// For a type <c>T</c> with no explicit <c>Drop()</c>:
/// <list type="bullet">
///   <item>collect every field whose (resolved) type has a <c>Drop()</c> method;</item>
///   <item>if any such field exists, synthesize a <c>void Drop()</c>: set
///   <c>HasDrop = true</c>, <c>DropReturnType = null</c>, and record the ordered droppable
///   field list on <c>SyntheticDropFields</c> (declaration order, Req 10.2);</item>
///   <item>explicit <c>Drop()</c> always takes precedence — no synthesis (Req 10.6).</item>
/// </list>
/// Resolution is recursive so the obligation cascades transitively (Req 10.5): a field's
/// type is resolved (and possibly synthesized) before the containing type is decided, which
/// means dependency order / leaves-first processing falls out naturally. A three-state
/// visited map provides cycle detection.
/// </para>
/// </summary>
public static class SyntheticDropInferenceEngine
{
    /// <summary>
    /// Tracks the resolution state of a type during the inference pass (cycle detection).
    /// </summary>
    private enum InferenceState
    {
        /// <summary>Currently being resolved (cycle detection).</summary>
        InProgress,

        /// <summary>Resolution complete.</summary>
        Resolved,
    }

    /// <summary>
    /// Runs synthetic drop inference over all class/struct types in the model.
    /// Synthesizes a void <c>Drop()</c> for every type that has droppable fields and
    /// no explicit <c>Drop()</c>, cascading the obligation transitively.
    /// </summary>
    public static void RunInference(ISemanticModel model)
    {
        // Snapshot the set of types that have an EXPLICIT Drop() before any synthesis runs.
        // RecognizeDropMethods (semantic model build) has already set HasDrop = true for these.
        // Capturing it up front lets us distinguish "explicit drop" (precedence, never
        // synthesize) from "synthesized drop" (which also sets HasDrop = true as we go).
        var explicitDrop = new HashSet<IType>();
        var declaredTypes = new List<IType>();

        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var type = symbol.DeclaredType;
            if(type is not (ClassType or StructType))
            {
                continue;
            }

            declaredTypes.Add(type);

            if(HasDrop(type))
            {
                _ = explicitDrop.Add(type);
            }
        }

        var stateMap = new Dictionary<IType, InferenceState>();

        foreach(var type in declaredTypes)
        {
            if(!stateMap.ContainsKey(type))
            {
                _ = Resolve(type, explicitDrop, stateMap);
            }
        }
    }

    /// <summary>
    /// Resolves whether <paramref name="type"/> is droppable, synthesizing a <c>Drop()</c>
    /// when it has droppable fields and no explicit <c>Drop()</c>. Returns whether the type
    /// ends up droppable (explicit or synthesized).
    /// </summary>
    private static bool Resolve(
        IType type,
        HashSet<IType> explicitDrop,
        Dictionary<IType, InferenceState> stateMap)
    {
        // Explicit Drop() always wins — never synthesize over it (Req 10.6).
        if(explicitDrop.Contains(type))
        {
            stateMap[type] = InferenceState.Resolved;
            return true;
        }

        if(stateMap.TryGetValue(type, out var state))
        {
            return state switch
            {
                // Cycle — conservatively treat as not-yet-droppable for this edge.
                InferenceState.InProgress => false,
                // Already resolved — report the synthesized result.
                InferenceState.Resolved => HasDrop(type),
                _ => false,
            };
        }

        var memberScope = MemberScopeOf(type);
        if(memberScope is null)
        {
            stateMap[type] = InferenceState.Resolved;
            return HasDrop(type);
        }

        // Mark in-progress before examining fields (cycle detection).
        stateMap[type] = InferenceState.InProgress;

        // Collect droppable fields in declaration order. Member ordering preserves
        // declaration order (Req 10.2).
        var droppableFields = new List<ISymbol>();
        foreach(var member in memberScope.Members)
        {
            if(member.Kind != SymbolKind.Field)
            {
                continue;
            }

            var fieldType = member.DeclaredType;
            if(fieldType is null)
            {
                continue;
            }

            if(IsDroppable(fieldType, explicitDrop, stateMap))
            {
                droppableFields.Add(member);
            }
        }

        // Synthesize a void Drop() when droppable fields exist (Req 10.1, 10.2, 10.3).
        if(droppableFields.Count > 0)
        {
            switch(type)
            {
                case ClassType ct:
                    ct.HasDrop = true;
                    ct.DropReturnType = null;
                    ct.SyntheticDropFields = droppableFields;
                    break;
                case StructType st:
                    st.HasDrop = true;
                    st.DropReturnType = null;
                    st.SyntheticDropFields = droppableFields;
                    break;
            }
        }

        stateMap[type] = InferenceState.Resolved;
        return HasDrop(type);
    }

    /// <summary>
    /// Determines whether a field's type is droppable, resolving the underlying type
    /// through aliases and generic constructions and recursing into user types so the
    /// drop obligation cascades transitively (Req 10.5).
    /// </summary>
    private static bool IsDroppable(
        IType type,
        HashSet<IType> explicitDrop,
        Dictionary<IType, InferenceState> stateMap)
    {
        switch(type)
        {
            case TypeAliasType alias:
                return IsDroppable(alias.UnderlyingType, explicitDrop, stateMap);

            case ConstructedType constructed:
                // A generic instantiation is droppable iff its definition is.
                return IsDroppable(constructed.GenericDefinition, explicitDrop, stateMap);

            case ClassType ct when ct.IsDisposableDrop:
                return false;

            case StructType st when st.IsDisposableDrop:
                return false;

            case ClassType or StructType:
                // Recurse so the field's type is synthesized before the container is decided.
                return Resolve(type, explicitDrop, stateMap);

            default:
                // Intrinsic / other types: trust the HasDrop flag set during model build.
                return HasDrop(type);
        }
    }

    /// <summary>Returns whether a type currently carries a <c>Drop()</c> method.</summary>
    private static bool HasDrop(IType type)
    {
        return type switch
        {
            ClassType ct => ct.HasDrop,
            StructType st => st.HasDrop,
            _ => false,
        };
    }

    /// <summary>Returns the member scope for a class/struct type, or null otherwise.</summary>
    private static IScope? MemberScopeOf(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };
    }
}