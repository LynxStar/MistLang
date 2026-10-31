using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal sealed partial class CSharpEmitterV2
{
    private static void EmitStructInitMarker(string outputDir, List<string> emittedFiles)
    {
        var writer = new CSharpWriter();
        writer.WriteLine($"namespace {ConstructorConstraintInterfaces.Namespace};");
        writer.WriteBlankLine();
        writer.WriteLine($"public readonly struct {StructAncestorFactory.MarkerTypeName}");
        using(writer.Block())
        {
        }

        writer.WriteBlankLine();
        writer.WriteLine($"public readonly struct {DefaultSynthesis.MarkerTypeName}");
        using(writer.Block())
        {
        }

        var path = Path.Combine(outputDir, "__StructInit.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    /// <summary>
    /// One tag-and-payload carrier struct per distinct non-optional union, top-level in a single
    /// generated file so it is reachable from every emitted namespace. The interner is read
    /// rather than the emitted surface walked, because every union a program mentions passes
    /// through it exactly once -- locals, signatures and casts alike. Optionals never get a
    /// carrier; <see cref="TypeNameEmitter"/> lowers them to Nullable or the bare type.
    /// </summary>
    private void EmitUnionCarriers(string outputDir, List<string> emittedFiles)
    {
        var interner = ((ProgramModel.Model.ProgramModel)programModel).TypeInterner;
        var carriers = new SortedDictionary<string, UnionType>(StringComparer.Ordinal);

        // A union over a type parameter is never a runtime type: only its closed substitutions are,
        // and each of those is interned and carried on its own.
        foreach(var union in interner.UnionTypes)
        {
            if(!OptionalUnion.IsOptional(union) && !IsOpen(union))
            {
                carriers[UnionCarrierNames.CarrierName(union)] = union;
            }
        }

        if(carriers.Count == 0)
        {
            return;
        }

        var writer = new CSharpWriter();
        var first = true;

        foreach(var (name, union) in carriers)
        {
            if(!first)
            {
                writer.WriteBlankLine();
            }

            first = false;
            UnionCarrierEmitter.Emit(name, union, carriers, writer);
        }

        var path = Path.Combine(outputDir, "__Unions.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    /// <summary>
    /// Every interned value of a declared kind gets a carrier, more than are reachable: a spare one
    /// is an unreferenced struct while a missing one fails the build of generated code, and the uses
    /// are split between constructed types and method call sites.
    /// </summary>
    private void EmitDependentCarriers(string outputDir, List<string> emittedFiles)
    {
        var interner = ((ProgramModel.Model.ProgramModel)programModel).TypeInterner;
        var kinds = new SortedSet<DependentKind>();

        foreach(var parameter in DependentParameters())
        {
            if(parameter is { IsDependent: true, IsPack: false, Domain: { } declared }
                && DependentDomains.Primitive(declared) is { } primitive
                && DependentDomains.KindOf(primitive) is { } kind)
            {
                _ = kinds.Add(kind);
            }
        }

        if(kinds.Count == 0)
        {
            return;
        }

        var carriers = new SortedDictionary<string, (DependentKind Kind, object Value)>(StringComparer.Ordinal);

        foreach(var lifted in interner.LiftedTypes)
        {
            if(DependentDomains.KindOfValue(lifted.Value) is { } kind
                && kinds.Contains(kind)
                && DependentCarrierNames.Carrier(lifted.Value) is { } name)
            {
                carriers[name] = (kind, lifted.Value);
            }
        }

        var writer = new CSharpWriter();

        foreach(var kind in kinds)
        {
            writer.WriteLine(
                $"public interface {DependentCarrierNames.Witness(kind)} {{ static abstract {DependentCarrierNames.WitnessType(kind)} Value {{ get; }} }}");
        }

        foreach(var (name, carrier) in carriers)
        {
            writer.WriteBlankLine();
            writer.WriteLine(
                $"public readonly struct {name} : {DependentCarrierNames.Witness(carrier.Kind)} "
                + $"{{ public static {DependentCarrierNames.WitnessType(carrier.Kind)} Value => {DependentCarrierNames.Literal(carrier.Value)}; }}");
        }

        var path = Path.Combine(outputDir, "__DependentValues.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    /// <summary>
    /// Every dependent type parameter the program declares, on a type or on one of its methods.
    /// </summary>
    private IEnumerable<TypeParameter> DependentParameters()
    {
        foreach(var type in ((ProgramModel.Model.ProgramModel)programModel).TypesByFQN.Values)
        {
            if(type is IHasTypeParameters generic)
            {
                foreach(var parameter in generic.TypeParametersList)
                {
                    yield return parameter;
                }
            }

            if(type is MemberType memberType)
            {
                foreach(var method in memberType.MethodsList)
                {
                    foreach(var parameter in method.TypeParametersList)
                    {
                        yield return parameter;
                    }
                }
            }
        }
    }

    private void EmitMonomorphs(string outputDir, List<string> emittedFiles, ConstraintClosureMap closures)
    {
        var writer = new CSharpWriter();

        if(!MonomorphEmitter.Emit(monomorphs, closures, variadicUses, writer, diagnostics))
        {
            return;
        }

        var path = Path.Combine(outputDir, "__Variadics.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void EmitContextRuntime(string outputDir, List<string> emittedFiles)
    {
        if(!ContextLowering.Used())
        {
            return;
        }

        var writer = new CSharpWriter();
        ContextLowering.Emit(writer);

        var path = Path.Combine(outputDir, "__Context.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void EmitOwnershipRuntime(string outputDir, List<string> emittedFiles)
    {
        if(!LifetimeLowering.Used())
        {
            return;
        }

        var writer = new CSharpWriter();
        LifetimeLowering.Emit(writer);

        var path = Path.Combine(outputDir, "__Ownership.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void EmitTaskRuntime(string outputDir, List<string> emittedFiles)
    {
        if(!TaskCombinators.Used())
        {
            return;
        }

        var writer = new CSharpWriter();
        TaskCombinators.Emit(writer);

        var path = Path.Combine(outputDir, "__Tasks.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void EmitInvariantRuntime(string outputDir, List<string> emittedFiles)
    {
        if(!InvariantCulture.Used())
        {
            return;
        }

        var writer = new CSharpWriter();
        InvariantCulture.Emit(writer);

        var path = Path.Combine(outputDir, "__Invariant.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void EmitArrayRuntime(string outputDir, List<string> emittedFiles)
    {
        if(!ArrayLowering.Used() && !ArrayEnumeration.Used())
        {
            return;
        }

        var writer = new CSharpWriter();

        if(ArrayLowering.Used())
        {
            ArrayLowering.Emit(writer);
        }

        if(ArrayEnumeration.Used())
        {
            if(ArrayLowering.Used())
            {
                writer.WriteBlankLine();
            }

            ArrayEnumeration.Emit(writer);
        }

        var path = Path.Combine(outputDir, "__Array.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void EmitVoidStandIn(string outputDir, List<string> emittedFiles)
    {
        var writer = new CSharpWriter();
        VoidStandIn.Emit(writer);

        var path = Path.Combine(outputDir, "__Void.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    /// <summary>
    /// One interface per distinct parameterized constructor constraint signature, in its own file
    /// because the declaration that constrains and the type that closes may sit in any namespace and
    /// each emitted file carries only its own unit's usings.
    /// <para>
    /// Collected from the CONSTRAINTS rather than from what closes them, because a <c>where</c> clause
    /// names the interface whether or not the generic is ever instantiated.
    /// </para>
    /// </summary>
    private void EmitConstructorConstraintInterfaces(string outputDir, List<string> emittedFiles)
    {
        var signatures = new Dictionary<string, IReadOnlyList<IMistType>>(StringComparer.Ordinal);

        foreach(var type in EmittableTypes())
        {
            foreach(var parameter in type.TypeParametersList)
            {
                Collect(parameter, signatures);
            }

            foreach(var method in type.MethodsList)
            {
                foreach(var parameter in method.TypeParametersList)
                {
                    Collect(parameter, signatures);
                }
            }
        }

        foreach(var method in EmittableBlocks().SelectMany(block => block.MethodsList))
        {
            foreach(var parameter in method.TypeParametersList)
            {
                Collect(parameter, signatures);
            }
        }

        if(signatures.Count == 0)
        {
            return;
        }

        var writer = new CSharpWriter();
        writer.WriteLine($"namespace {ConstructorConstraintInterfaces.Namespace};");
        writer.WriteBlankLine();

        var first = true;
        foreach(var (name, signature) in signatures.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if(!first)
            {
                writer.WriteBlankLine();
            }

            first = false;
            writer.WriteLine($"public interface {name}<{ConstructorConstraintInterfaces.TypeParameters(signature)}>");
            using(writer.Block())
            {
                var parameters = ConstructorConstraintInterfaces.DeclaredParameters(signature);
                writer.WriteLine(
                    $"static abstract T {ConstructorConstraintInterfaces.MemberName}({parameters});");
            }
        }

        var path = Path.Combine(outputDir, "__ConstructorConstraints.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void Collect(TypeParameter parameter, Dictionary<string, IReadOnlyList<IMistType>> signatures)
    {
        foreach(var signature in ConstraintClosureRegistry.Parameterized(parameter))
        {
            signatures[ConstructorConstraintInterfaces.InterfaceName(signature)] = signature;
        }
    }

    private IEnumerable<ExtensionBlock> EmittableBlocks()
    {
        return programModel.Assemblies
            .Where(assembly => assembly.HasMistSource)
            .SelectMany(assembly => assembly.CompilationUnits)
            .SelectMany(unit => unit.Extensions);
    }

    private IEnumerable<MemberType> EmittableTypes()
    {
        foreach(var assembly in programModel.Assemblies)
        {
            if(!assembly.HasMistSource)
            {
                continue;
            }

            foreach(var unit in assembly.CompilationUnits)
            {
                foreach(var type in unit.Types.OfType<MemberType>())
                {
                    yield return type;
                }
            }
        }
    }
}