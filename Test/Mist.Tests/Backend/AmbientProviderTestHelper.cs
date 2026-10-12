using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
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
    /// A context that resolves over the runtime directory and <paramref name="besideTheRuntime"/> but
    /// loads nothing until asked: a signature naming a BCL type still reads, and only what a caller loads
    /// is held. A provider projects every exported type of every assembly it holds, and the runtime's are
    /// hundreds of megabytes of model that no claim below Tier 5 is about.
    /// </summary>
    public static MetadataLoadContext RuntimeResolvingContext(params string[] besideTheRuntime)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var resolvable = Directory.GetFiles(runtimeDirectory, "*.dll")
            .Concat(besideTheRuntime)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return new MetadataLoadContext(new PathAssemblyResolver(resolvable));
    }

    public static AmbientTypeProviderV2 ProviderOver(DiagnosticBag diagnostics, params string[] assemblyPaths)
    {
        var context = RuntimeResolvingContext(assemblyPaths);
        var loaded = AmbientTypeProviderV2Factory.LoadAssemblies([.. assemblyPaths], context, diagnostics);

        return new AmbientTypeProviderV2(loaded, context, diagnostics);
    }

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

        var readonlyNames = WellKnownTypes.PrimitiveTypes.Select(primitive => primitive.Name)
            .Append(WellKnownTypes.String.Name)
            .Append(WellKnownTypes.Regex.Name)
            .ToHashSet(StringComparer.Ordinal);

        void register(MistType type)
        {
            type.Assembly = standard;

            // The real Standard declarations carry `intrinsic` (Boolean.mist, Int32.mist, ...),
            // and construction branches on it -- NewtypeBuilder synthesizes the Value accessor
            // only for intrinsic sources -- so an unflagged shell would diverge from a real
            // compilation exactly where a test is trying to observe it. The primitives, String and
            // Regex carry `readonly` too, which an enum base and a readonly constraint are judged by.
            if(type is ConcreteType concrete)
            {
                concrete.Modifiers = concrete.Modifiers with
                {
                    IsIntrinsic = true,
                    IsReadonly = readonlyNames.Contains(type.Name),
                    IsPure = readonlyNames.Contains(type.Name),
                };
            }

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
            WellKnownTypes.Type.Name,
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

        // The task family carries what Task.mist declares and every await reaches: a TResult, an
        // intrinsic Drop returning it, and the discipline that decides how many drops a scope owes.
        foreach(var (name, discipline) in new[]
        {
            (WellKnownTypes.TaskGeneric.Name, SubstructuralDiscipline.Relevant),
            (WellKnownTypes.ValueTaskGeneric.Name, SubstructuralDiscipline.Linear),
        })
        {
            var task = (ClassType)programModel.TypesByFQN[$"Standard.{name}"];
            var result = new TypeParameter("TResult", 0, task.Scope, task);
            task.TypeParametersList.Add(result);
            task.Modifiers = task.Modifiers with { Discipline = discipline };
            task.MethodsList.Add(
                new Method("Drop", result, [], new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = true })
                {
                    DeclaringType = task,
                });
        }

        // Index/Range (ranges feature) are Standard shells needed by ambient projection
        // but are not part of AllStandardTypes; register them explicitly.
        register(new StructType("Index", "Standard", scope, null, null!));
        register(new StructType("Range", "Standard", scope, null, null!));

        // Interfaces used for interface mapping.
        register(new AbstractType(WellKnownTypes.IEnumerable.Name, WellKnownTypes.IEnumerable.Namespace, scope, null, null));
        register(new AbstractType(WellKnownTypes.IEnumerator.Name, WellKnownTypes.IEnumerator.Namespace, scope, null, null));

        // The length-indexed definitions, for CLR T[] → Standard.Array<T, Length> and
        // System.Span<T> → Standard.Span<T, Length>. Their parameters are declared, not just their
        // names: each of these deliberately has no Length PROPERTY, the dependent parameter being
        // the length itself, so a shell without them answers nothing for `x.Length` and diverges
        // from a real compilation exactly where a test would look.
        void registerLengthIndexed(MemberType type, string elementName)
        {
            type.TypeParametersList.Add(new TypeParameter(elementName, 0, type.Scope, type));
            type.TypeParametersList.Add(
                new TypeParameter("Length", 1, type.Scope, type) { IsDependent = true });
            register(type);
        }

        registerLengthIndexed(
            new ClassType(WellKnownTypes.Array.Name, WellKnownTypes.Array.Namespace, scope, null, null!),
            "TValue");
        registerLengthIndexed(
            new StructType(WellKnownTypes.Span.Name, WellKnownTypes.Span.Namespace, scope, null, null!),
            "T");
        registerLengthIndexed(
            new StructType(
                WellKnownTypes.ReadOnlySpan.Name, WellKnownTypes.ReadOnlySpan.Namespace, scope, null, null!),
            "T");

        RegisterList(programModel, scope, register);

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

    /// <summary>
    /// The List shell, carrying the promises Standard/List.mist declares on the members a probe
    /// reaches -- a pure constructor, local readonly reads, local writes -- so a purity or readonly
    /// verdict over a list is the one a real compilation gives.
    /// </summary>
    private static void RegisterList(ProgramModelClass programModel, Scope scope, Action<MistType> register)
    {
        var list = new ClassType("List<>", "Standard", scope, null, null!);
        var element = new TypeParameter("T", 0, list.Scope, list);
        list.TypeParametersList.Add(element);
        list.BaseAbstractTypes.Add(programModel.TypeInterner.GetOrCreateConstructedType(programModel.TypesByFQN["Standard.IEnumerable<>"], [element]));

        var int32 = programModel.TypesByFQN["Standard.Int32"];
        var boolean = programModel.TypesByFQN["Standard.Boolean"];
        var voidType = programModel.TypesByFQN["Standard.Void"];

        static MemberModifiers promise(bool isPure = false, bool isLocal = false, bool isReadonly = false)
        {
            return new MemberModifiers
            {
                Accessibility = Accessibility.Public,
                IsIntrinsic = true,
                IsPure = isPure,
                IsReadonly = isReadonly || isPure,
                IsLocal = isLocal,
            };
        }

        list.ConstructorsList.Add(new Constructor("List", list, [], promise(isPure: true)) { DeclaringType = list });
        list.PropertiesList.Add(new Property("Count", int32, promise(isLocal: true, isReadonly: true)) { DeclaringType = list, Getter = new PropertyAccessor { IsAuto = true } });
        list.IndexersList.Add(new Indexer(element, [new Parameter("index", int32)], promise())
        {
            DeclaringType = list,
            Getter = new PropertyAccessor { IsAuto = true, OwnModifiers = promise(isLocal: true, isReadonly: true) },
            Setter = new PropertyAccessor { IsAuto = true, OwnModifiers = promise(isLocal: true) },
        });

        foreach(var (name, returns, parameters, modifiers) in new (string, IMistType, List<Parameter>, MemberModifiers)[]
        {
            ("Contains", boolean, [new Parameter("item", element)], promise(isLocal: true, isReadonly: true)),
            ("IndexOf", int32, [new Parameter("item", element)], promise(isLocal: true, isReadonly: true)),
            ("Add", voidType, [new Parameter("item", element)], promise(isLocal: true)),
            ("Insert", voidType, [new Parameter("index", int32), new Parameter("item", element)], promise(isLocal: true)),
            ("Remove", boolean, [new Parameter("item", element)], promise(isLocal: true)),
            ("RemoveAt", voidType, [new Parameter("index", int32)], promise(isLocal: true)),
            ("Clear", voidType, [], promise(isLocal: true)),
            ("Sort", voidType, [], promise(isLocal: true)),
            ("Reverse", voidType, [], promise(isLocal: true)),
        })
        {
            list.MethodsList.Add(new Method(name, returns, parameters, modifiers) { DeclaringType = list });
        }

        register(list);
    }
}