using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Implements the shared flat-to-bracketed binding rule (Requirement 4 and the design's
/// "Value-Parameter Arity and the Shared Binding Rule"). Given a declared slot list with
/// <em>at most one</em> <see cref="BindingSlotKind.UnbracketedPack"/> and a flat use-site
/// argument list, it binds the fixed scalar and <see cref="BindingSlotKind.BracketedPack"/>
/// slots one-to-one inward from both ends and assigns the remaining contiguous middle
/// arguments (possibly zero) to the single unbracketed pack.
///
/// <para>
/// The rule is deliberately kind-driven and free of any model dependency beyond
/// <see cref="IMistType"/>, so the same algorithm serves both sides of the design's
/// unifying principle:
/// <list type="bullet">
///   <item>the <b>type</b> side — fixed scalar slots, bracketed <c>Type[]:</c> packs, and
///     the single <c>params</c> pack of a type-parameter list; and</item>
///   <item>the <b>value</b> side — fixed value parameters and the single value pack
///     <c>params TArgs args</c>, whose contiguous middle collects into <c>(TArgs...)</c>.</item>
/// </list>
/// A caller interns the per-slot element lists (into a lifted <c>Type[]</c> for a pack slot,
/// or a single scalar type for a fixed slot); the binder itself performs no interning, so it
/// is a pure function of its inputs.
/// </para>
///
/// <para>
/// Both surface forms of a pack denote the same binding: a flat run of scalar middle
/// arguments (for example <c>Foo&lt;int, string&gt;</c>) and a single bracketed group in the
/// pack's position (for example <c>Foo&lt;[int, string]&gt;</c>) both yield the ordered
/// element list <c>[int, string]</c>, so the caller interns them to the same type
/// (Requirement 4.4). An empty middle run (or an empty bracketed group) yields the empty
/// pack (Requirement 4.3).
/// </para>
/// </summary>
internal static class PackBinder
{
    /// <summary>
    /// Binds <paramref name="arguments"/> to <paramref name="slots"/> per the binding rule.
    /// Returns <see cref="PackBindingStatus.TooFewArguments"/> when the flat argument list is
    /// shorter than the number of fixed and bracketed slots (the minimum required while the
    /// unbracketed pack absorbs zero arguments), which the caller reports as
    /// <c>MIST_VAR_0005</c>. Otherwise returns one <see cref="SlotBinding"/> per declared slot,
    /// in declaration order.
    /// </summary>
    public static PackBindingResult Bind(
        IReadOnlyList<BindingSlotKind> slots,
        IReadOnlyList<UseSiteArgument> arguments)
    {
        // The variadic sink is the list's first (and, when well-formed, only) unbracketed
        // pack. A surplus unbracketed pack is already reported as MIST_VAR_0001 during model
        // construction; here every non-sink slot binds as a single arity-1 slot, so a
        // malformed list still yields a total, deterministic assignment.
        var packIndex = FindUnbracketedPackIndex(slots);

        var leadingCount = packIndex >= 0 ? packIndex : slots.Count;
        var trailingCount = packIndex >= 0 ? slots.Count - packIndex - 1 : 0;

        // Requirement 4.6: a flat argument list that cannot fill every fixed and bracketed
        // slot (with the pack empty) is too short to bind.
        var minimumRequired = leadingCount + trailingCount;
        if(arguments.Count < minimumRequired)
        {
            return PackBindingResult.TooFew();
        }

        var bindings = new SlotBinding[slots.Count];

        // Leading fixed/bracketed slots take the leading arguments one-to-one, in order.
        for(var i = 0; i < leadingCount; i++)
        {
            bindings[i] = BindArityOneSlot(slots[i], arguments[i]);
        }

        // Trailing fixed/bracketed slots take the trailing arguments one-to-one, in order.
        for(var j = 0; j < trailingCount; j++)
        {
            var slotIndex = packIndex + 1 + j;
            var argumentIndex = arguments.Count - trailingCount + j;
            bindings[slotIndex] = BindArityOneSlot(slots[slotIndex], arguments[argumentIndex]);
        }

        // The single unbracketed pack absorbs the contiguous middle (possibly empty).
        if(packIndex >= 0)
        {
            var middle = Slice(arguments, leadingCount, arguments.Count - trailingCount);
            bindings[packIndex] = new SlotBinding(isPack: true, CollectPackElements(middle));
        }

        return PackBindingResult.Bound(bindings);
    }

    /// <summary>
    /// Binds a single arity-1 slot (fixed scalar or bracketed pack) to its one use-site
    /// argument. A fixed slot carries the argument's element(s) verbatim (a well-formed fixed
    /// argument is a single scalar); a bracketed pack slot is marked as a pack whose elements
    /// are the group's element types (a lone scalar written without brackets is tolerated as a
    /// one-element pack).
    /// </summary>
    private static SlotBinding BindArityOneSlot(BindingSlotKind slot, UseSiteArgument argument)
    {
        var isPack = slot != BindingSlotKind.Fixed;
        return new SlotBinding(isPack, argument.Types);
    }

    /// <summary>
    /// Collects the ordered element types the unbracketed pack binds from its contiguous
    /// middle run. A middle that is exactly one bracketed group is the pack's explicit
    /// bracketed spelling, so its element types are used directly; otherwise the flat scalar
    /// middle arguments contribute their types in order. Both spellings therefore produce the
    /// same ordered element list, so the caller interns them to the same type.
    /// </summary>
    private static IReadOnlyList<IMistType> CollectPackElements(IReadOnlyList<UseSiteArgument> middle)
    {
        if(middle.Count == 1 && middle[0].IsBracketedGroup)
        {
            return middle[0].Types;
        }

        var elements = new List<IMistType>(middle.Count);
        foreach(var argument in middle)
        {
            elements.AddRange(argument.Types);
        }

        return elements;
    }

    /// <summary>
    /// Returns the index of the first <see cref="BindingSlotKind.UnbracketedPack"/> slot, or
    /// <c>-1</c> when the list contains none (an all-arity-1 list, for example a construction
    /// of only fixed scalars and bracketed packs).
    /// </summary>
    private static int FindUnbracketedPackIndex(IReadOnlyList<BindingSlotKind> slots)
    {
        for(var i = 0; i < slots.Count; i++)
        {
            if(slots[i] == BindingSlotKind.UnbracketedPack)
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<UseSiteArgument> Slice(
        IReadOnlyList<UseSiteArgument> source, int start, int end)
    {
        var slice = new List<UseSiteArgument>(Math.Max(0, end - start));
        for(var i = start; i < end; i++)
        {
            slice.Add(source[i]);
        }

        return slice;
    }
}

/// <summary>
/// The binding role of a declared slot. Fixed scalar slots and bracketed packs each consume
/// exactly one use-site argument (binding inward from both ends); the single unbracketed pack
/// absorbs the contiguous middle.
/// </summary>
internal enum BindingSlotKind
{
    /// <summary>An ordinary scalar slot (a regular or dependent non-pack type parameter, or a
    /// fixed value parameter). Consumes exactly one use-site argument.</summary>
    Fixed,

    /// <summary>An explicit <c>Type[]:</c> pack supplied in brackets at the use site. Consumes
    /// exactly one use-site argument (a bracketed group).</summary>
    BracketedPack,

    /// <summary>The list's single bracket-free <c>params</c> pack. Absorbs the contiguous
    /// middle arguments (possibly zero).</summary>
    UnbracketedPack,
}

/// <summary>
/// A resolved use-site argument: either a single scalar type (for example the <c>int</c> in
/// <c>Foo&lt;int, string&gt;</c>) or a bracketed group (for example the <c>[int, string]</c>
/// in <c>Foo&lt;[int, string]&gt;</c>, including the empty group <c>[]</c>).
/// </summary>
internal readonly struct UseSiteArgument
{
    private UseSiteArgument(bool isBracketedGroup, IReadOnlyList<IMistType> types)
    {
        IsBracketedGroup = isBracketedGroup;
        Types = types;
    }

    /// <summary>True when this argument was written as a bracketed group <c>[...]</c>.</summary>
    public bool IsBracketedGroup { get; }

    /// <summary>The argument's element types: a single element for a scalar, or the group's
    /// ordered elements (possibly zero) for a bracketed group.</summary>
    public IReadOnlyList<IMistType> Types { get; }

    /// <summary>Creates a scalar argument carrying a single type.</summary>
    public static UseSiteArgument Scalar(IMistType type)
    {
        return new UseSiteArgument(isBracketedGroup: false, new[] { type });
    }

    /// <summary>Creates a bracketed-group argument carrying the group's ordered element types.</summary>
    public static UseSiteArgument Group(IReadOnlyList<IMistType> types)
    {
        return new UseSiteArgument(isBracketedGroup: true, types);
    }
}

/// <summary>
/// The binding of one declared slot: an ordered element list plus whether the slot is a pack.
/// A fixed slot carries a single scalar element; a pack slot carries the ordered pack elements
/// (which the caller interns into a lifted <c>Type[]</c>).
/// </summary>
internal readonly struct SlotBinding
{
    public SlotBinding(bool isPack, IReadOnlyList<IMistType> elements)
    {
        IsPack = isPack;
        Elements = elements;
    }

    /// <summary>True when this slot is a pack (bracketed or unbracketed); false for a fixed
    /// scalar slot.</summary>
    public bool IsPack { get; }

    /// <summary>The ordered element types bound to this slot. For a fixed slot this is a single
    /// scalar type; for a pack slot it is the ordered pack elements (possibly empty).</summary>
    public IReadOnlyList<IMistType> Elements { get; }
}

/// <summary>Whether <see cref="PackBinder.Bind"/> produced a binding or rejected the argument
/// list as too short.</summary>
internal enum PackBindingStatus
{
    /// <summary>Every slot was bound.</summary>
    Bound,

    /// <summary>The flat argument list was too short to fill the fixed and bracketed slots
    /// (reported as <c>MIST_VAR_0005</c>).</summary>
    TooFewArguments,
}

/// <summary>The outcome of the binding rule: a status and, when bound, one
/// <see cref="SlotBinding"/> per declared slot in declaration order.</summary>
internal readonly struct PackBindingResult
{
    private PackBindingResult(PackBindingStatus status, IReadOnlyList<SlotBinding> slots)
    {
        Status = status;
        Slots = slots;
    }

    public PackBindingStatus Status { get; }

    public IReadOnlyList<SlotBinding> Slots { get; }

    public static PackBindingResult Bound(IReadOnlyList<SlotBinding> slots)
    {
        return new PackBindingResult(PackBindingStatus.Bound, slots);
    }

    public static PackBindingResult TooFew()
    {
        return new PackBindingResult(PackBindingStatus.TooFewArguments, Array.Empty<SlotBinding>());
    }
}