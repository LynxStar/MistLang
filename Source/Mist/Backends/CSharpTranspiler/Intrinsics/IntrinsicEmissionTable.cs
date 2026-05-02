namespace Mist.Backends.CSharpTranspiler.Intrinsics;

/// <summary>
/// Concrete implementation of <see cref="IIntrinsicEmissionRegistry"/> that
/// populates dictionaries with emission templates for every intrinsic declared
/// by the Mist Standard Library's bootstrap tier.
///
/// <para>
/// Dictionary key formats:
/// <list type="bullet">
///   <item>Binary operators: <c>"Standard.Int32:operator+"</c></item>
///   <item>Unary operators: <c>"Standard.Int32:operator-unary"</c></item>
///   <item>Instance methods: <c>"Standard.String:Substring(Standard.Int32)"</c></item>
///   <item>Constructors: <c>"Standard.Int32:.ctor(Standard.Int32)"</c></item>
/// </list>
/// </para>
/// </summary>
internal sealed class IntrinsicEmissionTable : IIntrinsicEmissionRegistry
{
    private static readonly Dictionary<string, IntrinsicEmissionTemplate> BinaryOperators = BuildBinaryOperators();
    private static readonly Dictionary<string, IntrinsicEmissionTemplate> UnaryOperators = BuildUnaryOperators();
    private static readonly Dictionary<string, IntrinsicEmissionTemplate> InstanceMethods = BuildInstanceMethods();
    private static readonly Dictionary<string, IntrinsicEmissionTemplate> Constructors = new(StringComparer.Ordinal);

    private static Dictionary<string, IntrinsicEmissionTemplate> BuildBinaryOperators()
    {
        var dict = new Dictionary<string, IntrinsicEmissionTemplate>(StringComparer.Ordinal);
        PopulateNumericBinaryOperators(dict);
        PopulateBooleanBinaryOperators(dict);
        PopulateCharOperators(dict);
        PopulateStringOperators(dict);
        return dict;
    }

    private static Dictionary<string, IntrinsicEmissionTemplate> BuildUnaryOperators()
    {
        var dict = new Dictionary<string, IntrinsicEmissionTemplate>(StringComparer.Ordinal);
        PopulateNumericUnaryOperators(dict);
        PopulateBooleanUnaryOperators(dict);
        return dict;
    }

    private static Dictionary<string, IntrinsicEmissionTemplate> BuildInstanceMethods()
    {
        var dict = new Dictionary<string, IntrinsicEmissionTemplate>(StringComparer.Ordinal);
        PopulateInstanceMethods(dict);
        return dict;
    }

    // ── Population helpers ───────────────────────────────────────────────

    /// <summary>
    /// Registers binary arithmetic (+, -, *, /, %) and comparison (&lt;, &gt;, &lt;=, &gt;=, ==, !=)
    /// operators for all 11 numeric types.
    /// </summary>
    private static void PopulateNumericBinaryOperators(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        // All 11 numeric types
        string[] allNumericTypes =
        [
            "Standard.Int32", "Standard.UInt32", "Standard.Int64", "Standard.UInt64",
            "Standard.Int16", "Standard.UInt16", "Standard.Byte", "Standard.SByte",
            "Standard.Single", "Standard.Double", "Standard.Decimal"
        ];

        // 5 arithmetic operators
        string[] arithmeticOps = ["+", "-", "*", "/", "%"];

        // 6 comparison operators
        string[] comparisonOps = ["<", ">", "<=", ">=", "==", "!="];

        foreach (var type in allNumericTypes)
        {
            // Binary arithmetic: "{0} <op> {1}"
            foreach (var op in arithmeticOps)
            {
                dict[$"{type}:operator{op}"] = new IntrinsicEmissionTemplate($"{{0}} {op} {{1}}");
            }

            // Binary comparison: "{0} <op> {1}"
            foreach (var op in comparisonOps)
            {
                dict[$"{type}:operator{op}"] = new IntrinsicEmissionTemplate($"{{0}} {op} {{1}}");
            }
        }
    }

    /// <summary>
    /// Registers unary negation (-) for signed numeric types.
    /// UInt32, UInt64, UInt16, Byte do NOT have unary '-' (Requirement 3.4).
    /// </summary>
    private static void PopulateNumericUnaryOperators(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        string[] signedTypes =
        [
            "Standard.Int32", "Standard.Int64", "Standard.Int16", "Standard.SByte",
            "Standard.Single", "Standard.Double", "Standard.Decimal"
        ];

        foreach (var type in signedTypes)
        {
            dict[$"{type}:operator-unary"] = new IntrinsicEmissionTemplate("-{0}");
        }
    }

    /// <summary>
    /// Registers Boolean binary operators: &amp;&amp;, ||, ==, !=.
    /// </summary>
    private static void PopulateBooleanBinaryOperators(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        const string boolType = "Standard.Boolean";

        dict[$"{boolType}:operator&&"] = new IntrinsicEmissionTemplate("{0} && {1}");
        dict[$"{boolType}:operator||"] = new IntrinsicEmissionTemplate("{0} || {1}");
        dict[$"{boolType}:operator=="] = new IntrinsicEmissionTemplate("{0} == {1}");
        dict[$"{boolType}:operator!="] = new IntrinsicEmissionTemplate("{0} != {1}");
    }

    /// <summary>
    /// Registers Boolean unary operator: !.
    /// </summary>
    private static void PopulateBooleanUnaryOperators(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        const string boolType = "Standard.Boolean";
        dict[$"{boolType}:operator!unary"] = new IntrinsicEmissionTemplate("!{0}");
    }

    /// <summary>
    /// Registers Char comparison operators: ==, !=, &lt;, &gt;, &lt;=, &gt;=.
    /// </summary>
    private static void PopulateCharOperators(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        const string charType = "Standard.Char";
        string[] comparisonOps = ["==", "!=", "<", ">", "<=", ">="];

        foreach (var op in comparisonOps)
        {
            dict[$"{charType}:operator{op}"] = new IntrinsicEmissionTemplate($"{{0}} {op} {{1}}");
        }
    }

    /// <summary>
    /// Registers String operators: + (concat), == and != (equality).
    /// </summary>
    private static void PopulateStringOperators(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        const string stringType = "Standard.String";

        dict[$"{stringType}:operator+"] = new IntrinsicEmissionTemplate("{0} + {1}");
        dict[$"{stringType}:operator=="] = new IntrinsicEmissionTemplate("{0} == {1}");
        dict[$"{stringType}:operator!="] = new IntrinsicEmissionTemplate("{0} != {1}");
    }

    /// <summary>
    /// Registers intrinsic instance methods: String.Length, String.Substring,
    /// String.Contains, and Array.Length.
    /// </summary>
    private static void PopulateInstanceMethods(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        // String instance methods
        dict["Standard.String:Length()"] = new IntrinsicEmissionTemplate("{0}.Length");
        dict["Standard.String:Substring(Standard.Int32)"] = new IntrinsicEmissionTemplate("{0}.Substring({1})");
        dict["Standard.String:Contains(Standard.String)"] = new IntrinsicEmissionTemplate("{0}.Contains({1})");

        // Array instance methods — keyed as "Standard.Array" (generic but the
        // key works with just the base type name)
        dict["Standard.Array:Length()"] = new IntrinsicEmissionTemplate("{0}.Length");
    }

    // ── IIntrinsicEmissionRegistry implementation ────────────────────────

    /// <inheritdoc />
    public IntrinsicEmissionTemplate? TryGetBinaryOperator(string declaringType, string op)
    {
        var key = $"{declaringType}:operator{op}";
        return BinaryOperators.TryGetValue(key, out var template) ? template : null;
    }

    /// <inheritdoc />
    public IntrinsicEmissionTemplate? TryGetUnaryOperator(string declaringType, string op)
    {
        var key = $"{declaringType}:operator{op}unary";
        return UnaryOperators.TryGetValue(key, out var template) ? template : null;
    }

    /// <inheritdoc />
    public IntrinsicEmissionTemplate? TryGetInstanceMethod(string declaringType, string method, IReadOnlyList<string> paramTypes)
    {
        var paramList = paramTypes.Count > 0 ? string.Join(",", paramTypes) : "";
        var key = $"{declaringType}:{method}({paramList})";
        return InstanceMethods.TryGetValue(key, out var template) ? template : null;
    }

    /// <inheritdoc />
    public IntrinsicEmissionTemplate? TryGetConstructor(string declaringType, IReadOnlyList<string> paramTypes)
    {
        var paramList = paramTypes.Count > 0 ? string.Join(",", paramTypes) : "";
        var key = $"{declaringType}:.ctor({paramList})";
        return Constructors.TryGetValue(key, out var template) ? template : null;
    }

    /// <inheritdoc />
    public IntrinsicEmissionTemplate? TryGetCastOperator(string declaringType, string targetType)
    {
        // No intrinsic cast operators are registered yet — the stdlib can add
        // numeric conversion operators in a future iteration.
        return null;
    }
}
