using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Mist is culture-invariant: the text the emitted program makes of a value, the order it gives text,
/// its case mapping and every regular expression it runs name the invariant culture at the call (purity
/// Requirement 13.2), so no member Standard declares pure reads the process's current culture. At the
/// call rather than as a process-wide default, which the program can change and which would leave no room
/// for a culture-aware member beside these. A numeric primitive is formatted by the provider directly; any
/// other value whose text C# would take from the current culture goes through MistInvariant.Text, which
/// also reaches into a tuple, whose own ToString formats its elements by the current culture. An
/// interpolation is wrapped in FormattableString.Invariant rather than string.Create, whose handler
/// cannot be held across an await in a hole.
/// </summary>
internal static class InvariantCulture
{
    public const string Provider = "System.Globalization.CultureInfo.InvariantCulture";

    public const string RegexOption = "System.Text.RegularExpressions.RegexOptions.CultureInvariant";

    private const string StringOrder = "System.StringComparer.InvariantCulture";

    private const string ConsoleName = "Standard.Console";

    private const string ListName = "Standard.List<>";

    private const string SpanName = "Standard.Span<,>";

    private const string ReadOnlySpanName = "Standard.ReadOnlySpan<,>";

    private static readonly HashSet<string> Unformatted = new(StringComparer.Ordinal)
    {
        WellKnownTypes.String.FullyQualifiedName,
        WellKnownTypes.Char.FullyQualifiedName,
        WellKnownTypes.Boolean.FullyQualifiedName,
        WellKnownTypes.Void.FullyQualifiedName,
        WellKnownTypes.Regex.FullyQualifiedName,
    };

    private static readonly HashSet<string> Numerics = new(StringComparer.Ordinal)
    {
        WellKnownTypes.Int32.FullyQualifiedName,
        WellKnownTypes.Int64.FullyQualifiedName,
        WellKnownTypes.Int16.FullyQualifiedName,
        WellKnownTypes.UInt32.FullyQualifiedName,
        WellKnownTypes.UInt64.FullyQualifiedName,
        WellKnownTypes.UInt16.FullyQualifiedName,
        WellKnownTypes.Byte.FullyQualifiedName,
        WellKnownTypes.SByte.FullyQualifiedName,
        WellKnownTypes.Single.FullyQualifiedName,
        WellKnownTypes.Double.FullyQualifiedName,
        WellKnownTypes.Decimal.FullyQualifiedName,
    };

    [ThreadStatic]
    private static bool used;

    private enum Kind
    {
        Fixed,

        Numeric,

        Composite,
    }

    public static void Reset()
    {
        used = false;
    }

    public static bool Used()
    {
        return used;
    }

    public static bool Fixes(IMistType type)
    {
        return Classify(type) == Kind.Fixed;
    }

    public static string Text(string operand, IMistType type)
    {
        return Classify(type) switch
        {
            Kind.Fixed => operand,
            Kind.Numeric => $"{operand}.ToString({Provider})",
            _ => Helper(operand),
        };
    }

    public static string ObjectText(string receiver, IMistType type)
    {
        return Fixes(type) ? $"{receiver}.ToString()" : Text(receiver, type);
    }

    public static bool Concatenates(BinaryExpression binary)
    {
        return binary is { Operator: null, OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Concatenate }
            && Is(binary.Type, WellKnownTypes.String)
            && !(Fixes(binary.Left.Type) && Fixes(binary.Right.Type));
    }

    public static bool FormatsItself(Method? method)
    {
        return method is { Name: "ToString", ParametersList.Count: 0, Modifiers.IsStatic: false, DeclaringBlock: not null };
    }

    public static bool Writes(Method? callee, Parameter parameter)
    {
        return callee is { Name: "Write" or "WriteLine", DeclaringType: { } console }
            && console.FullyQualifiedName == ConsoleName
            && parameter.Type is UnknownType;
    }

    public static bool Provides(IMistType type)
    {
        return Classify(type) == Kind.Numeric;
    }

    public static string Hole(string value, IMistType type, bool provided)
    {
        if(Classify(type) == Kind.Composite)
        {
            return Helper(value);
        }

        return provided && Unwrap(TypeNameEmitter.Closed(type)) is ConstructedType { Definition.FullyQualifiedName: SpanName or ReadOnlySpanName }
            ? $"new string({value})"
            : value;
    }

    public static string Provided(string interpolated)
    {
        return $"System.FormattableString.Invariant({interpolated})";
    }

    public static string? Order(IMistType? listType)
    {
        return listType is not null
            && Present(listType) is ConstructedType { TypeArguments: [var element] } list
            && list.Definition.FullyQualifiedName == ListName
            ? Comparer(element)
            : null;
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistInvariant");

        using(writer.Block())
        {
            writer.WriteLine("public static string Text(object? value)");

            using(writer.Block())
            {
                writer.WriteLine("return value switch");
                writer.WriteLine("{");
                writer.WriteLine("    null => \"\",");
                writer.WriteLine("    System.Runtime.CompilerServices.ITuple tuple => Tuple(tuple),");
                writer.WriteLine($"    System.IFormattable formattable => formattable.ToString(null, {Provider}),");
                writer.WriteLine("    _ => value.ToString() ?? \"\",");
                writer.WriteLine("};");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static System.Collections.Generic.Comparer<T> Comparer<T>()");

            using(writer.Block())
            {
                writer.WriteLine("return System.Collections.Generic.Comparer<T>.Create(static (a, b) => a is string x && b is string y");
                writer.WriteLine("    ? string.Compare(x, y, System.StringComparison.InvariantCulture)");
                writer.WriteLine("    : System.Collections.Generic.Comparer<T>.Default.Compare(a, b));");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static System.Collections.Generic.Comparer<T> Elements<T>(params System.Collections.IComparer[] items)");

            using(writer.Block())
            {
                writer.WriteLine("return System.Collections.Generic.Comparer<T>.Create((a, b) =>");
                writer.WriteLine("{");
                writer.Indent();
                writer.WriteLine("var x = (System.Runtime.CompilerServices.ITuple)a!;");
                writer.WriteLine("var y = (System.Runtime.CompilerServices.ITuple)b!;");
                writer.WriteBlankLine();
                writer.WriteLine("for(var i = 0; i < items.Length; i++)");

                using(writer.Block())
                {
                    writer.WriteLine("var order = items[i].Compare(x[i], y[i]);");
                    writer.WriteBlankLine();
                    writer.WriteLine("if(order != 0)");

                    using(writer.Block())
                    {
                        writer.WriteLine("return order;");
                    }
                }

                writer.WriteBlankLine();
                writer.WriteLine("return 0;");
                writer.Dedent();
                writer.WriteLine("});");
            }

            writer.WriteBlankLine();
            writer.WriteLine("private static string Tuple(System.Runtime.CompilerServices.ITuple tuple)");

            using(writer.Block())
            {
                writer.WriteLine("var items = new string[tuple.Length];");
                writer.WriteBlankLine();
                writer.WriteLine("for(var i = 0; i < tuple.Length; i++)");

                using(writer.Block())
                {
                    writer.WriteLine("items[i] = Text(tuple[i]);");
                }

                writer.WriteBlankLine();
                writer.WriteLine("return \"(\" + string.Join(\", \", items) + \")\";");
            }
        }
    }

    private static string? Comparer(IMistType element)
    {
        var compared = Present(element);

        if(Is(compared, WellKnownTypes.String))
        {
            return StringOrder;
        }

        if(compared is TypeParameter or UnknownType)
        {
            used = true;
            return $"MistInvariant.Comparer<{TypeNameEmitter.Emit(element)}>()";
        }

        var items = compared switch
        {
            NamedTupleType named => named.ElementTypes,
            TupleType tuple => tuple.ElementTypes,
            _ => null,
        };

        if(items is null || OptionalUnion.InnerType(Unwrap(TypeNameEmitter.Closed(element))) is not null)
        {
            return null;
        }

        var comparers = items.Select(Comparer).ToList();

        if(comparers.All(comparer => comparer is null))
        {
            return null;
        }

        used = true;
        var each = items.Select((item, i) => comparers[i] ?? $"System.Collections.Generic.Comparer<{TypeNameEmitter.Emit(item)}>.Default");

        return $"MistInvariant.Elements<{TypeNameEmitter.Emit(element)}>({string.Join(", ", each)})";
    }

    private static string Helper(string value)
    {
        used = true;
        return $"MistInvariant.Text({value})";
    }

    private static Kind Classify(IMistType type)
    {
        var unwrapped = Unwrap(TypeNameEmitter.Closed(type));

        if(Numerics.Contains(unwrapped.FullyQualifiedName))
        {
            return Kind.Numeric;
        }

        if(Unformatted.Contains(unwrapped.FullyQualifiedName))
        {
            return Kind.Fixed;
        }

        return unwrapped switch
        {
            UnionType union => OptionalUnion.InnerType(union) is { } inner && Classify(inner) != Kind.Fixed ? Kind.Composite : Kind.Fixed,
            NamedTupleType named => Elements(named.ElementTypes),
            TupleType tuple => Elements(tuple.ElementTypes),
            EnumType or IFunctionType or Newtype => Kind.Fixed,
            DeclaredType { Assembly.HasMistSource: false } and not ConcreteType { IsIntrinsic: true } => Kind.Composite,
            ConcreteType { IsIntrinsic: true } => Kind.Fixed,
            ConcreteType { CloneSource.IsIntrinsic: true } => Kind.Fixed,
            ConcreteType declared => ClrCallbacks.Implements(declared, "System.IFormattable") ? Kind.Composite : Kind.Fixed,
            ConstructedType { Definition: ConcreteType definition } => Classify(definition),
            _ => Kind.Composite,
        };
    }

    private static Kind Elements(IEnumerable<IMistType> elements)
    {
        return elements.All(element => Classify(element) == Kind.Fixed) ? Kind.Fixed : Kind.Composite;
    }

    private static IMistType Unwrap(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Unwrap(aliased),
            RefinedType refined => Unwrap(refined.BaseType),
            LiftedType lifted => Unwrap(lifted.Type),
            UnknownValueType unknownValue => Unwrap(unknownValue.Domain),
            _ => type,
        };
    }

    private static IMistType Present(IMistType type)
    {
        var unwrapped = Unwrap(TypeNameEmitter.Closed(type));

        return OptionalUnion.InnerType(unwrapped) is { } inner ? Unwrap(inner) : unwrapped;
    }

    private static bool Is(IMistType type, WellKnownType wellKnown)
    {
        return Unwrap(TypeNameEmitter.Closed(type)).FullyQualifiedName == wellKnown.FullyQualifiedName;
    }
}