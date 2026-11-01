# Numeric and array capabilities

The MIST-1051 and MIST-1052 contracts extend Requirement 5's backend supply promise.
The Standard declarations are the public signature inventory. Intrinsic fields are fields,
not properties. The C# backend checks their name, static shape, exact backing type and
readonly storage against CLR field metadata, and refuses an unsupplied read.

## Numeric contracts

| Type set | Members |
| --- | --- |
| `int` | Nullable `Parse(string?)`, `Parse(ReadOnlySpan<char>)`, and `Parse(string?, IFormatProvider?)` |
| `ulong`, `float`, `double`, `decimal` | Nullable `Parse(string?, NumberStyles, IFormatProvider?)` |
| `float`, `double` | Static `IsFinite`, `IsNaN`, `IsInfinity`, `IsPositiveInfinity`, `IsNegativeInfinity`, `BitIncrement`, `BitDecrement` |
| `int`, `float`, `double`, `decimal`, `char` | Typed instance `CompareTo(T)` |
| All eleven numeric types and `char` | Instance `ToString(IFormatProvider?)` |
| `float`, `double` | Instance `ToString(string?, IFormatProvider?)` |
| `double` | Instance `ToString(string?)` |
| Eight integral types, `char`, `decimal` | Static readonly intrinsic `MinValue` and `MaxValue` fields |
| `float`, `double` | Static readonly intrinsic `MaxValue` fields |

Parsing returns a value on success and absence on invalid/null input or integral/decimal
overflow. The backend calls the corresponding CLR `TryParse`, without a public `TryParse`
API or an out parameter in Mist. Styles and providers pass through unchanged; string/span
`int.Parse` without a provider uses the CLR default integer parsing contract. Floating parsing
preserves NaN and infinity, including the CLR's infinity result on floating overflow. Invalid
style combinations retain the underlying CLR behavior. Compiler literal consumers must still
check finiteness themselves. Existing C# consumers are inventoried here for a future port;
this change does not rewrite the compiler into Mist or change its diagnostic handling.

The provider-only `int.Parse` overload is a narrow addition to the original task table,
demanded by `DiagnosticRenderer.cs:128` and `DiagnosticFactory.cs:134` at the inspected
revision. Typed `char.CompareTo` is also required by Storm's `NfaBuilder.cs:362-363`.
Qualified `System.Math`, `System.Convert` and non-mapped `System.Int128` routes remain available.

`IntrinsicBounds` exposes the declared field values to constant consumers without replacing
field reads in emitted source. This allows constant arithmetic, including negation of `int`
and `long` minima, to use the same wrapping rules as literal arithmetic.

## Array and Task contracts

| C# demand | Mist form |
| --- | --- |
| `Task.Run(Action)` / `Task.Run(Func<T>)` | Native async block / async expression |
| `task.Wait()` | Await the started task |
| `Task.WhenAll` | Existing `Task.WhenAll` / `Task<T>.WhenAll` |
| `Array.Empty<T>()` | Typed empty array literal |
| `Array.IndexOf(array, value)` | Instance `int? IndexOf(T value)` |
| Membership-only index search | Instance `bool Contains(T value)` |
| `Array.Sort(array, comparer)` | Instance `void Sort(IComparer<T> comparer)` |
| `Array.Find(array, predicate)` | Instance `T? Find(Func<T, bool> predicate)` |
| `Array.Exists(array, predicate)` | Instance `bool Any(Func<T, bool> predicate)` |

Index search and membership use the CLR's default element equality, including null and NaN.
IndexOf returns the first zero-based match or absence. Find and Any visit in array order and
stop on the first match; Find preserves a matched numeric zero as a present value. Sort mutates
the receiver using the caller's comparer. The dependent array length is unchanged. Parameterless `Any()` answers whether the receiver is
nonempty. It preserves the existing calls in `samples/ExtensionMethodBinding/Program.mist:30,40`
when adding the predicate overload: Requirement 11.4 of the extension-methods specification gives
own members precedence by name. This narrow companion overload avoids changing that lookup policy.
Intrinsic primitive clones forward bound fields and preserve absence when wrapping parsed values;
static fields do not become part of a struct ancestor's transported instance state.

Nullable parsing and array operations share their runtime source with lower-tier tests: the
compiler compiles those files and embeds the same files in programs that call the adapters.
Find boxes its result before the call site converts it to the closed result type, preserving
value-type absence. A union element widens into the result carrier on a match; absence enters
that carrier through its `void` arm. Optional elements keep a single optional layer. A result
closed to `void` converts both a match and absence to `Void.Instance`, while still evaluating
the search once. Backing storage keeps its CLR identity; the adapters close optional results
at each call.
Existing optional specialization supplies generic callers. Native async,
empty literals and the existing Task combinators keep their existing lowering.

## Production inventory

Inspected MistLang `84a76e5e47b0a47962c29b5b6c51ef27b0b42075`, Storm submodule
`86dfd33895ba15ec7a3187e00340b236e666ab19`. Paths below are under `Source/`.
Tests, generated source, `obj` and `bin` are excluded. Lines refer to that revision, before
membership-only C# calls are changed to `Contains`. Emitted-code strings are identified as such.

The historical CLI drain calls no longer exist: `Mist.CLI/Program.cs` calls
`ChildProcessRunner`, which starts the child with inherited output streams and waits for its
exit (`Mist.CLI/ChildProcessRunner.cs:18,39`). There are no current production `Task.Run`,
instance `task.Wait()` or `Task.FromResult` calls. The emitter still writes `Task.Run` for native
async syntax. No process drain implementation is replaced, serialized or removed by this packet.
A future Mist port that redirects pipes must start both reads before either await and join both
before reading the exit result, forwarding stdout/stderr to their respective destinations.

### Task and array sites

| Site | C# use | Mist mapping |
| --- | --- | --- |
| `Mist.CLI/Steering/SteeringInstaller.cs:120` | `Array.Sort(bundledFiles, StringComparer.Ordinal);` | Sort(comparer) |
| `Mist.CLI/Steering/SteeringInstaller.cs:248` | `Array.Sort(existing, StringComparer.Ordinal);` | Sort(comparer) |
| `Mist/Backends/CSharpTranspiler/AmbientProvider/AmbientTypeResolver.cs:390` | `return elementNames is not null && Array.Exists(elementNames, name => name is not null)` | Any(predicate) |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:1216` | `? "System.Threading.Tasks.Task.Run(async () => "` | Native async (emitted C#) |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:1217` | `: "System.Threading.Tasks.Task.Run(() => ");` | Native async (emitted C#) |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:1241` | `? $"new {TypeNameEmitter.Emit(asyncBlock.Type)}(System.Threading.Tasks.Task.Run(() =>"` | Native async (emitted C#) |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:1242` | `: "System.Threading.Tasks.Task.Run(() =>");` | Native async (emitted C#) |
| `Mist/Backends/CSharpTranspiler/Lowering/InvariantCulture.cs:388` | `var parameter = Array.Find(Overloads, overload => numeric.Any(target => Widens(target, overload)));` | Find(predicate), with explicit absence handling |
| `Mist/Backends/CSharpTranspiler/Lowering/TaskCombinators.cs:71` | `writer.WriteLine("await System.Threading.Tasks.Task.WhenAll(tasks);");` | Existing WhenAll |
| `Mist/Backends/CSharpTranspiler/Lowering/TaskCombinators.cs:80` | `writer.WriteLine("return System.Threading.Tasks.Task.WhenAll(tasks);");` | Existing WhenAll |
| `Mist/Backends/CSharpTranspiler/Lowering/TaskCombinators.cs:89` | `"return System.Threading.Tasks.TaskExtensions.Unwrap(System.Threading.Tasks.Task.WhenAny(tasks));");` | Existing WhenAny (unchanged) |
| `Mist/Backends/CSharpTranspiler/Lowering/TaskCombinators.cs:98` | `"return System.Threading.Tasks.TaskExtensions.Unwrap(System.Threading.Tasks.Task.WhenAny(tasks));");` | Existing WhenAny (unchanged) |
| `Mist/Driver/StdlibLoader.cs:85` | `Array.Sort(files, StringComparer.Ordinal);` | Sort(comparer) |
| `Mist/ProgramAnalysis/Comparability.cs:38` | `_ => Array.IndexOf(ComparableNames, type.FullyQualifiedName) >= 0,` | Contains for membership; nullable IndexOf otherwise |
| `Mist/ProgramModel/Construction/Binding/ConversionRelation.cs:164` | `return from is IFunctionType && Array.IndexOf(BaseDelegateNames, to.FullyQualifiedName) >= 0;` | Contains for membership; nullable IndexOf otherwise |
| `Mist/ProgramModel/Construction/Binding/ConversionRelation.cs:376` | `&& Array.IndexOf(targets, to) >= 0` | Contains for membership; nullable IndexOf otherwise |
| `Mist/ProgramModel/Construction/Binding/OperatorAdmissibility.cs:126` | `UnaryOperatorKind.Negate => IsNumeric(underlying) && !Array.Exists(Unnegatable, type => Is(underlying, type))` | Any(predicate) |
| `Mist/ProgramModel/Construction/Binding/TypeArgumentInference.cs:580` | `if(Array.Exists(bounds, slot => slot.Count == 0))` | Any(predicate) |
| `Mist/ProgramModel/Construction/Types/NewtypeConversions.cs:122` | `: Array.Empty<UnheldCopy?>();` | Typed empty literal |
| `Storm.SourceGenerator/Emit/AstEmitter.Children.cs:25` | `_ => Array.Empty<ExpressionIR>(),` | Typed empty literal |
| `Storm.SourceGenerator/Emit/EmissionContext.cs:70` | `Traits = traits ?? Array.Empty<TraitIR>();` | Typed empty literal |
| `Storm.SourceGenerator/Emit/NfaBuilder.cs:276` | `return Array.Empty<CharRange>();` | Typed empty literal |
| `Storm.SourceGenerator/Emit/ParserEmitter.Elements.cs:384` | `if(AstEmitter.Representative(field.Element, Array.Empty<ExpressionIR>()) is { } source)` | Typed empty literal |
| `Storm.SourceGenerator/Emit/ParserEmitter.Elements.cs:438` | `_ => Array.Empty<ExpressionIR>(),` | Typed empty literal |
| `Storm.SourceGenerator/Emit/ParserEmitter.InlineBlock.cs:410` | `_ => Array.Empty<ExpressionIR>(),` | Typed empty literal |
| `Storm.SourceGenerator/Emit/ParserEmitter.InlineProjection.cs:84` | `_ => Array.Empty<ExpressionIR>(),` | Typed empty literal |

### Numeric sites

| Site | C# use | Mist mapping |
| --- | --- | --- |
| `Mist/Backends/CSharpTranspiler/AmbientProvider/NameFormatTranslator.cs:43` | `if(!int.TryParse(arityStr, out var arity) \|\| arity < 1)` | Nullable Parse; handle absence explicitly |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:2687` | `_ => int.MaxValue,` | Same named typed field/predicate/step |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:2851` | `writer.Append(arity.ToString(CultureInfo.InvariantCulture));` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:3168` | `float f => $"{f.ToString("R", CultureInfo.InvariantCulture)}f",` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:3170` | `decimal m => $"{m.ToString(CultureInfo.InvariantCulture)}m",` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:3176` | `_ => Convert.ToString(literal.Value, CultureInfo.InvariantCulture)!,` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:3188` | `var digits = value.ToString(CultureInfo.InvariantCulture);` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:3205` | `var text = d.ToString("R", CultureInfo.InvariantCulture);` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:3239` | `BinaryExpression or UnaryExpression when NarrowArithmetic.Promotes(expression.Type) => int.MaxValue,` | Same named typed field/predicate/step |
| `Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:3250` | `_ => int.MaxValue,` | Same named typed field/predicate/step |
| `Mist/Backends/CSharpTranspiler/Emitter/ErasedShape.cs:28` | `return [$"Length: {length.ToString(CultureInfo.InvariantCulture)}"];` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Emitter/IndexerMetadataName.cs:55` | `candidate = "Indexer" + suffix.ToString(CultureInfo.InvariantCulture);` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Lowering/ConstructorConstraintInterfaces.cs:99` | `IsOpen(type) ? i.ToString(CultureInfo.InvariantCulture) : Identifier(type)));` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Lowering/DependentCarrierNames.cs:124` | `_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Lowering/DependentCarrierNames.cs:150` | `var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Lowering/RefinedConstituentNames.cs:81` | `Int128 integral => integral.ToString(CultureInfo.InvariantCulture),` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Lowering/RefinedConstituentNames.cs:82` | `float single when float.IsFinite(single) => single.ToString("R", CultureInfo.InvariantCulture),` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Lowering/RefinedConstituentNames.cs:83` | `double real when double.IsFinite(real) => real.ToString("R", CultureInfo.InvariantCulture),` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Lowering/RefinedConstituentNames.cs:84` | `decimal exact => exact.ToString(CultureInfo.InvariantCulture),` | Same formatting overload and provider |
| `Mist/Backends/CSharpTranspiler/Lowering/UnionCarrierNames.cs:70` | `+ (LengthErasure.ErasedLength(constructed)?.ToString(CultureInfo.InvariantCulture) ?? ""),` | Same formatting overload and provider |
| `Mist/Common/Diagnostics/DiagnosticFactory.cs:134` | `var highest = placeholders.Max(name => int.Parse(name, CultureInfo.InvariantCulture));` | Nullable Parse; handle absence explicitly |
| `Mist/Common/Diagnostics/DiagnosticJsonWriter.cs:136` | `case QuantityValue quantity: writer.WriteRawValue(quantity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;` | Same formatting overload and provider |
| `Mist/Common/Diagnostics/DiagnosticRenderer.cs:63` | `QuantityValue integer => integer.Value.ToString(CultureInfo.InvariantCulture),` | Same formatting overload and provider |
| `Mist/Common/Diagnostics/DiagnosticRenderer.cs:128` | `var index = int.Parse(name, CultureInfo.InvariantCulture);` | Nullable Parse; handle absence explicitly |
| `Mist/Common/Diagnostics/LegacyArguments.cs:81` | `IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),` | Same formatting overload and provider |
| `Mist/Driver/Generators/GeneratorDiagnostics.cs:15` | `if(!match.Success \|\| !int.TryParse(match.Groups[2].Value, out var row) \|\| row <= 0` | Nullable Parse; handle absence explicitly |
| `Mist/Driver/Generators/GeneratorDiagnostics.cs:16` | `\|\| !int.TryParse(match.Groups[3].Value, out var column) \|\| column <= 0)` | Nullable Parse; handle absence explicitly |
| `Mist/Driver/ProjectFileParser.Generators.cs:76` | `if(timeout is not long limit \|\| limit <= 0 \|\| limit > int.MaxValue / 1000)` | Same named typed field/predicate/step |
| `Mist/Driver/SdkDiscovery.cs:319` | `if(dotIndex > 0 && int.TryParse(numPart[..dotIndex], out var major))` | Nullable Parse; handle absence explicitly |
| `Mist/Driver/StandardStageDetails.cs:54` | `return text.ToString(CultureInfo.InvariantCulture);` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:15` | `public static readonly Admitted Everything = new([new Interval(long.MinValue, long.MaxValue)]);` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:25` | `public bool IsEverything => Intervals.Length == 1 && Intervals[0].Low == long.MinValue && Intervals[0].High == long.MaxValue;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:38` | `return new Admitted([new Interval(value, long.MaxValue)]);` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:43` | `return new Admitted([new Interval(long.MinValue, value)]);` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:48` | `return value == long.MaxValue ? Nothing : AtLeast(value + 1);` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:53` | `return value == long.MinValue ? Nothing : AtMost(value - 1);` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:100` | `var next = long.MinValue;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:110` | `open = interval.High < long.MaxValue;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:111` | `next = open ? interval.High + 1 : long.MaxValue;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:116` | `result.Add(new Interval(next, long.MaxValue));` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:140` | `Difference(Intervals[0].Low, other.Intervals[^1].High, long.MinValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:141` | `Difference(Intervals[^1].High, other.Intervals[0].Low, long.MaxValue));` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:151` | `var low = Intervals[0].Low < previous.Intervals[0].Low ? long.MinValue : Intervals[0].Low;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:152` | `var high = Intervals[^1].High > previous.Intervals[^1].High ? long.MaxValue : Intervals[^1].High;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:187` | `(long.MinValue, long.MaxValue) => "anything",` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:188` | `(long.MinValue, var high) => string.Create(CultureInfo.InvariantCulture, $"<= {high}"),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:189` | `(var low, long.MaxValue) => string.Create(CultureInfo.InvariantCulture, $">= {low}"),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:197` | `if(value is long.MinValue or long.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:203` | `return by > 0 && sum < value ? long.MaxValue : by < 0 && sum > value ? long.MinValue : sum;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:208` | `if(value is long.MinValue or long.MaxValue \|\| by is long.MinValue or long.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Admitted.cs:224` | `if(merged.Count > 0 && (merged[^1].High == long.MaxValue \|\| interval.Low <= merged[^1].High + 1))` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:45` | `ulong value when value <= long.MaxValue => (long)value,` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:56` | `=> Read(operand, visited) is { } constant && constant != long.MinValue ? -constant : null,` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:72` | `[WellKnownTypes.SByte.FullyQualifiedName] = (sbyte.MinValue, sbyte.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:73` | `[WellKnownTypes.Byte.FullyQualifiedName] = (byte.MinValue, byte.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:74` | `[WellKnownTypes.Int16.FullyQualifiedName] = (short.MinValue, short.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:75` | `[WellKnownTypes.UInt16.FullyQualifiedName] = (ushort.MinValue, ushort.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:76` | `[WellKnownTypes.Int32.FullyQualifiedName] = (int.MinValue, int.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:77` | `[WellKnownTypes.UInt32.FullyQualifiedName] = (uint.MinValue, uint.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:78` | `[WellKnownTypes.Int64.FullyQualifiedName] = (long.MinValue, long.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:79` | `[WellKnownTypes.UInt64.FullyQualifiedName] = (0, long.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/EnumConstants.cs:80` | `[WellKnownTypes.Char.FullyQualifiedName] = (char.MinValue, char.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Lengths.cs:154` | `return admitted?.Intervals is [var first, ..] && first.Low > long.MinValue ? first.Low : null;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralPredicates.cs:57` | `if(value is null \|\| (value.Value is double number && !double.IsFinite(number))` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralPredicates.cs:58` | `\|\| (value.Value is float single && !float.IsFinite(single)))` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralPredicates.cs:121` | `(float left, float right) => (int?)left.CompareTo(right),` | Typed CompareTo |
| `Mist/ProgramAnalysis/Refinements/LiteralPredicates.cs:122` | `(double left, double right) => left.CompareTo(right),` | Typed CompareTo |
| `Mist/ProgramAnalysis/Refinements/LiteralPredicates.cs:123` | `(decimal left, decimal right) => left.CompareTo(right),` | Typed CompareTo |
| `Mist/ProgramAnalysis/Refinements/LiteralValue.cs:18` | `LiteralExpression { Value: float number, Type: var type } when float.IsFinite(number) => new LiteralValue(number, type.FullyQualifiedName),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralValue.cs:19` | `LiteralExpression { Value: double number, Type: var type } when double.IsFinite(number) => new LiteralValue(number, type.FullyQualifiedName),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralValue.cs:61` | `return double.IsFinite(number) ? new LiteralValue(number, name) : null;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralValue.cs:67` | `return float.IsFinite(number) ? new LiteralValue(number, name) : null;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralValue.cs:92` | `long number when number != long.MinValue => new LiteralValue(-number, TypeName),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralValue.cs:93` | `int number when number != int.MinValue => new LiteralValue(-number, TypeName),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/LiteralValue.cs:103` | `return Value is string text ? $"\"{text}\"" : Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "";` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:16` | `private static readonly Int128 UnsignedShift = (Int128)long.MinValue;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:143` | `return value is { } constant && constant >= ulong.MinValue && constant <= ulong.MaxValue` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:169` | `return name == WellKnownTypes.Int32.FullyQualifiedName ? Admitted.Between(int.MinValue, int.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:171` | `: name == WellKnownTypes.Int16.FullyQualifiedName ? Admitted.Between(short.MinValue, short.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:172` | `: name == WellKnownTypes.SByte.FullyQualifiedName ? Admitted.Between(sbyte.MinValue, sbyte.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:173` | `: name == WellKnownTypes.UInt32.FullyQualifiedName ? Admitted.Between(uint.MinValue, uint.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:174` | `: name == WellKnownTypes.UInt16.FullyQualifiedName ? Admitted.Between(ushort.MinValue, ushort.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:175` | `: name == WellKnownTypes.Byte.FullyQualifiedName ? Admitted.Between(byte.MinValue, byte.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/MatchedValues.cs:176` | `: name == WellKnownTypes.Char.FullyQualifiedName ? Admitted.Between(char.MinValue, char.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealRequirement.cs:123` | `float number => float.IsFinite(number),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealRequirement.cs:124` | `double number => double.IsFinite(number),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:120` | `return Kind == RealKind.Decimal ? point.Decimal.ToString(CultureInfo.InvariantCulture)` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:121` | `: Kind == RealKind.Single ? ((float)point.Binary).ToString("R", CultureInfo.InvariantCulture)` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:122` | `: point.Binary.ToString("R", CultureInfo.InvariantCulture);` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:297` | `return new RealNumber(new RealPoint(number == 0 ? 0 : number, 0), double.IsNaN(number), double.IsPositiveInfinity(number) ? 1 : double.IsNegativeInfinity(number) ? -1 : 0);` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:345` | `RealKind.Single => new RealInterval(new RealPoint(-float.MaxValue, 0), true, new RealPoint(float.MaxValue, 0), true),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:346` | `RealKind.Double => new RealInterval(new RealPoint(-double.MaxValue, 0), true, new RealPoint(double.MaxValue, 0), true),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:347` | `_ => new RealInterval(new RealPoint(0, decimal.MinValue), true, new RealPoint(0, decimal.MaxValue), true),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:414` | `return kind == RealKind.Single ? higher ? float.BitIncrement((float)value) : float.BitDecrement((float)value)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:415` | `: higher ? double.BitIncrement(value) : double.BitDecrement(value);` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/RealSet.cs:420` | `return kind == RealKind.Decimal ? left.Decimal.CompareTo(right.Decimal) : left.Binary.CompareTo(right.Binary);` | Typed CompareTo |
| `Mist/ProgramAnalysis/Refinements/Requirement.cs:177` | `=> value >= long.MinValue && value <= long.MaxValue ? (long)value : null,` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Refinements/Requirement.cs:182` | `when Constant(operand) is { } constant && constant != long.MinValue => -constant,` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Rules/FiniteCoverage.cs:258` | `if(interval.High < long.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Rules/FiniteCoverage.cs:269` | `var high = i + 1 < edges.Count ? edges[i + 1] - 1 : long.MaxValue;` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Rules/FiniteCoverage.cs:308` | `return value.ToString(CultureInfo.InvariantCulture);` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Rules/RefinementAnalyzer.Closings.cs:113` | `return lifted.Value is ulong ? Admitted.Exactly(long.MaxValue) : Requirement.ValuesOf(lifted);` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs:529` | `ComparisonKind.LessThan when member.Low is { } low && low > long.MinValue => Admitted.Below(low),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs:530` | `ComparisonKind.LessThanOrEqual when member.Low is { } low && low > long.MinValue => Admitted.AtMost(low),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs:531` | `ComparisonKind.GreaterThan when member.High is { } high && high < long.MaxValue => Admitted.Above(high),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs:532` | `ComparisonKind.GreaterThanOrEqual when member.High is { } high && high < long.MaxValue => Admitted.AtLeast(high),` | Same named typed field/predicate/step |
| `Mist/ProgramAnalysis/Rules/RefinementFindings.cs:24` | `context.Report(ProgramAnalysisDiagnostics.IndexOutOfBounds, at.Node, [value, onIndex], new NameValue(only.Low.ToString(CultureInfo.InvariantCulture)), target, new NameValue(predicate));` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Rules/RefinementFindings.cs:35` | `context.Report(ProgramAnalysisDiagnostics.ConstraintViolated, at.Node, [value, at], new NameValue(constant.ToString(CultureInfo.InvariantCulture)), new NameValue(predicate), new NameValue(onConstraint));` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Rules/RefinementFindings.cs:44` | `context.Report(ProgramAnalysisDiagnostics.RefinementViolated, at.Node, [value, at], new NameValue(constant.ToString(CultureInfo.InvariantCulture)), new NameValue(predicate), target);` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Rules/RefinementFindings.cs:64` | `context.Report(ProgramAnalysisDiagnostics.ConstraintViolated, at, about, new NameValue(only.ToString(CultureInfo.InvariantCulture)), new NameValue(predicate), new NameValue(parameter));` | Same formatting overload and provider |
| `Mist/ProgramAnalysis/Rules/RefinementFindings.cs:152` | `LiteralExpression { Value: { } constant } => Convert.ToString(constant, CultureInfo.InvariantCulture) ?? "",` | Same formatting overload and provider |
| `Mist/ProgramModel/Construction/Binding/ConstantConversion.cs:17` | `[WellKnownTypes.SByte.FullyQualifiedName] = (sbyte.MinValue, sbyte.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/ConstantConversion.cs:18` | `[WellKnownTypes.Byte.FullyQualifiedName] = (byte.MinValue, byte.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/ConstantConversion.cs:19` | `[WellKnownTypes.Int16.FullyQualifiedName] = (short.MinValue, short.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/ConstantConversion.cs:20` | `[WellKnownTypes.UInt16.FullyQualifiedName] = (ushort.MinValue, ushort.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/ConstantConversion.cs:21` | `[WellKnownTypes.UInt32.FullyQualifiedName] = (uint.MinValue, uint.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/ConstantConversion.cs:22` | `[WellKnownTypes.UInt64.FullyQualifiedName] = (ulong.MinValue, ulong.MaxValue),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs:537` | `return size is LiteralExpression literal && IntegralConstant.Of(literal.Value) is { } written && written >= 0 && written <= int.MaxValue` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/IntegerLiterals.cs:22` | `(false, false) when magnitude <= int.MaxValue => ((int)magnitude, WellKnownTypes.Int32),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/IntegerLiterals.cs:23` | `(_, false) when magnitude <= uint.MaxValue => ((uint)magnitude, WellKnownTypes.UInt32),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/IntegerLiterals.cs:24` | `(false, _) when magnitude <= long.MaxValue => ((long)magnitude, WellKnownTypes.Int64),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/IntegerLiterals.cs:33` | `(2147483648, false, false) => (int.MinValue, WellKnownTypes.Int32),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/IntegerLiterals.cs:34` | `(9223372036854775808, false, _) => (long.MinValue, WellKnownTypes.Int64),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/IntegerLiterals.cs:41` | `return Parse(lexeme) is ( > int.MaxValue and <= uint.MaxValue, false, false) ? WellKnownTypes.Int64 : null;` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/IntegerLiterals.cs:46` | `return IntegralConstant.Of(Read(lexeme)?.Value) is { } length && length <= int.MaxValue ? (int)length : null;` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Binding/Expressions/IntegerLiterals.cs:57` | `return ulong.TryParse(text, style, CultureInfo.InvariantCulture, out var magnitude)` | Nullable Parse; handle absence explicitly |
| `Mist/ProgramModel/Construction/Binding/Expressions/LiteralBinder.cs:41` | `'M' => (decimal.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : null, WellKnownTypes.Decimal),` | Nullable Parse; handle absence explicitly |
| `Mist/ProgramModel/Construction/Binding/Expressions/LiteralBinder.cs:42` | `'F' => (float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) ? f : null, WellKnownTypes.Single),` | Nullable Parse; handle absence explicitly |
| `Mist/ProgramModel/Construction/Binding/Expressions/LiteralBinder.cs:43` | `_ => (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null, WellKnownTypes.Double),` | Nullable Parse; handle absence explicitly |
| `Mist/ProgramModel/Construction/Binding/SpreadProjection.cs:63` | `StorageElementIndex = int.Parse(memberName.AsSpan(field.Name.Length)) - 1,` | Nullable Parse; handle absence explicitly |
| `Mist/ProgramModel/Construction/Binding/SpreadProjection.cs:97` | `\|\| !int.TryParse(memberName.AsSpan(stem.Length), out var index))` | Nullable Parse; handle absence explicitly |
| `Mist/ProgramModel/Construction/Members/SpreadMember.cs:79` | `&& (nameIsSpread \|\| int.TryParse(name.AsSpan(stem.Length), out _));` | Nullable Parse; handle absence explicitly |
| `Mist/ProgramModel/Construction/Resolution/PackTypePositions.cs:82` | `&& length is >= 0 and <= int.MaxValue` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Resolution/ScopeResolver.cs:832` | `if(value is { } signed && signed >= long.MinValue && signed <= long.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Resolution/ScopeResolver.cs:837` | `if(value is { } unsigned && unsigned > long.MaxValue && unsigned <= ulong.MaxValue)` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Construction/Resolution/TypeInterner.cs:47` | `return value <= long.MaxValue ? GetOrCreateLifted(type, (long)value) : GetOrCreateLifted(type, value);` | Same named typed field/predicate/step |
| `Mist/ProgramModel/DependentDomains.cs:83` | `char character => character is >= ' ' and <= '~' ? character.ToString(CultureInfo.InvariantCulture) : $"\\u{(int)character:x4}",` | Same formatting overload and provider |
| `Mist/ProgramModel/DependentDomains.cs:84` | `_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,` | Same formatting overload and provider |
| `Mist/ProgramModel/DependentDomains.cs:96` | `{ } number when number >= int.MinValue && number <= int.MaxValue => WellKnownTypes.Int32.Keyword,` | Same named typed field/predicate/step |
| `Mist/ProgramModel/DependentDomains.cs:97` | `{ } number when number >= uint.MinValue && number <= uint.MaxValue => WellKnownTypes.UInt32.Keyword,` | Same named typed field/predicate/step |
| `Mist/ProgramModel/DependentDomains.cs:98` | `{ } number when number >= long.MinValue && number <= long.MaxValue => WellKnownTypes.Int64.Keyword,` | Same named typed field/predicate/step |
| `Mist/ProgramModel/IntegralConstant.cs:15` | `[WellKnownTypes.SByte.FullyQualifiedName] = (sbyte.MinValue, sbyte.MaxValue, value => (sbyte)value),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/IntegralConstant.cs:16` | `[WellKnownTypes.Byte.FullyQualifiedName] = (byte.MinValue, byte.MaxValue, value => (byte)value),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/IntegralConstant.cs:17` | `[WellKnownTypes.Int16.FullyQualifiedName] = (short.MinValue, short.MaxValue, value => (short)value),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/IntegralConstant.cs:18` | `[WellKnownTypes.UInt16.FullyQualifiedName] = (ushort.MinValue, ushort.MaxValue, value => (ushort)value),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/IntegralConstant.cs:19` | `[WellKnownTypes.Int32.FullyQualifiedName] = (int.MinValue, int.MaxValue, value => (int)value),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/IntegralConstant.cs:20` | `[WellKnownTypes.UInt32.FullyQualifiedName] = (uint.MinValue, uint.MaxValue, value => (uint)value),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/IntegralConstant.cs:21` | `[WellKnownTypes.Int64.FullyQualifiedName] = (long.MinValue, long.MaxValue, value => (long)value),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/IntegralConstant.cs:22` | `[WellKnownTypes.UInt64.FullyQualifiedName] = (ulong.MinValue, ulong.MaxValue, value => (ulong)value),` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Model/Scopes/CandidateSet.cs:201` | `var nearestDepth = int.MaxValue;` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Model/Scopes/CandidateSet.cs:224` | `var nearest = int.MaxValue;` | Same named typed field/predicate/step |
| `Mist/ProgramModel/Model/Scopes/CandidateSet.cs:245` | `ofKind.Sort((left, right) => left.Depth.CompareTo(right.Depth));` | Typed CompareTo |
| `Mist/ProgramModel/Model/Scopes/TypeMemberLookup.cs:319` | `\|\| !int.TryParse(requested.AsSpan(stem.Length), out var index)` | Nullable Parse; handle absence explicitly |
| `Mist/ProgramModel/Model/Types/LiftedType.cs:21` | `: base(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", new Scope(null), declaringType: null)` | Same formatting overload and provider |
| `Storm.SourceGenerator/Emit/AstEmitter.cs:660` | `return properties.OrderBy(property => property.Source != null && order.TryGetValue(property.Source, out var position) ? position : int.MaxValue).ToList();` | Same named typed field/predicate/step |
| `Storm.SourceGenerator/Emit/NfaBuilder.cs:362` | `var cmp = a.Lo.CompareTo(b.Lo);` | Typed CompareTo |
| `Storm.SourceGenerator/Emit/NfaBuilder.cs:363` | `return cmp != 0 ? cmp : a.Hi.CompareTo(b.Hi);` | Typed CompareTo |
| `Storm.SourceGenerator/Emit/ParserEmitter.InlineBlock.cs:89` | `return Depth == 0 ? local : local + Depth.ToString(CultureInfo.InvariantCulture);` | Same formatting overload and provider |
