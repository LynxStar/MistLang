using System.Globalization;
using System.Text;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Tracks unique (type, value) pairs used as dependent type arguments across the program
/// and generates carrier structs for each. A carrier struct implements the corresponding
/// <c>IConst{Type}</c> interface and exposes the value via a static property.
/// </summary>
/// <remarks>
/// Naming convention: <c>Const_{Type}_{Value}</c> where:
/// <list type="bullet">
///   <item>Type is the C# keyword capitalized (Int, Uint, Long, Bool, Char, etc.)</item>
///   <item>Value is the literal representation with special handling:
///     <list type="bullet">
///       <item>Negative numbers: <c>Neg</c> prefix (e.g., <c>Const_Int_Neg1</c>)</item>
///       <item>Boolean: <c>True</c> / <c>False</c></item>
///       <item>Char: the character itself for alphanumeric, hex code for others (e.g., <c>Const_Char_X</c>, <c>Const_Char_0x20</c>)</item>
///     </list>
///   </item>
/// </list>
/// </remarks>
internal sealed class CarrierStructRegistry
{
    /// <summary>
    /// Maps a C# keyword to its capitalized form for struct/interface naming.
    /// </summary>
    private static readonly Dictionary<string, string> KeywordToCapitalized = new(StringComparer.Ordinal)
    {
        ["int"] = "Int",
        ["uint"] = "Uint",
        ["long"] = "Long",
        ["ulong"] = "Ulong",
        ["short"] = "Short",
        ["ushort"] = "Ushort",
        ["byte"] = "Byte",
        ["sbyte"] = "Sbyte",
        ["bool"] = "Bool",
        ["char"] = "Char",
    };

    /// <summary>
    /// Represents a registered carrier struct entry.
    /// </summary>
    internal readonly record struct CarrierEntry(string StructName, string InterfaceName, string Keyword, string LiteralValue);

    /// <summary>
    /// The set of registered carrier entries, keyed by struct name for deduplication.
    /// </summary>
    private readonly Dictionary<string, CarrierEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets all registered carrier entries in a stable (alphabetical) order.
    /// </summary>
    public IReadOnlyList<CarrierEntry> Entries =>
        _entries.Values.OrderBy(e => e.StructName, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Registers a dependent value argument and returns the carrier struct name.
    /// If the (type, value) pair is already registered, returns the existing struct name.
    /// Returns <c>null</c> if the argument represents an open (forwarded) parameter
    /// that should not generate a carrier struct.
    /// </summary>
    public string? Register(DependentValueArgument depArg)
    {
        var keyword = CSharpTypeMap.GetKeyword(depArg.ValueType);
        if (!KeywordToCapitalized.TryGetValue(keyword, out var capitalized))
            return null;

        // Open/forwarded parameters have a string value (the parameter name) — skip them.
        if (depArg.Value is string)
            return null;

        var valueSuffix = FormatValueSuffix(depArg.Value, keyword);
        var structName = $"Const_{capitalized}_{valueSuffix}";
        var interfaceName = $"IConst{capitalized}";
        var literalValue = FormatLiteral(depArg.Value, keyword);

        _entries.TryAdd(structName, new CarrierEntry(structName, interfaceName, keyword, literalValue));
        return structName;
    }

    /// <summary>
    /// Gets the carrier struct name for a given dependent value argument without registering it.
    /// Returns <c>null</c> if the argument represents an open (forwarded) parameter.
    /// </summary>
    public static string? GetStructName(DependentValueArgument depArg)
    {
        var keyword = CSharpTypeMap.GetKeyword(depArg.ValueType);
        if (!KeywordToCapitalized.TryGetValue(keyword, out var capitalized))
            return null;

        // Open/forwarded parameters have a string value (the parameter name) — skip them.
        if (depArg.Value is string)
            return null;

        var valueSuffix = FormatValueSuffix(depArg.Value, keyword);
        return $"Const_{capitalized}_{valueSuffix}";
    }

    /// <summary>
    /// Collects all unique (type, value) pairs from the semantic model by scanning
    /// all symbols and expression types for <see cref="ConstructedType"/> instances
    /// with non-null <see cref="ConstructedType.DependentArguments"/>.
    /// </summary>
    public static CarrierStructRegistry CollectFromModel(ISemanticModel model)
    {
        var registry = new CarrierStructRegistry();

        // Scan all symbols' declared types, base types, and interfaces.
        foreach (var symbol in model.AllSymbols)
        {
            CollectFromType(symbol.DeclaredType, registry);

            // For type symbols, scan base types, interfaces, and member scope.
            if (symbol.DeclaredType is ClassType ct)
            {
                CollectFromType(ct.BaseType, registry);
                foreach (var iface in ct.Interfaces)
                    CollectFromType(iface, registry);
                foreach (var member in ct.MemberScope.Members)
                    CollectFromType(member.DeclaredType, registry);
            }
            else if (symbol.DeclaredType is StructType st)
            {
                foreach (var iface in st.Interfaces)
                    CollectFromType(iface, registry);
                foreach (var member in st.MemberScope.Members)
                    CollectFromType(member.DeclaredType, registry);
            }
            else if (symbol.DeclaredType is InterfaceType it)
            {
                foreach (var iface in it.Interfaces)
                    CollectFromType(iface, registry);
                foreach (var member in it.MemberScope.Members)
                    CollectFromType(member.DeclaredType, registry);
            }
        }

        // Scan expression types (populated by Pass 2) for constructed types with dependent args.
        // This captures usages like `new FixedBuffer<5>()` where the constructed type is the
        // expression type but not necessarily stored as a symbol's DeclaredType.
        if (model is SemanticModel concreteModel)
        {
            foreach (var (_, type) in concreteModel.ExpressionTypes)
            {
                CollectFromType(type, registry);
            }
        }

        return registry;
    }

    /// <summary>
    /// Recursively scans a type for <see cref="ConstructedType"/> instances with dependent arguments.
    /// </summary>
    private static void CollectFromType(IType? type, CarrierStructRegistry registry)
    {
        if (type is ConstructedType ct && ct.DependentArguments is not null)
        {
            foreach (var depArg in ct.DependentArguments)
            {
                if (depArg is not null)
                {
                    registry.Register(depArg);
                }
            }
        }
    }

    /// <summary>
    /// Formats the value suffix for the carrier struct name.
    /// </summary>
    private static string FormatValueSuffix(object value, string keyword)
    {
        return value switch
        {
            long l when l < 0 => $"Neg{(-l).ToString(CultureInfo.InvariantCulture)}",
            long l => l.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "True" : "False",
            char c => FormatCharSuffix(c),
            _ => value.ToString() ?? "0",
        };
    }

    /// <summary>
    /// Formats a char value for the struct name suffix.
    /// Alphanumeric characters use their literal form; others use hex code.
    /// </summary>
    private static string FormatCharSuffix(char c)
    {
        if (char.IsLetterOrDigit(c))
            return c.ToString();
        return $"0x{((int)c).ToString("X2", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Formats the literal value for the carrier struct's <c>Value</c> property body.
    /// </summary>
    private static string FormatLiteral(object value, string keyword)
    {
        return value switch
        {
            long l when keyword == "int" => l.ToString(CultureInfo.InvariantCulture),
            long l when keyword == "uint" => $"{l.ToString(CultureInfo.InvariantCulture)}u",
            long l when keyword == "long" => $"{l.ToString(CultureInfo.InvariantCulture)}L",
            long l when keyword == "ulong" => $"{l.ToString(CultureInfo.InvariantCulture)}UL",
            long l when keyword == "short" => $"(short){l.ToString(CultureInfo.InvariantCulture)}",
            long l when keyword == "ushort" => $"(ushort){l.ToString(CultureInfo.InvariantCulture)}",
            long l when keyword == "byte" => $"(byte){l.ToString(CultureInfo.InvariantCulture)}",
            long l when keyword == "sbyte" => $"(sbyte){l.ToString(CultureInfo.InvariantCulture)}",
            bool b => b ? "true" : "false",
            char c => $"'{EscapeChar(c)}'",
            _ => value.ToString() ?? "0",
        };
    }

    /// <summary>
    /// Escapes a character for use in a C# char literal.
    /// </summary>
    private static string EscapeChar(char c)
    {
        return c switch
        {
            '\'' => @"\'",
            '\\' => @"\\",
            '\0' => @"\0",
            '\a' => @"\a",
            '\b' => @"\b",
            '\f' => @"\f",
            '\n' => @"\n",
            '\r' => @"\r",
            '\t' => @"\t",
            '\v' => @"\v",
            _ when char.IsControl(c) => $@"\u{(int)c:X4}",
            _ => c.ToString(),
        };
    }

    /// <summary>
    /// Generates the carrier struct declarations as a string to be appended to the helpers file.
    /// </summary>
    public string GenerateCarrierStructs()
    {
        if (_entries.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        foreach (var entry in Entries)
        {
            sb.AppendLine("[System.Runtime.CompilerServices.CompilerGenerated]");
            sb.AppendLine($"public struct {entry.StructName} : {entry.InterfaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static {entry.Keyword} Value => {entry.LiteralValue};");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
