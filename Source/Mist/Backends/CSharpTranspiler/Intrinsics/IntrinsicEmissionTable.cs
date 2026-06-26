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
    private static readonly Dictionary<string, IntrinsicEmissionTemplate> StaticMethods = BuildStaticMethods();
    private static readonly Dictionary<string, IntrinsicEmissionTemplate> CastOperators = BuildCastOperators();
    private static readonly Dictionary<string, IntrinsicEmissionTemplate> Constructors = BuildConstructors();

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

    private static Dictionary<string, IntrinsicEmissionTemplate> BuildStaticMethods()
    {
        var dict = new Dictionary<string, IntrinsicEmissionTemplate>(StringComparer.Ordinal);
        PopulateStaticMethods(dict);
        return dict;
    }

    private static Dictionary<string, IntrinsicEmissionTemplate> BuildCastOperators()
    {
        var dict = new Dictionary<string, IntrinsicEmissionTemplate>(StringComparer.Ordinal);
        PopulateCastOperators(dict);
        return dict;
    }

    private static Dictionary<string, IntrinsicEmissionTemplate> BuildConstructors()
    {
        var dict = new Dictionary<string, IntrinsicEmissionTemplate>(StringComparer.Ordinal);
        PopulateConstructors(dict);
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

        foreach(var type in allNumericTypes)
        {
            // Binary arithmetic: "{0} <op> {1}"
            foreach(var op in arithmeticOps)
            {
                dict[$"{type}:operator{op}"] = new IntrinsicEmissionTemplate($"{{0}} {op} {{1}}");
            }

            // Binary comparison: "{0} <op> {1}"
            foreach(var op in comparisonOps)
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

        foreach(var type in signedTypes)
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

        foreach(var op in comparisonOps)
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
    /// Registers intrinsic instance methods: String.Substring, String.Contains,
    /// and intrinsic property emission templates: String.Length, Array.Length.
    /// Properties use the same zero-arg key format for lookup by the emitter's
    /// MemberAccessSuffix path.
    /// </summary>
    private static void PopulateInstanceMethods(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        // String instance properties (accessed via MemberAccessSuffix emitter path)
        dict["Standard.String:Length()"] = new IntrinsicEmissionTemplate("{0}.Length");

        // String instance methods
        dict["Standard.String:Substring(Standard.Int32)"] = new IntrinsicEmissionTemplate("{0}.Substring({1})");
        dict["Standard.String:Contains(Standard.String)"] = new IntrinsicEmissionTemplate("{0}.Contains({1})");

        // Array instance properties (accessed via MemberAccessSuffix emitter path)
        dict["Standard.Array:Length()"] = new IntrinsicEmissionTemplate("{0}.Length");

        // Type instance members
        dict["Standard.Type:Name()"] = new IntrinsicEmissionTemplate("{0}.Name");

        // Regex instance methods
        dict["Standard.Regex:IsMatch(Standard.String)"] = new IntrinsicEmissionTemplate("{0}.IsMatch({1})");
        dict["Standard.Regex:IsFullMatch(Standard.String)"] = new IntrinsicEmissionTemplate(
            "System.Text.RegularExpressions.Regex.IsMatch({1}, \"^(?:\" + {0}.ToString() + \")$\")");
        dict["Standard.Regex:Pattern()"] = new IntrinsicEmissionTemplate("{0}.ToString()");

        // IClonable<T>.Clone() intrinsics for bootstrap types (Req 8 AC 6, task 12.1).
        // Value types: Clone() is an identity copy (the value is already independent).
        // String: immutable reference, Clone() returns the same reference (observationally independent).
        dict["Standard.Int32:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.Int64:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.Int16:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.UInt32:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.UInt64:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.UInt16:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.Byte:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.SByte:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.Single:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.Double:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.Decimal:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.Boolean:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.Char:Clone()"] = new IntrinsicEmissionTemplate("{0}");
        dict["Standard.String:Clone()"] = new IntrinsicEmissionTemplate("{0}");
    }

    /// <summary>
    /// Registers intrinsic static methods: Type.Of(unknown) → value.GetType().
    /// </summary>
    private static void PopulateStaticMethods(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        // Type.Of(value) lowers to value.GetType() in C#.
        // {0} is the first argument (the unknown value).
        dict["Standard.Type:Of(unknown)"] = new IntrinsicEmissionTemplate("{0}.GetType()");
    }

    /// <summary>
    /// Registers intrinsic cast operators: Regex implicit→string, Regex explicit←string.
    /// Key format: <c>"{DeclaringType}:{implicit|explicit}({TargetType})"</c> where
    /// TargetType is the C# emitted type name.
    /// </summary>
    private static void PopulateCastOperators(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        // Regex → string (implicit): extracts the pattern via .ToString()
        dict["Standard.Regex:implicit(string)"] = new IntrinsicEmissionTemplate("{0}.ToString()");

        // String → Regex (explicit): constructs a compiled Regex instance
        dict["Standard.Regex:explicit(System.Text.RegularExpressions.Regex)"] = new IntrinsicEmissionTemplate(
            "new System.Text.RegularExpressions.Regex({0}, System.Text.RegularExpressions.RegexOptions.Compiled)");
    }

    /// <summary>
    /// Registers intrinsic constructors: Regex(string pattern).
    /// </summary>
    private static void PopulateConstructors(Dictionary<string, IntrinsicEmissionTemplate> dict)
    {
        // Regex constructor: new Regex(pattern, RegexOptions.Compiled)
        dict["Standard.Regex:.ctor(Standard.String)"] = new IntrinsicEmissionTemplate(
            "new System.Text.RegularExpressions.Regex({0}, System.Text.RegularExpressions.RegexOptions.Compiled)");

        // Span/ReadOnlySpan constructors: new Span<T>(array) → new System.Span<T>(array)
        // These are intrinsic constructors that take an Array<T, unknown> and create a span.
        // The emitter resolves the actual element type from context.
        dict["Standard.Span:.ctor(Standard.Array)"] = new IntrinsicEmissionTemplate(
            "new System.Span<{T}>({0})");
        dict["Standard.ReadOnlySpan:.ctor(Standard.Array)"] = new IntrinsicEmissionTemplate(
            "new System.ReadOnlySpan<{T}>({0})");
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
        // Try implicit first, then explicit.
        var implicitKey = $"{declaringType}:implicit({targetType})";
        if(CastOperators.TryGetValue(implicitKey, out var implicitTemplate))
        {
            return implicitTemplate;
        }

        var explicitKey = $"{declaringType}:explicit({targetType})";
        return CastOperators.TryGetValue(explicitKey, out var explicitTemplate) ? explicitTemplate : null;
    }

    /// <inheritdoc />
    public IntrinsicEmissionTemplate? TryGetStaticMethod(string declaringType, string method, IReadOnlyList<string> paramTypes)
    {
        var paramList = paramTypes.Count > 0 ? string.Join(",", paramTypes) : "";
        var key = $"{declaringType}:{method}({paramList})";
        return StaticMethods.TryGetValue(key, out var template) ? template : null;
    }

    /// <inheritdoc />
    public bool HasInstanceMethods(string declaringType)
    {
        var prefix = $"{declaringType}:";
        foreach(var key in InstanceMethods.Keys)
        {
            if(key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}