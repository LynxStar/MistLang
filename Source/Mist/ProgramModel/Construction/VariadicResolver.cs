using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Handles variadic type-parameter pack resolution: pack position semantics, pack indexing,
/// variadic family lookup, variadic constructed type binding, spread tuple construction, and
/// dependent literal lifting. Extracted from <see cref="ScopeResolver"/> to keep the core
/// scope/type resolution focused on non-variadic concerns.
/// </summary>
internal sealed class VariadicResolver
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;

    public VariadicResolver(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Spread tuple
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// True when <paramref name="typeRef"/> is a single (non-union) pack-spread operand
    /// — the <c>TArgs...</c> inside <c>(TArgs...)</c>.
    /// </summary>
    public static bool TryGetLoneSpread(TypeReferenceNode typeRef, out SpreadSingleTypeRefSingleTypeRefNode spread)
    {
        if(typeRef.SingleTypeRefs.Length == 0 && typeRef.First is SpreadSingleTypeRefSingleTypeRefNode loneSpread)
        {
            spread = loneSpread;
            return true;
        }

        spread = null!;
        return false;
    }

    /// <summary>
    /// Resolves the value-pack tuple <c>(TArgs...)</c> from a lone spread operand. The
    /// operand must denote a type-parameter pack; the resulting tuple's element list is
    /// the pack itself, which "spreads" into a single symbolic element while the pack is
    /// unbound (a later stage expands it into one tuple element per bound type once the
    /// pack binds a concrete <c>Type[]</c>).
    /// </summary>
    public IMistType ResolveSpreadTuple(SpreadSingleTypeRefSingleTypeRefNode spread, Scope scope)
    {
        var pack = ResolveSpreadOperandPack(spread, scope);
        return pack is null ? ErrorType.Instance : BuildValuePackTuple(pack);
    }

    /// <summary>
    /// Resolves a spread operand (the <c>TArgs</c> in <c>TArgs...</c>) to its
    /// type-parameter pack. Returns <c>null</c> when the operand does not denote a pack.
    /// </summary>
    public TypeParameter? ResolveSpreadOperandPack(SpreadSingleTypeRefSingleTypeRefNode spread, Scope scope)
    {
        var operand = scopeResolver.ResolveNamedType(QualifiedNameHelper.Build(spread.Name), scope, spread);
        if(operand is ErrorType)
        {
            return null;
        }

        if(operand is TypeParameter { IsPack: true } pack)
        {
            return pack;
        }

        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, spread, null, spread.GetType().Name);
        return null;
    }

    /// <summary>
    /// Builds the value-pack tuple type <c>(TArgs...)</c> for the type-parameter pack.
    /// </summary>
    public TupleType BuildValuePackTuple(TypeParameter pack)
    {
        return context.ProgramModel.TypeInterner.GetOrCreateTupleType(new IMistType[] { pack });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pack position semantics
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies pack position semantics (Requirement 6) to a named reference whose operand
    /// resolved to a type-parameter pack.
    /// </summary>
    public IMistType ResolvePackInTypePosition(
        TypeParameter pack,
        NamedSingleTypeRefSingleTypeRefNode named,
        bool barePackAllowed)
    {
        if(named.ArrayRank is SizedArrayRankArrayRankSuffixNode sized)
        {
            return ResolvePackIndex(pack, sized, named);
        }

        if(barePackAllowed && named.ArrayRank is null && named.Optional is null)
        {
            return pack;
        }

        context.Diagnostics.ReportAtNode(VariadicsDiagnostics.PackInTypePosition, named, null, pack.Name);
        return ErrorType.Instance;
    }

    /// <summary>
    /// Resolves pack indexing <c>TArgs[i]</c> under the capability ladder.
    /// </summary>
    private IMistType ResolvePackIndex(
        TypeParameter pack,
        SizedArrayRankArrayRankSuffixNode sized,
        Storm.Runtime.SyntaxNode locationNode)
    {
        var index = long.TryParse(sized.Size.Lexeme, out var parsed) ? parsed : 0L;

        if(pack.FixedPackLength is not { } length)
        {
            context.Diagnostics.ReportAtNode(
                VariadicsDiagnostics.UnprovenPackIndex, locationNode, null, pack.Name, index + 1);
            return ErrorType.Instance;
        }

        if(index < 0 || index >= length)
        {
            context.Diagnostics.ReportAtNode(
                VariadicsDiagnostics.PackIndexOutOfBounds, locationNode, null, index, pack.Name, length);
            return ErrorType.Instance;
        }

        return UnknownType.Instance;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Variadic family lookup and construction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Searches for the variadic family named <paramref name="baseName"/> (a definition with
    /// a single bracket-free <c>params</c> pack).
    /// </summary>
    public IMistType? TryResolveVariadicFamily(string baseName, Scope scope)
    {
        var (namespacePrefix, simpleName) = SplitQualifiedName(baseName);

        if(namespacePrefix is null)
        {
            for(var current = scope; current is not null; current = current.Parent)
            {
                foreach(var entry in current.LocalTypes)
                {
                    if(SimpleName(entry.Key) == simpleName && IsUnbracketedVariadicFamily(entry.Value))
                    {
                        return entry.Value;
                    }
                }
            }
        }

        var candidatesByNamespace = new Dictionary<string, IMistType>(StringComparer.Ordinal);
        foreach(var type in context.ProgramModel.TypesByFQN.Values)
        {
            if(SimpleName(type.Name) == simpleName && IsUnbracketedVariadicFamily(type))
            {
                _ = candidatesByNamespace.TryAdd(type.NamespaceName, type);
            }
        }

        if(candidatesByNamespace.Count == 0)
        {
            return null;
        }

        foreach(var ns in EnumerateVisibleNamespaces(scope, namespacePrefix))
        {
            if(candidatesByNamespace.TryGetValue(ns, out var family))
            {
                return family;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the definition has a pack slot, or the use site has a bracketed pack group.
    /// </summary>
    public bool IsVariadicConstruction(IMistType definition, TypeArgumentListNode typeArgList)
    {
        var slots = GetDeclaredSlots(definition);
        return slots is not null && (HasPackSlot(slots) || HasBracketedPackArgument(typeArgList));
    }

    /// <summary>
    /// Binds a flat (or explicitly bracketed) use-site argument list to a variadic
    /// definition's declared slots via <see cref="PackBinder"/>, then interns the
    /// resulting constructed type.
    /// </summary>
    public IMistType ResolveVariadicConstructedType(
        IMistType definition,
        TypeArgumentListNode typeArgList,
        Scope scope,
        Storm.Runtime.SyntaxNode locationNode)
    {
        var slots = GetDeclaredSlots(definition)!;
        var slotKinds = new List<BindingSlotKind>(slots.Count);
        foreach(var slot in slots)
        {
            slotKinds.Add(ToBindingSlotKind(slot));
        }

        var arguments = BuildUseSiteArguments(typeArgList, scope);
        var result = PackBinder.Bind(slotKinds, arguments);

        if(result.Status == PackBindingStatus.TooFewArguments)
        {
            context.Diagnostics.ReportAtNode(
                VariadicsDiagnostics.TooFewArgumentsForBinding, locationNode, null, definition.Name);
            return ErrorType.Instance;
        }

        var canonicalArguments = new List<IMistType>(result.Slots.Count);
        foreach(var binding in result.Slots)
        {
            if(binding.IsPack)
            {
                canonicalArguments.Add(
                    context.ProgramModel.TypeInterner.GetOrCreateLiftedTypeArray(binding.Elements));
            }
            else
            {
                canonicalArguments.Add(binding.Elements.Count == 1 ? binding.Elements[0] : ErrorType.Instance);
            }
        }

        return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, canonicalArguments);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dependent literal lifting
    // ═══════════════════════════════════════════════════════════════════════

    public IMistType LiftIntegerLiteral(string lexeme)
    {
        var value = long.TryParse(lexeme, out var parsed) ? parsed : 0L;
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(value, lexeme);
    }

    public IMistType LiftNegatedIntegerLiteral(string lexeme)
    {
        var value = long.TryParse(lexeme, out var parsed) ? -parsed : 0L;
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(value, $"-{lexeme}");
    }

    public IMistType LiftHexLiteral(string lexeme)
    {
        var hexStr = lexeme.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        var value = long.TryParse(hexStr, NumberStyles.HexNumber, null, out var parsed)
            ? parsed : 0L;
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(value, lexeme);
    }

    public IMistType LiftBinaryLiteral(string lexeme)
    {
        var binStr = lexeme.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        long value;
        try
        {
            value = Convert.ToInt64(binStr, 2);
        }
        catch
        {
            value = 0L;
        }

        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(value, lexeme);
    }

    public IMistType LiftCharLiteral(string lexeme)
    {
        var charValue = lexeme.Length >= 3 ? lexeme[1] : '\0';
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(charValue, lexeme);
    }

    public IMistType LiftBoolTrue()
    {
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(true, "true");
    }

    public IMistType LiftBoolFalse()
    {
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(false, "false");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static bool IsUnbracketedVariadicFamily(IMistType type)
    {
        var slots = GetDeclaredSlots(type);
        return slots is not null && HasUnbracketedPackSlot(slots);
    }

    private static BindingSlotKind ToBindingSlotKind(TypeParameter slot)
    {
        return slot.PackKind switch
        {
            TypeParameterPackKind.Unbracketed => BindingSlotKind.UnbracketedPack,
            TypeParameterPackKind.Bracketed => BindingSlotKind.BracketedPack,
            _ => BindingSlotKind.Fixed,
        };
    }

    private static IReadOnlyList<TypeParameter>? GetDeclaredSlots(IMistType definition)
    {
        return definition switch
        {
            MemberType member => member.TypeParametersList,
            TypeAlias alias => alias.TypeParametersList,
            Newtype newtype => newtype.TypeParametersList,
            _ => null,
        };
    }

    private static bool HasPackSlot(IReadOnlyList<TypeParameter> slots)
    {
        return slots.Any(s => s.IsPack);
    }

    private static bool HasUnbracketedPackSlot(IReadOnlyList<TypeParameter> slots)
    {
        return slots.Any(s => s.IsUnbracketedPack);
    }

    private static bool HasBracketedPackArgument(TypeArgumentListNode typeArgList)
    {
        if(typeArgList.TypeArgument is BracketedPackTypeArgumentTypeArgumentNode)
        {
            return true;
        }

        foreach(var arg in typeArgList.TypeArguments)
        {
            if(arg is BracketedPackTypeArgumentTypeArgumentNode)
            {
                return true;
            }
        }

        return false;
    }

    private List<UseSiteArgument> BuildUseSiteArguments(TypeArgumentListNode typeArgList, Scope scope)
    {
        var arguments = new List<UseSiteArgument>(1 + typeArgList.TypeArguments.Length);

        AddUseSiteArgument(typeArgList.TypeArgument, scope, arguments);

        foreach(var arg in typeArgList.TypeArguments)
        {
            AddUseSiteArgument(arg, scope, arguments);
        }

        return arguments;
    }

    private void AddUseSiteArgument(TypeArgumentNode arg, Scope scope, List<UseSiteArgument> arguments)
    {
        if(arg is BracketedPackTypeArgumentTypeArgumentNode bracketed)
        {
            arguments.Add(UseSiteArgument.Group(ResolveBracketedGroup(bracketed, scope)));
        }
        else
        {
            arguments.Add(UseSiteArgument.Scalar(scopeResolver.ResolveScalarTypeArgument(arg, scope)));
        }
    }

    private List<IMistType> ResolveBracketedGroup(BracketedPackTypeArgumentTypeArgumentNode bracketed, Scope scope)
    {
        var elements = new List<IMistType>();

        if(bracketed.PackElements is { } list)
        {
            elements.Add(scopeResolver.Resolve(list.TypeReference, scope));

            foreach(var element in list.TypeReferences)
            {
                elements.Add(scopeResolver.Resolve(element, scope));
            }
        }

        return elements;
    }

    private static (string? NamespacePrefix, string SimpleName) SplitQualifiedName(string baseName)
    {
        var lastDot = baseName.LastIndexOf('.');
        return lastDot < 0
            ? (null, baseName)
            : (baseName[..lastDot], baseName[(lastDot + 1)..]);
    }

    private static string SimpleName(string name)
    {
        var angle = name.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? name : name[..angle];
    }

    private static IEnumerable<string> EnumerateVisibleNamespaces(Scope scope, string? namespacePrefix)
    {
        if(namespacePrefix is not null)
        {
            yield return namespacePrefix;
            yield break;
        }

        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current.Namespace is not null)
            {
                yield return current.Namespace;
            }
        }

        for(var current = scope; current is not null; current = current.Parent)
        {
            foreach(var imported in current.ImportedNamespaces)
            {
                yield return imported;
            }
        }
    }
}
