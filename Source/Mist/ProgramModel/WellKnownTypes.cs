namespace Mist.ProgramModel;

/// <summary>
/// A well-known Mist type: carries the simple name, namespace, keyword alias, and
/// fully qualified name in one place. Every reference to a built-in type throughout
/// the compiler flows through these constants rather than repeating magic strings.
/// </summary>
internal sealed record WellKnownType(string Name, string Namespace, string? Keyword)
{
    /// <summary>
    /// The fully qualified name (e.g. "Standard.Int32"). Used as the registry key
    /// in <see cref="Model.ProgramModel.TypesByFQN"/>.
    /// </summary>
    public string FullyQualifiedName { get; } = $"{Namespace}.{Name}";
}

/// <summary>
/// Single source of truth for every well-known Mist type. All magic strings for
/// primitive aliases, delegates, interfaces, and structural types live here and
/// nowhere else.
/// </summary>
internal static class WellKnownTypes
{
    // ── Numeric primitives ────────────────────────────────────────────────
    public static readonly WellKnownType Int32 = new("Int32", "Standard", "int");
    public static readonly WellKnownType Int64 = new("Int64", "Standard", "long");
    public static readonly WellKnownType Int16 = new("Int16", "Standard", "short");
    public static readonly WellKnownType UInt32 = new("UInt32", "Standard", "uint");
    public static readonly WellKnownType UInt64 = new("UInt64", "Standard", "ulong");
    public static readonly WellKnownType UInt16 = new("UInt16", "Standard", "ushort");
    public static readonly WellKnownType Byte = new("Byte", "Standard", "byte");
    public static readonly WellKnownType SByte = new("SByte", "Standard", "sbyte");
    public static readonly WellKnownType Single = new("Single", "Standard", "float");
    public static readonly WellKnownType Double = new("Double", "Standard", "double");
    public static readonly WellKnownType Decimal = new("Decimal", "Standard", "decimal");

    // ── Non-numeric primitives ────────────────────────────────────────────
    public static readonly WellKnownType Boolean = new("Boolean", "Standard", "bool");
    public static readonly WellKnownType Char = new("Char", "Standard", "char");
    public static readonly WellKnownType String = new("String", "Standard", "string");
    public static readonly WellKnownType Regex = new("Regex", "Standard", "regex");

    // ── Structural types ──────────────────────────────────────────────────
    public static readonly WellKnownType Void = new("Void", "Standard", "void");
    public static readonly WellKnownType Task = new("Task", "Standard", Keyword: null);
    public static readonly WellKnownType TaskGeneric = new("Task<>", "Standard", Keyword: null);
    public static readonly WellKnownType ValueTask = new("ValueTask", "Standard", Keyword: null);
    public static readonly WellKnownType ValueTaskGeneric = new("ValueTask<>", "Standard", Keyword: null);
    public static readonly WellKnownType Array = new("Array<,>", "Standard", Keyword: null);
    public static readonly WellKnownType IEnumerable = new("IEnumerable<>", "Standard", Keyword: null);
    public static readonly WellKnownType IEnumerator = new("IEnumerator<>", "Standard", Keyword: null);

    /// <summary>
    /// All primitive types that have a keyword alias (the ones the
    /// <see cref="Construction.ScopeResolver"/> maps from syntax).
    /// </summary>
    public static readonly IReadOnlyList<WellKnownType> PrimitiveTypes =
    [
        Int32, Int64, Int16, UInt32, UInt64, UInt16,
        Byte, SByte, Single, Double, Decimal,
        Boolean, Char, String, Regex,
    ];

    /// <summary>
    /// All well-known types that the Standard Library must provide as shells for the
    /// program model to function (registered during ambient provider initialization
    /// or in test helpers).
    /// </summary>
    public static readonly IReadOnlyList<WellKnownType> AllStandardTypes =
    [
        Int32, Int64, Int16, UInt32, UInt64, UInt16,
        Byte, SByte, Single, Double, Decimal,
        Boolean, Char, String, Regex, Void,
        Task, TaskGeneric, ValueTask, ValueTaskGeneric,
    ];
}