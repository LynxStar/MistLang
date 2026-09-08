namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Maps CLR types (by namespace + type name) to their Mist Standard Library equivalents.
/// Types in this map are never projected from assemblies — the existing Standard Library
/// instance is returned instead.
/// </summary>
internal static class WellKnownTypeMap
{
    /// <summary>
    /// The well-known types whose CLR members are added to the Mist declaration standing in for
    /// them. Everything else in <see cref="Entries"/> is reachable only through what Standard
    /// declares, which is what classic does: measured, <c>"x".ToUpper()</c> resolves there and
    /// <c>(42).CompareTo(3)</c> does not, on Int32, Double, Char and Boolean alike.
    /// <para>
    /// A LIST RATHER THAN ALL OF Entries, because most of those targets cannot take a bridge at all.
    /// Standard.Array declares no Length on purpose -- its dependent type parameter is the length,
    /// and a projected property beside it makes every read ambiguous. Task and ValueTask are type
    /// ALIASES, not member types. Func and Action are one Mist declaration behind seventeen CLR
    /// arities each.
    /// </para>
    /// </summary>
    public static readonly HashSet<string> BridgedMembers = new(StringComparer.Ordinal)
    {
        "System.String",
    };

    /// <summary>
    /// CLR (namespace, typeName) -> Mist (namespace, typeName).
    /// Keys use CLR names (with backtick arity suffix for generics).
    /// </summary>
    public static readonly Dictionary<(string Namespace, string TypeName), (string MistNamespace, string MistTypeName)> Entries = new()
    {
        // Numeric types
        [("System", "Int32")] = ("Standard", "Int32"),
        [("System", "Int64")] = ("Standard", "Int64"),
        [("System", "Int16")] = ("Standard", "Int16"),
        [("System", "Byte")] = ("Standard", "Byte"),
        [("System", "SByte")] = ("Standard", "SByte"),
        [("System", "UInt32")] = ("Standard", "UInt32"),
        [("System", "UInt64")] = ("Standard", "UInt64"),
        [("System", "UInt16")] = ("Standard", "UInt16"),
        [("System", "Single")] = ("Standard", "Single"),
        [("System", "Double")] = ("Standard", "Double"),
        [("System", "Decimal")] = ("Standard", "Decimal"),
        // Primitives
        [("System", "String")] = ("Standard", "String"),
        [("System", "Boolean")] = ("Standard", "Boolean"),
        [("System", "Char")] = ("Standard", "Char"),
        [("System", "Void")] = ("Standard", "Void"),
        // Ranges and indices (map System.Index/System.Range to their spellable Standard names)
        [("System", "Index")] = ("Standard", "Index"),
        [("System", "Range")] = ("Standard", "Range"),
        // Async
        [("System.Threading.Tasks", "Task")] = ("Standard", "Task"),
        [("System.Threading.Tasks", "Task`1")] = ("Standard", "Task<>"),
        [("System.Threading.Tasks", "ValueTask")] = ("Standard", "ValueTask"),
        [("System.Threading.Tasks", "ValueTask`1")] = ("Standard", "ValueTask<>"),
        // The enumerable interfaces ARE the CLR ones, which ClrProjection has always asserted at
        // emission -- a foreach, a LINQ extension and every framework API expect that interface and
        // a structurally identical duplicate satisfies none of them. Mapping them here is the same
        // claim made one stage earlier, so the model holds ONE type where it used to hold two that
        // only emission reconciled.
        [("System.Collections.Generic", "IEnumerable`1")] = ("Standard", "IEnumerable<>"),
        [("System.Collections.Generic", "IEnumerator`1")] = ("Standard", "IEnumerator<>"),
        // Array (CLR T[] resolves to Standard.Array<T, unknown>)
        [("System", "Array")] = ("Standard", "Array<,>"),
        // Func<params TArgs, TResult> — always arity 2 (pack + return type).
        // All CLR System.Func arities map to the single variadic declaration.
        [("System", "Func`1")] = ("Standard", "Func<,>"),
        [("System", "Func`2")] = ("Standard", "Func<,>"),
        [("System", "Func`3")] = ("Standard", "Func<,>"),
        [("System", "Func`4")] = ("Standard", "Func<,>"),
        [("System", "Func`5")] = ("Standard", "Func<,>"),
        [("System", "Func`6")] = ("Standard", "Func<,>"),
        [("System", "Func`7")] = ("Standard", "Func<,>"),
        [("System", "Func`8")] = ("Standard", "Func<,>"),
        [("System", "Func`9")] = ("Standard", "Func<,>"),
        [("System", "Func`10")] = ("Standard", "Func<,>"),
        [("System", "Func`11")] = ("Standard", "Func<,>"),
        [("System", "Func`12")] = ("Standard", "Func<,>"),
        [("System", "Func`13")] = ("Standard", "Func<,>"),
        [("System", "Func`14")] = ("Standard", "Func<,>"),
        [("System", "Func`15")] = ("Standard", "Func<,>"),
        [("System", "Func`16")] = ("Standard", "Func<,>"),
        [("System", "Func`17")] = ("Standard", "Func<,>"),
        // Action<params TArgs> — always arity 1 (just the pack).
        // All CLR System.Action arities map to the single variadic declaration.
        [("System", "Action")] = ("Standard", "Action<>"),
        [("System", "Action`1")] = ("Standard", "Action<>"),
        [("System", "Action`2")] = ("Standard", "Action<>"),
        [("System", "Action`3")] = ("Standard", "Action<>"),
        [("System", "Action`4")] = ("Standard", "Action<>"),
        [("System", "Action`5")] = ("Standard", "Action<>"),
        [("System", "Action`6")] = ("Standard", "Action<>"),
        [("System", "Action`7")] = ("Standard", "Action<>"),
        [("System", "Action`8")] = ("Standard", "Action<>"),
        [("System", "Action`9")] = ("Standard", "Action<>"),
        [("System", "Action`10")] = ("Standard", "Action<>"),
        [("System", "Action`11")] = ("Standard", "Action<>"),
        [("System", "Action`12")] = ("Standard", "Action<>"),
        [("System", "Action`13")] = ("Standard", "Action<>"),
        [("System", "Action`14")] = ("Standard", "Action<>"),
        [("System", "Action`15")] = ("Standard", "Action<>"),
        [("System", "Action`16")] = ("Standard", "Action<>"),
    };
}