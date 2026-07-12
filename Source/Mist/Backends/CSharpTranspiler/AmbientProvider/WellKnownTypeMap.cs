namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Maps CLR types (by namespace + type name) to their Mist Standard Library equivalents.
/// Types in this map are never projected from assemblies — the existing Standard Library
/// instance is returned instead.
/// </summary>
internal static class WellKnownTypeMap
{
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
        // Async
        [("System.Threading.Tasks", "Task")] = ("Standard", "Task"),
        [("System.Threading.Tasks", "Task`1")] = ("Standard", "Task<>"),
        [("System.Threading.Tasks", "ValueTask")] = ("Standard", "ValueTask"),
        [("System.Threading.Tasks", "ValueTask`1")] = ("Standard", "ValueTask<>"),
        // Func family (arity 1-17)
        [("System", "Func`1")] = ("Standard", "Func<>"),
        [("System", "Func`2")] = ("Standard", "Func<,>"),
        [("System", "Func`3")] = ("Standard", "Func<,,>"),
        [("System", "Func`4")] = ("Standard", "Func<,,,>"),
        [("System", "Func`5")] = ("Standard", "Func<,,,,>"),
        [("System", "Func`6")] = ("Standard", "Func<,,,,,>"),
        [("System", "Func`7")] = ("Standard", "Func<,,,,,,>"),
        [("System", "Func`8")] = ("Standard", "Func<,,,,,,,>"),
        [("System", "Func`9")] = ("Standard", "Func<,,,,,,,,>"),
        [("System", "Func`10")] = ("Standard", "Func<,,,,,,,,,>"),
        [("System", "Func`11")] = ("Standard", "Func<,,,,,,,,,,>"),
        [("System", "Func`12")] = ("Standard", "Func<,,,,,,,,,,,>"),
        [("System", "Func`13")] = ("Standard", "Func<,,,,,,,,,,,,>"),
        [("System", "Func`14")] = ("Standard", "Func<,,,,,,,,,,,,,>"),
        [("System", "Func`15")] = ("Standard", "Func<,,,,,,,,,,,,,,>"),
        [("System", "Func`16")] = ("Standard", "Func<,,,,,,,,,,,,,,,>"),
        [("System", "Func`17")] = ("Standard", "Func<,,,,,,,,,,,,,,,,>"),
        // Action family (arity 0-16)
        [("System", "Action")] = ("Standard", "Action"),
        [("System", "Action`1")] = ("Standard", "Action<>"),
        [("System", "Action`2")] = ("Standard", "Action<,>"),
        [("System", "Action`3")] = ("Standard", "Action<,,>"),
        [("System", "Action`4")] = ("Standard", "Action<,,,>"),
        [("System", "Action`5")] = ("Standard", "Action<,,,,>"),
        [("System", "Action`6")] = ("Standard", "Action<,,,,,>"),
        [("System", "Action`7")] = ("Standard", "Action<,,,,,,>"),
        [("System", "Action`8")] = ("Standard", "Action<,,,,,,,>"),
        [("System", "Action`9")] = ("Standard", "Action<,,,,,,,,>"),
        [("System", "Action`10")] = ("Standard", "Action<,,,,,,,,,>"),
        [("System", "Action`11")] = ("Standard", "Action<,,,,,,,,,,>"),
        [("System", "Action`12")] = ("Standard", "Action<,,,,,,,,,,,>"),
        [("System", "Action`13")] = ("Standard", "Action<,,,,,,,,,,,,>"),
        [("System", "Action`14")] = ("Standard", "Action<,,,,,,,,,,,,,>"),
        [("System", "Action`15")] = ("Standard", "Action<,,,,,,,,,,,,,,>"),
        [("System", "Action`16")] = ("Standard", "Action<,,,,,,,,,,,,,,,>"),
    };
}