// Both forms emit the same monomorphized C# class per observed arity.
// For example, Foo<int, string> and Bar<[int, string]> both emit:
public class Foo<T1, T2> { }
public class Bar<T1, T2> { }
