using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics;

/// <summary>
/// Phase 2.5 — IClonable derivation (Ownership v2, design § "Phase 2.5").
///
/// <para>
/// Computes <see cref="ClassType.ImplementsIClonable"/> /
/// <see cref="StructType.ImplementsIClonable"/> for every user and bootstrap
/// class/struct type. A type implements <c>IClonable&lt;T&gt;</c> when EITHER:
/// </para>
/// <list type="number">
///   <item>it explicitly declares <c>IClonable&lt;T&gt;</c> in its abstract list
///         (the bootstrap value types and <c>String</c> are this base case), OR</item>
///   <item>every one of its instance fields has a type that itself implements
///         <c>IClonable&lt;T&gt;</c> (auto-derivation, Req 8 AC 6).</item>
/// </list>
/// A type with any field whose type is not clonable is NOT derived (Req 8 AC 7).
///
/// <para>
/// The derivation is the <b>greatest fixpoint</b> of the "all fields clonable"
/// rule. The algorithm starts by optimistically assuming every candidate type
/// (one that does not explicitly declare <c>IClonable</c>) is clonable, then
/// repeatedly eliminates any candidate that has a field whose type is provably
/// not clonable, until no further change occurs. This makes the pass correct in
/// the presence of recursion and cycles in the type graph (e.g. a struct that
/// holds a field of its own type, or two structs that reference each other):
/// such a cycle of otherwise-clonable types derives as clonable, and the
/// iteration always terminates because eliminations are monotone.
/// </para>
///
/// <para>
/// The result is cached on the type model for <c>MoveBoundaryResolver</c> to
/// query. The pass runs after the semantic model is built and before the
/// <c>OwnershipAnalyzer</c>.
/// </para>
/// </summary>
public static class IClonableDerivationEngine
{
    /// <summary>
    /// The display name of the stdlib <c>IClonable&lt;T&gt;</c> abstract type.
    /// </summary>
    private const string IClonableName = "IClonable";

    /// <summary>
    /// Fallback legacy-primitive keyword names that are clonable base cases when
    /// the stdlib is not loaded (the <see cref="LegacyPrimitiveTypes"/> singletons
    /// carry these display names and an empty member scope, so they do not declare
    /// the abstract type the way the real <c>Standard.*</c> types do).
    /// </summary>
    private static readonly HashSet<string> LegacyPrimitiveNames = new(StringComparer.Ordinal)
    {
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string",
    };

    /// <summary>
    /// Runs IClonable derivation over every class/struct type in the model and
    /// sets <c>ImplementsIClonable</c> accordingly.
    /// </summary>
    public static void RunInference(ISemanticModel model)
    {
        // Collect every user/stdlib class and struct type from the model.
        var allTypes = model.AllSymbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Struct)
            .Select(s => s.DeclaredType)
            .OfType<IType>()
            .Distinct()
            .ToList();

        // Types that explicitly declare IClonable<T> (directly or via a base type)
        // are clonable up front and are not subject to field-based derivation.
        var explicitDeclarers = new HashSet<IType>();

        // Candidates are everything else; we optimistically assume each is clonable
        // and eliminate those proven otherwise.
        var clonable = new Dictionary<IType, bool>();

        foreach(var type in allTypes)
        {
            if(DeclaresIClonableInAbstracts(type, new HashSet<IType>()))
            {
                _ = explicitDeclarers.Add(type);
                clonable[type] = true;
            }
            else
            {
                // Greatest-fixpoint seed: assume clonable until disproven.
                clonable[type] = true;
            }
        }

        // Iterate to a fixpoint, eliminating candidates with a non-clonable field.
        bool changed;
        do
        {
            changed = false;

            foreach(var type in allTypes)
            {
                // Explicit declarers are fixed-true and never eliminated.
                if(explicitDeclarers.Contains(type))
                {
                    continue;
                }

                if(!clonable[type])
                {
                    continue;
                }

                if(!AllFieldsClonable(type, clonable, explicitDeclarers))
                {
                    clonable[type] = false;
                    changed = true;
                }
            }
        }
        while(changed);

        // Write the derived result back onto the type model.
        foreach(var type in allTypes)
        {
            var result = clonable[type];
            switch(type)
            {
                case ClassType ct:
                    ct.ImplementsIClonable = result;
                    break;
                case StructType st:
                    st.ImplementsIClonable = result;
                    break;
            }
        }
    }

    /// <summary>
    /// Returns true when every instance field of <paramref name="type"/> has a
    /// clonable type under the current fixpoint assumption. A type with no
    /// instance fields is vacuously clonable.
    /// </summary>
    private static bool AllFieldsClonable(
        IType type,
        Dictionary<IType, bool> clonable,
        HashSet<IType> explicitDeclarers)
    {
        var memberScope = GetMemberScope(type);
        if(memberScope is null)
        {
            return true;
        }

        foreach(var member in memberScope.Members)
        {
            if(member.Kind != SymbolKind.Field)
            {
                continue;
            }

            // Static fields are not part of the value's clonable object graph.
            if(member is Symbol symbol && symbol.IsStatic)
            {
                continue;
            }

            var fieldType = member.DeclaredType;
            if(fieldType is null)
            {
                continue;
            }

            if(!IsTypeClonable(fieldType, clonable, explicitDeclarers))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether a field's type is clonable under the current fixpoint
    /// assumption. Handles primitives, value-type leaves, the type graph (with
    /// the candidate assumption), and conservative cases that cannot be proven.
    /// </summary>
    private static bool IsTypeClonable(
        IType type,
        Dictionary<IType, bool> clonable,
        HashSet<IType> explicitDeclarers)
    {
        type = Unwrap(type);

        switch(type)
        {
            case ClassType:
            case StructType:
                // Legacy-primitive fallback singletons (stdlib not loaded).
                if(LegacyPrimitiveNames.Contains(type.DisplayName))
                {
                    return true;
                }

                // A type that explicitly declares IClonable is always clonable.
                if(explicitDeclarers.Contains(type))
                {
                    return true;
                }

                // Otherwise use the current candidate assumption. A type outside
                // the model (no entry) is conservatively treated as not clonable
                // unless it explicitly declares the abstract type, which is checked
                // directly here to cover types reached only through fields.
                if(clonable.TryGetValue(type, out var isClonable))
                {
                    return isClonable;
                }

                return DeclaresIClonableInAbstracts(type, new HashSet<IType>());

            case EnumType:
                // Enums wrap a primitive underlying type and have value semantics.
                return true;

            case TupleType tuple:
                // A tuple (value type) is clonable iff all its elements are.
                foreach(var element in tuple.ElementTypes)
                {
                    if(!IsTypeClonable(element, clonable, explicitDeclarers))
                    {
                        return false;
                    }
                }

                return true;

            case ConstructedType constructed:
                // A constructed generic type (e.g. Box<int>) is clonable iff its
                // generic definition is clonable and every type argument is clonable.
                if(!IsTypeClonable(constructed.GenericDefinition, clonable, explicitDeclarers))
                {
                    return false;
                }

                foreach(var arg in constructed.TypeArguments)
                {
                    if(!IsTypeClonable(arg, clonable, explicitDeclarers))
                    {
                        return false;
                    }
                }

                return true;

            case TypeParameterType:
                // Cannot prove an unsubstituted type parameter clonable at the
                // declaration site — conservatively not derived.
                return false;

            default:
                // Abstract types, arrays, unknown, error, etc. are not
                // auto-derivable clonable sources.
                return false;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> declares <c>IClonable&lt;T&gt;</c>
    /// in its abstract list, checked directly and transitively through base types.
    /// </summary>
    private static bool DeclaresIClonableInAbstracts(IType type, HashSet<IType> visited)
    {
        type = Unwrap(type);

        if(!visited.Add(type))
        {
            return false;
        }

        return type switch
        {
            ClassType ct => AbstractListHasIClonable(ct.Abstracts)
                                || (ct.BaseType is not null && DeclaresIClonableInAbstracts(ct.BaseType, visited)),
            StructType st => AbstractListHasIClonable(st.Abstracts)
                                || (st.BaseType is not null && DeclaresIClonableInAbstracts(st.BaseType, visited)),
            _ => false,
        };
    }

    /// <summary>
    /// Returns true when any entry in the abstract list is the
    /// <c>IClonable</c> abstract type (either the bare abstract type or a constructed
    /// <c>IClonable&lt;T&gt;</c>).
    /// </summary>
    private static bool AbstractListHasIClonable(IReadOnlyList<IType> abstracts)
    {
        foreach(var abs in abstracts)
        {
            if(IsIClonableAbstract(abs))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when the given type reference denotes the stdlib
    /// <c>IClonable</c> abstract type, whether constructed (<c>IClonable&lt;T&gt;</c>)
    /// or bare.
    /// </summary>
    private static bool IsIClonableAbstract(IType type)
    {
        return type switch
        {
            ConstructedType constructed => IsIClonableAbstract(constructed.GenericDefinition),
            AbstractType abs => abs.DisplayName == IClonableName,
            _ => type.DisplayName == IClonableName && type.Kind == TypeKind.Abstract,
        };
    }

    /// <summary>
    /// Resolves transparent wrappers (type aliases, newtypes, refinement types)
    /// to the underlying nominal type for clonability inspection.
    /// </summary>
    private static IType Unwrap(IType type)
    {
        while(true)
        {
            switch(type)
            {
                case TypeAliasType alias:
                    type = alias.UnderlyingType;
                    break;
                case NewtypeType newtype:
                    type = newtype.SourceType;
                    break;
                case RefinementType refinement:
                    type = refinement.BaseType;
                    break;
                default:
                    return type;
            }
        }
    }

    /// <summary>
    /// Returns the member scope of a class or struct type, or null for types that
    /// have no inspectable member scope.
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };
    }
}