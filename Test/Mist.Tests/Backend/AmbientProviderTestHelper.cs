using Mist.ProgramModel;
using Mist.ProgramModel.Model.Scopes;
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

        // These stubs stand in for stdlib types that TypeBuilder would normally register
        // from Standard/*.mist, so they are attributed to the standard library assembly
        // the same way, keeping the fixture faithful to a real compilation.
        var standard = programModel.AddStandardAssembly();

        void register(MistType type)
        {
            type.Assembly = standard;
            _ = programModel.RegisterType(type);
        }

        // Primitive types + structural types (Task, ValueTask, Void, etc.). Each shell keeps
        // the kind its Standard/*.mist declaration has, because the optional lowering picks
        // Nullable-versus-null by that kind: String, Regex and the Task family are classes,
        // the rest are structs.
        var classKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            WellKnownTypes.String.Name,
            WellKnownTypes.Regex.Name,
            WellKnownTypes.Task.Name,
            WellKnownTypes.TaskGeneric.Name,
            WellKnownTypes.ValueTask.Name,
            WellKnownTypes.ValueTaskGeneric.Name,
        };

        foreach(var wellKnown in WellKnownTypes.AllStandardTypes)
        {
            register(classKinds.Contains(wellKnown.Name)
                ? new ClassType(wellKnown.Name, wellKnown.Namespace, scope, null, null!)
                : new StructType(wellKnown.Name, wellKnown.Namespace, scope, null, null!));
        }

        // Index/Range (ranges feature) are Standard shells needed by ambient projection
        // but are not part of AllStandardTypes; register them explicitly.
        register(new StructType("Index", "Standard", scope, null, null!));
        register(new StructType("Range", "Standard", scope, null, null!));

        // Interfaces used for interface mapping.
        register(new AbstractType(WellKnownTypes.IEnumerable.Name, WellKnownTypes.IEnumerable.Namespace, scope, null, null));
        register(new AbstractType(WellKnownTypes.IEnumerator.Name, WellKnownTypes.IEnumerator.Namespace, scope, null, null));

        // Array definition for CLR T[] → Standard.Array<T, Length>.
        register(new ClassType(WellKnownTypes.Array.Name, WellKnownTypes.Array.Namespace, scope, null, null!));

        // Func family (arity 1-17).
        for(var arity = 1; arity <= 17; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            register(new ClassType($"Func<{commas}>", "Standard", scope, null, null!));
        }

        // Action family (arity 0-16).
        register(new ClassType("Action", "Standard", scope, null, null!));
        for(var arity = 1; arity <= 16; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            register(new ClassType($"Action<{commas}>", "Standard", scope, null, null!));
        }
    }
}