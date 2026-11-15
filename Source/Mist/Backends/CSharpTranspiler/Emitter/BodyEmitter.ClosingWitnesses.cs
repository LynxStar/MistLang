using System.Text;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The witness a writer builds for a type argument (runtime-dependent-values design, <em>Identity by value: a closing as
/// a type argument</em>): a parameter that takes one passes its own on, a type mentioning no closing of a value-routed
/// type and no witnessed parameter takes the shared plain witness, and any other is built where it is written. Its
/// values are the slot values in pre-order, each closing's own slots before the next argument, a witnessed parameter
/// contributing its witness's values where it stands, so two writers that spell one closing differently build equal
/// values. The test and the stamp read every value back from the stored array rather than from where it was written,
/// so a capture's initializer runs once, when the witness is built, however often either is asked.
/// </summary>
internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static Dictionary<IMistType, string>? storedValues;

    internal static string Witness(IMistType argument, DiagnosticBag? diagnostics, Func<CapturedValue, string?>? read = null)
    {
        var closed = Unaliased(TypeNameEmitter.Closed(argument));

        if(closed is TypeParameter parameter && ClosingWitnesses.IsWitnessed(parameter))
        {
            return SlotReach.Read(parameter);
        }

        var spelled = TypeNameEmitter.Emit(closed);

        if(!ClosingWitnesses.Carries(closed))
        {
            return $"{ClosingWitnesses.Of(spelled)}.Plain";
        }

        var layout = new WitnessLayout();
        layout.Walk(closed);
        var values = layout.Parts.Select(part => part.Slot is { } slot
            ? $"({SlotRoutes.Keyword(slot)})({ReadValue(part.Argument!, diagnostics, read)})"
            : $"..{SlotReach.Read(part.Spread!)}.Values");
        var test = WitnessTest(closed, "__v", new WitnessCursor());
        var stamp = WitnessStamp(closed, layout, diagnostics);
        var modifier = layout.Parts.Any(part => part.Spread is not null) || test.Contains("__closing_", StringComparison.Ordinal) ? "" : "static ";

        return $"new {ClosingWitnesses.Of(spelled)}([{string.Join(", ", values)}], {modifier}(__v, __s) => {test}, {stamp ?? "null"})";
    }

    internal static bool WitnessAsked(IMistType tested)
    {
        return Unaliased(TypeNameEmitter.Closed(tested)) switch
        {
            TypeParameter parameter => ClosingWitnesses.IsWitnessed(parameter),
            ConstructedType constructed when !SlotSensitivity.IsContainer(constructed) && constructed.Definition is IHasTypeParameters generic
                => constructed.TypeArguments.Where((argument, i) => i < generic.TypeParametersList.Count && !generic.TypeParametersList[i].IsDependent
                    && ClosingWitnesses.IsWitnessed(generic.TypeParametersList[i]) && SlotSensitivity.Parameters(argument).Any(ClosingWitnesses.IsWitnessed)).Any(),
            TupleType tuple => tuple.ElementTypes.Any(WitnessAsked),
            NamedTupleType named => named.ElementTypes.Any(WitnessAsked),
            _ => false,
        };
    }

    internal static List<string> WitnessComparisons(IMistType tested, string bound, DiagnosticBag? diagnostics = null)
    {
        var comparisons = new List<string>();

        switch(Unaliased(TypeNameEmitter.Closed(tested)))
        {
            case TypeParameter parameter when ClosingWitnesses.IsWitnessed(parameter):
                comparisons.Add($"{SlotReach.Read(parameter)}.Is({bound})");
                break;

            case ConstructedType constructed when !SlotSensitivity.IsContainer(constructed) && constructed.Definition is IHasTypeParameters generic:
                for(var i = 0; i < constructed.TypeArguments.Count && i < generic.TypeParametersList.Count; i++)
                {
                    var slot = generic.TypeParametersList[i];

                    if(!slot.IsDependent && ClosingWitnesses.IsWitnessed(slot) && SlotSensitivity.Parameters(constructed.TypeArguments[i]).Any(ClosingWitnesses.IsWitnessed))
                    {
                        comparisons.Add($"{bound}.{ClosingWitnesses.Name(slot)}.Equals({Witness(constructed.TypeArguments[i], diagnostics)})");
                    }
                }

                break;

            case TupleType tuple:
                for(var i = 0; i < tuple.ElementTypes.Count; i++)
                {
                    comparisons.AddRange(WitnessComparisons(tuple.ElementTypes[i], $"{bound}.Item{i + 1}", diagnostics));
                }

                break;

            case NamedTupleType named:
                for(var i = 0; i < named.ElementTypes.Count; i++)
                {
                    comparisons.AddRange(WitnessComparisons(named.ElementTypes[i], $"{bound}.Item{i + 1}", diagnostics));
                }

                break;
        }

        return comparisons;
    }

    private static bool ConsumedByWitness(IMistType type, SlotCapture capture)
    {
        return WitnessConsumes(type, capture)
            || (KeptLocal(capture) is null && Unaliased(TypeNameEmitter.Closed(type)) is ConstructedType constructed
                && FactoredCaptures(constructed).Any(captured => ReferenceEquals(captured.Capture, capture)));
    }

    internal static List<string> FactoryWitnesses(IMistType product)
    {
        return Unaliased(TypeNameEmitter.Closed(product)) is ConstructedType constructed
            ? [.. ClosingWitnesses.Stored(constructed.Definition).Select(parameter => Witness(WitnessArgument(constructed, parameter), null, captured => KeptLocal(captured.Capture)))]
            : [.. ClosingWitnesses.Stored(product).Select(SlotReach.Read)];
    }

    private static bool WitnessConsumes(IMistType type, SlotCapture capture)
    {
        return Unaliased(TypeNameEmitter.Closed(type)) is ConstructedType constructed
            && ClosingWitnesses.Stored(constructed.Definition).Any(parameter =>
                WitnessCaptures(WitnessArgument(constructed, parameter)).Any(captured => ReferenceEquals(captured.Capture, capture)));
    }

    private static IEnumerable<SlotCapture> ReadTwice(IMistType type)
    {
        return Unaliased(TypeNameEmitter.Closed(type)) is ConstructedType constructed
            ? FactoredCaptures(constructed).Select(captured => captured.Capture)
                .Where(capture => ConsumedByClosing(constructed, capture) || WitnessConsumes(constructed, capture))
            : [];
    }

    private static IEnumerable<CapturedValue> FactoredCaptures(ConstructedType closing)
    {
        if(ProgramModel.Construction.Defaultability.DeclarationOf(closing) is not { } declaration)
        {
            return [];
        }

        var substitution = TypeSubstitution.ForReceiver(closing);
        return ConstructorWitnesses.Stored(declaration).Where(witness => ClosingWitnesses.IsFactored(witness.Parameter))
            .SelectMany(witness => ProductCaptures(substitution.Apply(witness.Parameter, interner: null)));
    }

    private static IEnumerable<CapturedValue> ProductCaptures(IMistType product)
    {
        return Unaliased(TypeNameEmitter.Closed(product)) is ConstructedType constructed
            ? ClosingWitnesses.Stored(constructed.Definition).SelectMany(parameter => WitnessCaptures(WitnessArgument(constructed, parameter)))
                .Concat(FactoredCaptures(constructed))
            : [];
    }

    private static IEnumerable<CapturedValue> FactoredCallCaptures(IReadOnlyList<IMistType> arguments, IReadOnlyList<TypeParameter> parameters)
    {
        return parameters.Zip(arguments).Where(pair => ClosingWitnesses.IsFactored(pair.First)).SelectMany(pair => ProductCaptures(pair.Second));
    }

    private static string ReadValue(IMistType argument, DiagnosticBag? diagnostics, Func<CapturedValue, string?>? read)
    {
        if(argument is CapturedValue captured && read?.Invoke(captured) is { } overridden)
        {
            return overridden;
        }

        return SlotValue(argument, diagnostics) ?? EmissionDiagnostics.Fallback("witness value", TypeDisplay.Mist(argument), "default");
    }

    private static string WitnessTest(IMistType type, string value, WitnessCursor cursor)
    {
        var closed = Unaliased(TypeNameEmitter.Closed(type));

        switch(closed)
        {
            case TypeParameter parameter when ClosingWitnesses.IsWitnessed(parameter):
                cursor.Spread(parameter);
                return $"{SlotReach.Read(parameter)}.Is({value})";

            case ConstructedType constructed when !SlotSensitivity.IsContainer(constructed) && ClosingWitnesses.Carries(constructed):
                return ConstructedTest(constructed, value, cursor);

            case TupleType or NamedTupleType when ClosingWitnesses.Carries(closed):
                var elements = closed is TupleType tuple ? tuple.ElementTypes : ((NamedTupleType)closed).ElementTypes;
                var bound = $"__w{cursor.Next()}";
                var spelled = $"System.ValueTuple<{string.Join(", ", elements.Select(TypeNameEmitter.Emit))}>";
                var parts = elements.Select((element, i) => WitnessTest(element, $"{bound}.Item{i + 1}", cursor)).ToList();
                return $"({value} is {spelled} {bound} && {string.Join(" && ", parts)})";

            case UnionType when OptionalUnion.IsOptional(closed) && OptionalUnion.InnerType(closed) is { } inner && ClosingWitnesses.Carries(inner):
                return $"({value} is null || {WitnessTest(inner, value, cursor)})";

            default:
                cursor.Skip(closed);
                return $"{value} is {TypeNameEmitter.Emit(closed)}";
        }
    }

    private static string ConstructedTest(ConstructedType constructed, string value, WitnessCursor cursor)
    {
        var bound = $"__w{cursor.Next()}";
        var checks = new List<string> { $"{value} is {TypeNameEmitter.Emit(constructed)} {bound}" };
        var generic = constructed.Definition as IHasTypeParameters;

        for(var i = 0; i < constructed.TypeArguments.Count; i++)
        {
            var slot = generic is not null && i < generic.TypeParametersList.Count ? generic.TypeParametersList[i] : null;
            var argument = constructed.TypeArguments[i];

            if(slot is not null && SlotRoutes.IsValueRouted(slot))
            {
                checks.Add($"{bound}.{SlotRoutes.Hidden(slot)} == ({SlotRoutes.Keyword(slot)})__s[{cursor.Offset}]");
                cursor.Value();
            }
            else if(slot is not null && ClosingWitnesses.IsWitnessed(slot))
            {
                var nested = new WitnessLayout();
                nested.Walk(argument);
                var reads = new List<string>();

                foreach(var part in nested.Parts)
                {
                    if(part.Slot is not null)
                    {
                        reads.Add($"__s[{cursor.Offset}]");
                        cursor.Value();
                    }
                    else
                    {
                        reads.Add($"..{SlotReach.Read(part.Spread!)}.Values");
                        cursor.Spread(part.Spread!);
                    }
                }

                checks.Add($"{bound}.{ClosingWitnesses.Name(slot)}.Same([{string.Join(", ", reads)}])");
            }
            else
            {
                cursor.Skip(argument);
            }
        }

        if(constructed.Enclosing is { } enclosing)
        {
            cursor.Skip(enclosing);
        }

        return $"({string.Join(" && ", checks)})";
    }

    private static string? WitnessStamp(IMistType type, WitnessLayout layout, DiagnosticBag? diagnostics)
    {
        if(StructStamp.HeldByValue(type) is null)
        {
            return null;
        }

        var enclosing = storedValues;
        storedValues = new Dictionary<IMistType, string>(ReferenceEqualityComparer.Instance);
        var cursor = new WitnessCursor();

        try
        {
            foreach(var part in layout.Parts)
            {
                if(part.Slot is { } slot)
                {
                    _ = storedValues.TryAdd(part.Argument!, $"(({SlotRoutes.Keyword(slot)})__s[{cursor.Offset}])");
                    cursor.Value();
                }
                else
                {
                    cursor.Spread(part.Spread!);
                }
            }

            return $"static __s => {StructStamp.Of(type, diagnostics ?? new DiagnosticBag())}";
        }
        finally
        {
            storedValues = enclosing;
        }
    }

    private static string? StoredValue(IMistType argument)
    {
        return storedValues?.GetValueOrDefault(argument);
    }

    private sealed record WitnessPart(TypeParameter? Slot, IMistType? Argument, TypeParameter? Spread);

    private sealed class WitnessLayout
    {
        private readonly HashSet<IMistType> seen = new(ReferenceEqualityComparer.Instance);

        public List<WitnessPart> Parts { get; } = [];

        public void Walk(IMistType type)
        {
            var closed = Unaliased(TypeNameEmitter.Closed(type));

            switch(closed)
            {
                case TypeParameter parameter when ClosingWitnesses.IsWitnessed(parameter):
                    Parts.Add(new WitnessPart(null, null, parameter));
                    break;

                case ConstructedType constructed:
                    var generic = constructed.Definition as IHasTypeParameters;

                    for(var i = 0; i < constructed.TypeArguments.Count; i++)
                    {
                        if(generic is not null && i < generic.TypeParametersList.Count && SlotRoutes.IsValueRouted(generic.TypeParametersList[i]))
                        {
                            Parts.Add(new WitnessPart(generic.TypeParametersList[i], constructed.TypeArguments[i], null));
                        }
                        else
                        {
                            Walk(constructed.TypeArguments[i]);
                        }
                    }

                    if(constructed.Enclosing is { } enclosing)
                    {
                        Walk(enclosing);
                    }

                    break;

                default:
                    if(!seen.Add(closed))
                    {
                        return;
                    }

                    foreach(var part in WitnessParts(closed))
                    {
                        Walk(part);
                    }

                    break;
            }
        }

    }

    private static IEnumerable<IMistType> WitnessParts(IMistType type)
    {
        return type switch
        {
            UnionType union => union.Constituents,
            NamedTupleType named => named.ElementTypes,
            TupleType tuple => tuple.ElementTypes,
            FunctionType function => function.ParameterTypes.Append(function.ReturnType),
            _ => [],
        };
    }

    private sealed class WitnessCursor
    {
        private readonly StringBuilder spreads = new();
        private int values;
        private int bindings;

        public string Offset => spreads.Length == 0 ? $"{values}" : $"{values}{spreads}";

        public void Value()
        {
            values++;
        }

        public void Spread(TypeParameter parameter)
        {
            _ = spreads.Append($" + {SlotReach.Read(parameter)}.Values.Length");
        }

        public void Skip(IMistType type)
        {
            var layout = new WitnessLayout();
            layout.Walk(type);

            foreach(var part in layout.Parts)
            {
                if(part.Slot is not null)
                {
                    Value();
                }
                else
                {
                    Spread(part.Spread!);
                }
            }
        }

        public int Next()
        {
            return ++bindings;
        }
    }
}