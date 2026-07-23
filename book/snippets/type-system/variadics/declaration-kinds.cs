// Each variadic declaration emits per-arity monomorphized C#.
// For example, at arity 2:
public class Bundle<T1, T2> { }
public struct Pair<T1, T2> { }
public interface IApplier<T1, T2, TResult> { }
// Action<int, string> resolves to System.Action<int, string>
// Row<int, string> emits a newtype wrapping System.ValueTuple<T1, T2>
