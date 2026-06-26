using System.Globalization;
using System.Text;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Storm.Runtime;

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
    private readonly Dictionary<string, CarrierEntry> entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets all registered carrier entries in a stable (alphabetical) order.
    /// </summary>
    public IReadOnlyList<CarrierEntry> Entries =>
        entries.Values.OrderBy(e => e.StructName, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Registers a dependent value by keyword and value directly, without needing an <see cref="IType"/>.
    /// Used when scanning AST nodes where the value type is known from the literal kind.
    /// Returns the carrier struct name, or <c>null</c> if the keyword is not recognized.
    /// </summary>
    public string? RegisterByKeyword(string keyword, object value)
    {
        if(!KeywordToCapitalized.TryGetValue(keyword, out var capitalized))
        {
            return null;
        }

        if(value is string)
        {
            return null;
        }

        var valueSuffix = FormatValueSuffix(value, keyword);
        var structName = $"Const_{capitalized}_{valueSuffix}";
        var interfaceName = $"IConst{capitalized}";
        var literalValue = FormatLiteral(value, keyword);

        _ = entries.TryAdd(structName, new CarrierEntry(structName, interfaceName, keyword, literalValue));
        return structName;
    }

    /// <summary>
    /// Registers a dependent value argument and returns the carrier struct name.
    /// If the (type, value) pair is already registered, returns the existing struct name.
    /// Returns <c>null</c> if the argument represents an open (forwarded) parameter
    /// that should not generate a carrier struct.
    /// </summary>
    public string? Register(DependentValueArgument depArg)
    {
        var keyword = CSharpTypeMap.GetKeyword(depArg.ValueType);
        if(!KeywordToCapitalized.TryGetValue(keyword, out var capitalized))
        {
            return null;
        }

        // Open/forwarded parameters have a string value (the parameter name) — skip them.
        if(depArg.Value is string)
        {
            return null;
        }

        var valueSuffix = FormatValueSuffix(depArg.Value, keyword);
        var structName = $"Const_{capitalized}_{valueSuffix}";
        var interfaceName = $"IConst{capitalized}";
        var literalValue = FormatLiteral(depArg.Value, keyword);

        _ = entries.TryAdd(structName, new CarrierEntry(structName, interfaceName, keyword, literalValue));
        return structName;
    }

    /// <summary>
    /// Gets the carrier struct name for a given dependent value argument without registering it.
    /// Returns <c>null</c> if the argument represents an open (forwarded) parameter.
    /// </summary>
    public static string? GetStructName(DependentValueArgument depArg)
    {
        var keyword = CSharpTypeMap.GetKeyword(depArg.ValueType);
        if(!KeywordToCapitalized.TryGetValue(keyword, out var capitalized))
        {
            return null;
        }

        // Open/forwarded parameters have a string value (the parameter name) — skip them.
        if(depArg.Value is string)
        {
            return null;
        }

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
        foreach(var symbol in model.AllSymbols)
        {
            CollectFromType(symbol.DeclaredType, registry);

            // For type symbols, scan base types, interfaces, and member scope.
            if(symbol.DeclaredType is ClassType ct)
            {
                CollectFromType(ct.BaseType, registry);
                foreach(var iface in ct.Abstracts)
                {
                    CollectFromType(iface, registry);
                }

                foreach(var member in ct.MemberScope.Members)
                {
                    CollectFromType(member.DeclaredType, registry);
                }
            }
            else if(symbol.DeclaredType is StructType st)
            {
                foreach(var iface in st.Abstracts)
                {
                    CollectFromType(iface, registry);
                }

                foreach(var member in st.MemberScope.Members)
                {
                    CollectFromType(member.DeclaredType, registry);
                }
            }
            else if(symbol.DeclaredType is AbstractType it)
            {
                foreach(var iface in it.Abstracts)
                {
                    CollectFromType(iface, registry);
                }

                foreach(var member in it.MemberScope.Members)
                {
                    CollectFromType(member.DeclaredType, registry);
                }
            }
        }

        // Scan expression types (populated by Pass 2) for constructed types with dependent args.
        // This captures usages like `new FixedBuffer<5>()` where the constructed type is the
        // expression type but not necessarily stored as a symbol's DeclaredType.
        if(model is SemanticModel concreteModel)
        {
            foreach(var (_, type) in concreteModel.ExpressionTypes)
            {
                CollectFromType(type, registry);
            }

            // Scan ASTs for literal type arguments that may not be captured in expression types
            // (e.g., method calls with dependent type arguments like `Repeat<3>("hello")`).
            foreach(var ast in concreteModel.Asts)
            {
                if(ast is not null)
                {
                    CollectFromAst(ast, registry);
                }
            }
        }

        return registry;
    }

    /// <summary>
    /// Recursively walks an AST node tree looking for literal type arguments
    /// (boolean, char) and registers carrier structs for them.
    /// Integer literals are NOT registered here because their carrier type depends
    /// on the target type parameter (int, uint, long, etc.) which is only known
    /// from the semantic model. The semantic model scan handles integer literals correctly.
    /// This method only handles unambiguous literal types (bool, char).
    /// </summary>
    private static void CollectFromAst(SyntaxNode node, CarrierStructRegistry registry)
    {
        // Check if this node is a literal type argument and register it.
        // Only register bool and char — integer literals need type context.
        switch(node)
        {
            case TrueLiteralTypeArgumentTypeArgumentNode:
                RegisterBoolLiteral(true, registry);
                return;
            case FalseLiteralTypeArgumentTypeArgumentNode:
                RegisterBoolLiteral(false, registry);
                return;
            case CharLiteralTypeArgumentTypeArgumentNode charLit:
                RegisterCharLiteral(charLit.Literal.Lexeme, registry);
                return;
            // Skip integer/hex/binary/negated literals — their type depends on context.
            case IntegerLiteralTypeArgumentTypeArgumentNode:
            case NegatedIntegerTypeArgumentTypeArgumentNode:
            case HexLiteralTypeArgumentTypeArgumentNode:
            case BinaryLiteralTypeArgumentTypeArgumentNode:
                // These are handled by the semantic model scan which knows the correct type.
                // For method calls, we register them below by checking the method's type params.
                break;
        }

        // For method calls with type arguments, check if any are integer literals
        // in dependent type parameter positions and register with the correct type.
        if(node is MethodCallSuffixPostfixSuffixNode methodCall && methodCall.TypeArgs is not null)
        {
            RegisterMethodCallLiterals(methodCall, registry);
            // Still recurse into children for nested expressions.
        }

        // Recurse into children.
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CollectFromAst(child, registry);
            }
        }
    }

    /// <summary>
    /// Registers carrier structs for literal type arguments in a method call,
    /// using the method's dependent type parameter types to determine the correct
    /// carrier struct type (e.g., "int" for <c>Repeat&lt;3&gt;</c>).
    /// </summary>
    private static void RegisterMethodCallLiterals(MethodCallSuffixPostfixSuffixNode methodCall, CarrierStructRegistry registry)
    {
        if(methodCall.TypeArgs is null)
        {
            return;
        }

        // Collect all type argument nodes.
        var argNodes = new List<TypeArgumentNode> { methodCall.TypeArgs.TypeArgument };
        foreach(var extra in methodCall.TypeArgs.TypeArguments)
        {
            argNodes.Add(extra);
        }

        // For method calls, we default to "int" for integer literals since we don't
        // have easy access to the method's type parameters from the AST alone.
        // This is correct for the common case of method-level dependent params.
        foreach(var argNode in argNodes)
        {
            switch(argNode)
            {
                case IntegerLiteralTypeArgumentTypeArgumentNode intLit:
                    RegisterIntLiteral(intLit.Literal.Lexeme, negated: false, registry);
                    break;
                case NegatedIntegerTypeArgumentTypeArgumentNode negLit:
                    RegisterIntLiteral(negLit.Literal.Lexeme, negated: true, registry);
                    break;
                case HexLiteralTypeArgumentTypeArgumentNode hexLit:
                    RegisterHexLiteral(hexLit.Literal.Lexeme, registry);
                    break;
                case BinaryLiteralTypeArgumentTypeArgumentNode binLit:
                    RegisterBinaryLiteral(binLit.Literal.Lexeme, registry);
                    break;
            }
        }
    }

    /// <summary>
    /// Registers a carrier struct for an integer literal type argument.
    /// </summary>
    private static void RegisterIntLiteral(string lexeme, bool negated, CarrierStructRegistry registry)
    {
        if(long.TryParse(lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            if(negated)
            {
                value = -value;
            }

            _ = registry.RegisterByKeyword("int", value);
        }
    }

    /// <summary>
    /// Registers a carrier struct for a hex literal type argument.
    /// </summary>
    private static void RegisterHexLiteral(string lexeme, CarrierStructRegistry registry)
    {
        var hex = lexeme.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? lexeme[2..] : lexeme;
        if(long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            _ = registry.RegisterByKeyword("int", value);
        }
    }

    /// <summary>
    /// Registers a carrier struct for a binary literal type argument.
    /// </summary>
    private static void RegisterBinaryLiteral(string lexeme, CarrierStructRegistry registry)
    {
        var bin = lexeme.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? lexeme[2..] : lexeme;
        try
        {
            var value = Convert.ToInt64(bin, 2);
            _ = registry.RegisterByKeyword("int", value);
        }
        catch
        {
            // Ignore invalid binary literals.
        }
    }

    /// <summary>
    /// Registers a carrier struct for a boolean literal type argument.
    /// </summary>
    private static void RegisterBoolLiteral(bool value, CarrierStructRegistry registry)
    {
        _ = registry.RegisterByKeyword("bool", value);
    }

    /// <summary>
    /// Registers a carrier struct for a char literal type argument.
    /// </summary>
    private static void RegisterCharLiteral(string lexeme, CarrierStructRegistry registry)
    {
        if(lexeme.Length >= 3 && lexeme[0] == '\'' && lexeme[^1] == '\'')
        {
            var inner = lexeme[1..^1];
            char c;
            if(inner.Length == 1)
            {
                c = inner[0];
            }
            else if(inner.StartsWith('\\'))
            {
                c = inner switch
                {
                    "\\n" => '\n',
                    "\\r" => '\r',
                    "\\t" => '\t',
                    "\\0" => '\0',
                    "\\'" => '\'',
                    "\\\\" => '\\',
                    _ when inner.StartsWith("\\u") && inner.Length == 6 =>
                        (char)int.Parse(inner[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    _ => '?'
                };
            }
            else
            {
                c = '?';
            }

            _ = registry.RegisterByKeyword("char", c);
        }
    }

    /// <summary>
    /// Recursively scans a type for <see cref="ConstructedType"/> instances with dependent arguments.
    /// </summary>
    private static void CollectFromType(IType? type, CarrierStructRegistry registry)
    {
        if(type is ConstructedType ct && ct.DependentArguments is not null)
        {
            foreach(var depArg in ct.DependentArguments)
            {
                if(depArg is not null)
                {
                    _ = registry.Register(depArg);
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
        if(char.IsLetterOrDigit(c))
        {
            return c.ToString();
        }

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
        if(entries.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach(var entry in Entries)
        {
            _ = sb.AppendLine("[System.Runtime.CompilerServices.CompilerGenerated]");
            _ = sb.AppendLine($"public struct {entry.StructName} : {entry.InterfaceName}");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    public static {entry.Keyword} Value => {entry.LiteralValue};");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine();
        }

        return sb.ToString();
    }
}