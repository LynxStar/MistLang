using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
/// also reaches into a tuple, whose own ToString formats its elements by the current culture, and calls
/// any other value's own ToString with the invariant culture current for that call. An interpolation is
/// wrapped in FormattableString.Invariant rather than string.Create, whose handler cannot be held across an
/// await in a hole. A Console argument C# converts to a number is written as the conversion C#'s overload
/// resolution takes: Overloads is Console.Write's numeric parameters in C#'s better-conversion-target order,
/// which leaves decimal unranked against float and double, and the operator is the one whose target is the most
/// encompassing that reaches the chosen parameter. Where C# finds no single choice, or prefers char, or has
/// bool, which ranks against no number, the argument is written as it stands for Roslyn to judge, as is a ref
/// struct, which no helper taking an object can be handed, and a CLR enum, which converts to no number.
/// A list of clones of an ordered intrinsic is ordered by the backing value: the emitted clone implements no
/// IComparable, so C#'s default order would throw.
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

    private static readonly string[] Overloads =
    [
        WellKnownTypes.Int32.FullyQualifiedName,
        WellKnownTypes.UInt32.FullyQualifiedName,
        WellKnownTypes.Int64.FullyQualifiedName,
        WellKnownTypes.UInt64.FullyQualifiedName,
        WellKnownTypes.Single.FullyQualifiedName,
        WellKnownTypes.Double.FullyQualifiedName,
        WellKnownTypes.Decimal.FullyQualifiedName,
    ];

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

    public static bool FixesPresent(IMistType optional)
    {
        return Classify(Present(optional)) == Kind.Fixed;
    }

    public static string ConditionalText(string receiver, IMistType optional)
    {
        if(Classify(Present(optional)) == Kind.Numeric)
        {
            return $"{receiver}?.ToString({Provider})";
        }

        used = true;
        return $"MistInvariant.Optional({receiver})";
    }

    public static bool WritesText(IMistType type)
    {
        return !Fixes(type) || ImplicitNumeric(type) is not null;
    }

    public static string Written(string operand, IMistType type)
    {
        return ImplicitNumeric(type) is { } numeric
            ? Text($"(({TypeNameEmitter.Emit(numeric)}){operand})", numeric)
            : Text(operand, type);
    }

    public static bool Concatenates(BinaryExpression binary)
    {
        return binary is { Operator: null, OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Concatenate }
            && Is(binary.Type, WellKnownTypes.String)
            && !(Fixes(binary.Left.Type) && Fixes(binary.Right.Type));
    }

    public static bool FormatsItself(Method? method)
    {
        return method is { Name: "ToString", ParametersList.Count: 0, Modifiers.IsStatic: false }
            && (method.DeclaringBlock is not null || method.DeclaringType is { Assembly.HasMistSource: false });
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
        return listType is not null && ListElement(listType, TypeSubstitution.Empty, 0) is { } element
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
                writer.WriteLine("    _ when Pair(value) is { } pair => pair,");
                writer.WriteLine("    _ => ByType(value),");
                writer.WriteLine("};");
            }

            writer.WriteBlankLine();
            writer.WriteLine("private static string ByType(object value)");

            using(writer.Block())
            {
                writer.WriteLine("var current = System.Globalization.CultureInfo.CurrentCulture;");
                writer.WriteLine($"System.Globalization.CultureInfo.CurrentCulture = {Provider};");
                writer.WriteBlankLine();
                writer.WriteLine("try");

                using(writer.Block())
                {
                    writer.WriteLine("return value.ToString() ?? \"\";");
                }

                writer.WriteLine("finally");

                using(writer.Block())
                {
                    writer.WriteLine("System.Globalization.CultureInfo.CurrentCulture = current;");
                }
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static string? Optional(object? value)");

            using(writer.Block())
            {
                writer.WriteLine("return value is null ? null : Text(value);");
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
            writer.WriteLine(
                "public static System.Collections.Generic.Comparer<T> ByValue<T, V>(System.Func<T, V> value, System.Collections.Generic.IComparer<V> order)");

            using(writer.Block())
            {
                writer.WriteLine("return System.Collections.Generic.Comparer<T>.Create((a, b) => order.Compare(value(a), value(b)));");
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

            writer.WriteBlankLine();
            writer.WriteLine("private static string? Pair(object value)");

            using(writer.Block())
            {
                writer.WriteLine("var type = value.GetType();");
                writer.WriteBlankLine();
                writer.WriteLine("if(!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(System.Collections.Generic.KeyValuePair<,>))");

                using(writer.Block())
                {
                    writer.WriteLine("return null;");
                }

                writer.WriteBlankLine();
                writer.WriteLine("return \"[\" + Text(type.GetProperty(\"Key\")!.GetValue(value)) + \", \" + Text(type.GetProperty(\"Value\")!.GetValue(value)) + \"]\";");
            }
        }
    }

    private static IMistType? ListElement(IMistType type, TypeSubstitution substitution, int depth)
    {
        if(depth > 32)
        {
            return null;
        }

        switch(Present(type))
        {
            case ConstructedType { TypeArguments: [var element] } list when list.Definition.FullyQualifiedName == ListName:
                return substitution.Apply(element, interner: null);

            case ConstructedType { Definition: ConcreteType { BaseType: { } baseType } definition } constructed:
                var closed = constructed.TypeArguments.Select(argument => substitution.Apply(argument, interner: null)).ToList();
                return ListElement(
                    baseType, substitution.Extend(TypeSubstitution.ForParameters(definition.TypeParametersList, closed)), depth + 1);

            case ConcreteType { BaseType: { } baseType }:
                return ListElement(baseType, substitution, depth + 1);

            case TypeParameter parameter:
                return parameter.ConstraintsList
                    .OfType<TypeConstraint>()
                    .Select(constraint => ListElement(constraint.ConstraintType, substitution, depth + 1))
                    .FirstOrDefault(element => element is not null);

            default:
                return null;
        }
    }

    private static IMistType? ImplicitNumeric(IMistType type)
    {
        if(Classify(type) == Kind.Numeric || IsChar(type, 0))
        {
            return null;
        }

        var targets = ImplicitTargets(Unwrap(TypeNameEmitter.Closed(type)));

        if(targets.Any(target => IsChar(target, 0) || Is(target, WellKnownTypes.Boolean)))
        {
            return null;
        }

        var numeric = targets.Where(target => Classify(target) == Kind.Numeric).ToList();
        var parameter = Array.Find(Overloads, overload => numeric.Any(target => Widens(target, overload)));

        if(parameter is null
            || ((parameter == WellKnownTypes.Single.FullyQualifiedName || parameter == WellKnownTypes.Double.FullyQualifiedName)
                && numeric.Any(target => Is(target, WellKnownTypes.Decimal))))
        {
            return null;
        }

        var reaching = numeric.Where(target => Widens(target, parameter)).ToList();
        var encompassing = reaching.Where(target => reaching.All(other => Widens(other, Name(target)))).ToList();

        return encompassing.Select(Name).Distinct(StringComparer.Ordinal).Count() == 1 ? encompassing[0] : null;
    }

    private static List<IMistType> ImplicitTargets(IMistType unwrapped)
    {
        if(unwrapped is EnumType enumType)
        {
            return enumType is { Assembly.HasMistSource: true, UnderlyingType: { } underlying } ? [underlying] : [];
        }

        var substitution = TypeSubstitution.ForReceiver(unwrapped as ConstructedType);

        return Conversions(unwrapped, 0)
            .Where(conversion => conversion.IsImplicit)
            .Select(conversion => substitution.Apply(conversion.TargetType, interner: null))
            .ToList();
    }

    private static bool Widens(IMistType from, string to)
    {
        var name = Name(from);

        return name == to || ConversionRelation.WidensNumerically(name, to);
    }

    private static string Name(IMistType type)
    {
        return Unwrap(TypeNameEmitter.Closed(type)).FullyQualifiedName;
    }

    private static bool IsChar(IMistType type, int depth)
    {
        var unwrapped = Unwrap(TypeNameEmitter.Closed(type));

        if(unwrapped.FullyQualifiedName == WellKnownTypes.Char.FullyQualifiedName)
        {
            return true;
        }

        return depth <= 32 && unwrapped switch
        {
            ConcreteType { CloneSource: { } source } => IsChar(source, depth + 1),
            Newtype { SourceType: { } source } => IsChar(source, depth + 1),
            _ => false,
        };
    }

    private static IEnumerable<CastOperator> Conversions(IMistType type, int depth)
    {
        var declared = type switch
        {
            ConstructedType { Definition: ConcreteType definition } => definition,
            ConcreteType concrete => concrete,
            _ => null,
        };

        if(declared is null || depth > 32)
        {
            return [];
        }

        return declared.BaseType is { } baseType
            ? declared.CastOperatorsList.Concat(Conversions(Unwrap(baseType), depth + 1))
            : declared.CastOperatorsList;
    }

    private static string? Comparer(IMistType element)
    {
        var compared = Present(element);

        if(Is(compared, WellKnownTypes.String))
        {
            return StringOrder;
        }

        if(compared is ConcreteType { CloneSource: { IsIntrinsic: true } source } && Ordered(source))
        {
            used = true;
            var absent = OptionalUnion.InnerType(Unwrap(TypeNameEmitter.Closed(element))) is null ? "" : "?";
            var value = TypeNameEmitter.Emit(source) + absent;
            var order = Is(source, WellKnownTypes.String) ? StringOrder : $"System.Collections.Generic.Comparer<{value}>.Default";

            return $"MistInvariant.ByValue<{TypeNameEmitter.Emit(element)}, {value}>(static item => item{absent}.Value, {order})";
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
            EnumType or IFunctionType or Newtype or StructType { IsRef: true } => Kind.Fixed,
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

    private static bool Ordered(IMistType source)
    {
        return Numerics.Contains(Name(source))
            || Is(source, WellKnownTypes.String)
            || Is(source, WellKnownTypes.Char)
            || Is(source, WellKnownTypes.Boolean);
    }
}