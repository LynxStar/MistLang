using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// Shared helper for AmbientTypeProviderV2 tests. Registers the minimal set of
/// Standard Library types required for ambient type projection (aliases, delegates,
/// interfaces, arrays).
/// </summary>
internal static class AmbientProviderTestHelper
{
    /// <summary>
    /// Registers all Standard Library types needed by AmbientTypeProviderV2 into a
    /// test ProgramModel. Call this before <c>BuildAndRegister</c> to ensure all
    /// well-known types can be resolved.
    /// </summary>
    public static void RegisterStandardTypes(ProgramModelClass programModel)
    {
        _ = programModel.GetOrAddNamespace("Standard");
        var scope = new Scope(null);

        // Primitive types (aliases for System.Int32, System.String, etc.)
        var primitiveTypes = new[]
        {
            "Int32", "Int64", "Int16", "Byte", "SByte",
            "UInt32", "UInt64", "UInt16", "Single", "Double", "Decimal",
            "String", "Boolean", "Char", "Void",
            "Task", "Task<>", "ValueTask", "ValueTask<>",
        };

        foreach(var name in primitiveTypes)
        {
            _ = programModel.RegisterType(new ClassType(name, "Standard", scope, null, null!));
        }

        // Interfaces used for interface mapping.
        _ = programModel.RegisterType(new AbstractType("IEnumerable<>", "Standard", scope, null, null));
        _ = programModel.RegisterType(new AbstractType("IEnumerator<>", "Standard", scope, null, null));

        // Array definition for CLR T[] → Standard.Array<T, unknown>.
        _ = programModel.RegisterType(new ClassType("Array<,>", "Standard", scope, null, null!));

        // Func family (arity 1-17).
        for(var arity = 1; arity <= 17; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            _ = programModel.RegisterType(new ClassType($"Func<{commas}>", "Standard", scope, null, null!));
        }

        // Action family (arity 0-16).
        _ = programModel.RegisterType(new ClassType("Action", "Standard", scope, null, null!));
        for(var arity = 1; arity <= 16; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            _ = programModel.RegisterType(new ClassType($"Action<{commas}>", "Standard", scope, null, null!));
        }
    }
}
