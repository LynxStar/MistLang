using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// V2 C# emitter that consumes an <see cref="IProgramModel"/> and produces one
/// compilable C# source file per Mist source file, then compiles them into a DLL
/// using <see cref="CSharpCompilationDriver"/>.
/// </summary>
internal sealed class CSharpEmitterV2
{
    private readonly IProgramModel programModel;

    private VariadicUseRegistry VariadicUses => ((ProgramModel.Model.ProgramModel)programModel).VariadicUses;
    private readonly CompilerOptions options;
    private readonly ImmutableArray<string> referencePaths;
    private readonly DiagnosticBag diagnostics;

    public CSharpEmitterV2(
        IProgramModel programModel,
        CompilerOptions options,
        ImmutableArray<string> referencePaths,
        DiagnosticBag diagnostics)
    {
        this.programModel = programModel;
        this.options = options;
        this.referencePaths = referencePaths;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Emits all compilation units to disk and compiles the result.
    /// <para>
    /// One C# file per Mist file, which is what preserves file scope: a file's using
    /// directives reach its own types and nothing else, exactly as they do in the source.
    /// Emitting per TYPE instead would either lose the usings or leak one file's imports
    /// into another's.
    /// </para>
    /// <para>
    /// Every assembly is emitted into a single CLR assembly, so a composed program contains
    /// one <c>Main</c> per project and Roslyn reports CS0017 unless told which to use. The
    /// compiled project's own entry point, recorded during member resolution, is what
    /// disambiguates. Once emission splits per assembly this becomes unnecessary; see
    /// <c>backlog/multi-project-emission.md</c>.
    /// </para>
    /// </summary>
    public CompilerResult Emit()
    {
        var isTopLevelEntry = options.EntryPoint == "top-level";

        // A top-level-statement entry point is C#'s to find: the statements compile into a
        // synthesized type whose name is not ours to predict, so naming one would be wrong.
        var entryPoint = isTopLevelEntry ? null : programModel.SelfAssembly?.EntryPoint;

        // An executable with no entry point cannot be produced. Reported here as a Mist
        // error rather than left to Roslyn, because it is the emitter that cannot do its
        // job and the Mist-level cause is clearer than the C# symptom. Duplicate entry
        // points are caught earlier, during member resolution.
        if(entryPoint is null && !isTopLevelEntry && options.OutputType == ProjectOutputType.Executable)
        {
            diagnostics.Report(
                BackendDiagnostics.MissingEntryPoint, default, options.ProjectName, (object?)options.ProjectName);
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        var outputDir = options.OutputDirectory;
        _ = Directory.CreateDirectory(outputDir);

        var emittedFiles = new List<string>();
        var closures = ConstraintClosureMap.Build((ProgramModel.Model.ProgramModel)programModel, diagnostics);

        ReportForeignConformances();

        EmitConstructorConstraintInterfaces(outputDir, emittedFiles);
        EmitStructInitMarker(outputDir, emittedFiles);
        EmitUnionCarriers(outputDir, emittedFiles);
        EmitDependentCarriers(outputDir, emittedFiles);
        EmitMonomorphs(outputDir, emittedFiles, closures);
        EmitVoidStruct(outputDir, emittedFiles);

        // The standard library is provided by the runtime, so its declarations are resolved
        // against but never emitted.
        foreach(var assembly in programModel.Assemblies)
        {
            if(!assembly.HasMistSource || assembly.Kind == MistAssemblyKind.Standard)
            {
                continue;
            }

            foreach(var unit in assembly.CompilationUnits)
            {
                EmitCompilationUnit(
                    unit, assembly, isTopLevelEntry, closures, outputDir, emittedFiles);
            }
        }

        var csprojContent = ProjectFileEmitter.Emit(options);
        var csprojPath = Path.Combine(outputDir, $"{options.ProjectName}.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        // Transpilation is done; compiling it is a separate step, and it must not run if
        // transpilation failed. Roslyn would answer with C# errors against generated files,
        // which describe symptoms in code the user never wrote and bury the Mist-level cause
        // that is already sitting in the bag. The pipeline gates Stages 4 and 5 before calling
        // here, so an error at this point is emission's own.
        if(diagnostics.HasErrors)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Compile the emitted sources using the in-process Roslyn driver.
        var binDir = options.BinDirectory;
        _ = Directory.CreateDirectory(binDir);

        var compileResult = CSharpCompilationDriver.Compile(
            outputDir,
            binDir,
            options.ProjectName,
            diagnostics,
            emittedFileCount: emittedFiles.Count,
            mainTypeName: entryPoint?.FullyQualifiedName,
            additionalReferencePaths: referencePaths,
            outputType: options.OutputType);

        if(!compileResult.Succeeded)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        return new CompilerResult(true, compileResult.PrimaryArtifactPath, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Emits a single compilation unit as one C# source file.
    /// </summary>
    private void EmitCompilationUnit(
        CompilationUnit unit,
        IAssembly assembly,
        bool isTopLevelEntry,
        ConstraintClosureMap closures,
        string outputDir,
        List<string> emittedFiles)
    {
        // A variadic family is not emitted where it was written: C# cannot express it once, so
        // EmitMonomorphs writes one declaration per arity instead and this skips the original.
        var emittableTypes = unit.Types
            .Where(TypeEmitter.ProducesDeclaration)
            .Where(type => Monomorph.PackOf(type) is null)
            .ToList();
        var emittableBlocks = EmittableBlocks(unit, assembly);

        // A file that declares nothing emittable would produce a .cs holding only usings,
        // which Roslyn accepts but which adds a file to the compilation for no reason.
        if(emittableTypes.Count == 0 && emittableBlocks.Count == 0 && !unit.HasTopLevelStatements)
        {
            return;
        }

        var writer = new CSharpWriter();

        EmitUsings(unit, assembly, writer);

        if(unit.HasTopLevelStatements && isTopLevelEntry)
        {
            EmitTopLevelStatements(unit, writer);
        }

        if(emittableTypes.Count > 0 || emittableBlocks.Count > 0)
        {
            if(unit.HasTopLevelStatements && isTopLevelEntry)
            {
                writer.WriteBlankLine();
            }

            EmitFileTypes(
                emittableTypes, emittableBlocks, unit.HasTopLevelStatements && isTopLevelEntry, closures, writer);
        }

        // Determine output path: {outputDir}/{Assembly.Name}/{RelativePath}/{FileName}.cs
        var fileName = DeriveOutputFileName(unit);
        var pathSegments = new List<string> { outputDir, assembly.Name };
        if(!string.IsNullOrEmpty(unit.RelativePath))
        {
            pathSegments.Add(unit.RelativePath);
        }

        pathSegments.Add(fileName);
        var fullPath = Path.Combine([.. pathSegments]);

        var dir = Path.GetDirectoryName(fullPath);
        if(dir is not null)
        {
            _ = Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, writer.ToString());
        emittedFiles.Add(fullPath);
    }

    /// <summary>
    /// Per program rather than per emitted type: a foreign conformer is never emitted, so a check
    /// placed at its declaration would never run.
    /// </summary>
    private void ReportForeignConformances()
    {
        foreach(var conformance in ((ProgramModel.Model.ProgramModel)programModel).ConformancesList)
        {
            if(conformance.ConformingType.Assembly.HasMistSource)
            {
                continue;
            }

            diagnostics.ReportAtNode(
                BackendDiagnostics.ForeignConformance,
                conformance.DeclarationNode,
                null,
                TypeDisplay.Mist(conformance.ConformingType),
                TypeDisplay.Mist(conformance.AbstractType));
        }
    }

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

        foreach(var union in interner.UnionTypes)
        {
            if(!OptionalUnion.IsOptional(union))
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
            EmitCarrierStruct(name, union, writer);
        }

        var path = Path.Combine(outputDir, "__Unions.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    /// <summary>
    /// One witness interface per dependent DOMAIN and one struct per distinct VALUE, top-level in a
    /// single generated file so a construction in any namespace can name them. C# has no dependent
    /// type parameter, so a value that must participate in type identity has to become a TYPE, and
    /// a struct implementing a static-abstract-member interface is the smallest thing that is one.
    /// <para>
    /// The interner is read rather than the emitted surface walked, for the reason the unions above
    /// are: it is already keyed on the domain and value together, so it holds each pair exactly
    /// once however many times the program writes it.
    /// </para>
    /// </summary>
    private void EmitDependentCarriers(string outputDir, List<string> emittedFiles)
    {
        var interner = ((ProgramModel.Model.ProgramModel)programModel).TypeInterner;
        var carriers = new SortedDictionary<string, (string Keyword, string Witness, object Value)>(
            StringComparer.Ordinal);
        var witnesses = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var witnessDomains = new Dictionary<string, IMistType>(StringComparer.Ordinal);

        // Every DECLARED domain, so a constraint never names an interface the file does not carry.
        // A family declared but never constructed still emits its `where`, so collecting only from
        // constructions would leave that clause dangling.
        foreach(var parameter in DependentParameters())
        {
            if(parameter.Domain is { } declared
                && DependentCarrierNames.Domain(declared) is { } domain
                && DependentCarrierNames.Witness(declared) is { } witness)
            {
                witnesses[witness] = domain.Keyword;
                witnessDomains[witness] = declared;
            }
        }

        // Every declared domain crossed with every value the program mentions. Deliberately more
        // than is reachable, because the two are cheap in opposite directions: a spare carrier is
        // an unreferenced struct in a generated file nobody reads, while a missing one is a build
        // failure against code the user never wrote.
        //
        // The alternative is to collect the pairs actually used, and the uses are not in one place
        // -- a constructed type holds its arguments, but a method call site holds its own, so
        // covering both means walking every expression in the program to save a few structs.
        //
        // THE DOMAIN COMES FROM THE SLOT, never from the value: every integral literal lifts as
        // int, so a 999 filling a `long` parameter would otherwise be carried by an IConstInt and
        // refused by the very constraint it was emitted for.
        foreach(var (witness, keyword) in witnesses.ToList())
        {
            foreach(var lifted in interner.LiftedTypes)
            {
                if(DependentCarrierNames.Fits(witnessDomains[witness], lifted.Value)
                    && DependentCarrierNames.Carrier(witnessDomains[witness], lifted) is { } name)
                {
                    carriers[name] = (keyword, witness, lifted.Value);
                }
            }
        }

        if(witnesses.Count == 0)
        {
            return;
        }

        var writer = new CSharpWriter();

        foreach(var (witness, keyword) in witnesses)
        {
            writer.WriteLine(
                $"public interface {witness} {{ static abstract {keyword} Value {{ get; }} }}");
        }

        foreach(var (name, carrier) in carriers)
        {
            writer.WriteBlankLine();
            writer.WriteLine(
                $"public readonly struct {name} : {carrier.Witness} "
                + $"{{ public static {carrier.Keyword} Value => {DependentCarrierNames.Literal(carrier.Value)}; }}");
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

    /// <summary>
    /// One C# declaration per (variadic family, arity) the program uses, in a single generated file.
    /// <para>
    /// MONOMORPHIZATION IS THE TARGET'S REQUIREMENT AND NOT THE LANGUAGE'S. Mist writes a variadic
    /// family once and its arity is unbounded; C# has no pack, so a program that used the family at
    /// two widths needs two declarations. Nothing about this reaches the model, which keeps the pack
    /// symbolic -- what changes is only what is WRITTEN, and only here.
    /// </para>
    /// <para>
    /// Emitted top-level rather than into the declaring namespace, as the union and dependent
    /// carriers are, so a construction in any namespace can name one.
    /// </para>
    /// </summary>
    private void EmitMonomorphs(string outputDir, List<string> emittedFiles, ConstraintClosureMap closures)
    {
        var interner = ((ProgramModel.Model.ProgramModel)programModel).TypeInterner;
        var specializations = MonomorphSet.Discover(interner);

        if(specializations.Count == 0)
        {
            return;
        }

        var writer = new CSharpWriter();
        var anyEmitted = false;

        foreach(var (definition, arity) in specializations)
        {
            if(definition is not MemberType member || Monomorph.For(Monomorph.PackIn(((IHasTypeParameters)definition).TypeParametersList), arity) is not { } monomorph)
            {
                continue;
            }

            using(monomorph.Enter())
            {
                TypeEmitter.Emit(member, closures, VariadicUses, writer, diagnostics, ref anyEmitted);
            }
        }

        if(!anyEmitted)
        {
            return;
        }

        var path = Path.Combine(outputDir, "__Variadics.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    /// <summary>
    /// The CLR shape of Standard.Void, generated because System.Void is refused in every
    /// position a unit type needs, and the stdlib's own Void.mist cannot be transpiled until
    /// properties and operator declarations emit -- at which point this method should be
    /// deleted in its favour. Members follow Void.mist; ToString and the equality overrides
    /// are backend additions so interpolation prints "Void" and the operators compile clean.
    /// </summary>
    private static void EmitVoidStruct(string outputDir, List<string> emittedFiles)
    {
        var writer = new CSharpWriter();
        writer.WriteLine("namespace Standard;");
        writer.WriteBlankLine();
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("public readonly struct Void : System.IEquatable<Void>");

        using(writer.Block())
        {
            writer.WriteLine("public static readonly Void Instance = default;");
            writer.WriteBlankLine();
            writer.WriteLine("public bool Equals(Void other) => true;");
            writer.WriteBlankLine();
            writer.WriteLine("public override bool Equals(object? obj) => obj is Void;");
            writer.WriteBlankLine();
            writer.WriteLine("public override int GetHashCode() => 0;");
            writer.WriteBlankLine();
            writer.WriteLine("public static bool operator ==(Void left, Void right) => true;");
            writer.WriteBlankLine();
            writer.WriteLine("public static bool operator !=(Void left, Void right) => false;");
            writer.WriteBlankLine();
            writer.WriteLine("public override string ToString() => \"Void\";");
            writer.WriteBlankLine();
            writer.WriteLine("public static implicit operator System.Type(Void v) => typeof(Void);");
        }

        var path = Path.Combine(outputDir, "__Void.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void EmitCarrierStruct(string name, UnionType union, CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine($"public readonly struct {name}");

        using(writer.Block())
        {
            writer.WriteLine("private readonly byte _tag;");
            writer.WriteLine("private readonly object _value;");
            writer.WriteBlankLine();
            writer.WriteLine($"private {name}(byte tag, object value)");

            using(writer.Block())
            {
                writer.WriteLine("_tag = tag;");
                writer.WriteLine("_value = value;");
            }

            for(var i = 0; i < union.Constituents.Count; i++)
            {
                var constituentName = UnionCarrierNames.ConstituentName(union.Constituents[i]);
                var typeName = TypeNameEmitter.Emit(union.Constituents[i]);
                var tag = i + 1;

                writer.WriteBlankLine();
                writer.WriteLine($"public bool Is{constituentName} => _tag == {tag};");
                writer.WriteBlankLine();
                writer.WriteLine($"public {typeName} As{constituentName} => _tag == {tag}");
                writer.WriteLine($"    ? ({typeName})_value");
                writer.WriteLine(
                    $"    : throw new System.InvalidOperationException(\"Union does not hold {constituentName}\");");
                writer.WriteBlankLine();
                writer.WriteLine(
                    $"public {TypeNameEmitter.EmitOptional(union.Constituents[i])} "
                    + $"{UnionCarrierNames.NarrowingAccessor(union.Constituents[i])} "
                    + $"=> _tag == {tag} ? ({typeName})_value : null;");
            }

            for(var i = 0; i < union.Constituents.Count; i++)
            {
                var typeName = TypeNameEmitter.Emit(union.Constituents[i]);
                var tag = i + 1;

                writer.WriteBlankLine();
                writer.WriteLine($"public static implicit operator {name}({typeName} value)");
                writer.WriteLine($"    => new {name}({tag}, value);");
            }
        }
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
            writer.WriteLine($"public interface {name}<T>");
            using(writer.Block())
            {
                var parameters = ConstructorConstraintInterfaces.Parameters(signature);
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

    private IEnumerable<MemberType> EmittableTypes()
    {
        foreach(var assembly in programModel.Assemblies)
        {
            if(!assembly.HasMistSource || assembly.Kind == MistAssemblyKind.Standard)
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

    /// <summary>
    /// A MANIFEST GLOBAL USING HAS TO BE EMITTED, not just resolved through. The file never spells
    /// it, and an ambient type reached through one emits unqualified -- TypeNameEmitter leaves every
    /// CLR name bare on the assumption the usings cover its namespace -- so without this the name
    /// arrives in C# with nothing to bind it to. Mist-declared types are unaffected either way,
    /// being emitted fully qualified, which is why the gap stayed invisible while only they used it.
    /// </summary>
    private static void EmitUsings(CompilationUnit unit, IAssembly assembly, CSharpWriter writer)
    {
        // Unconditional because TypeNameEmitter projects the Standard namespace onto C#
        // keywords and System types, so a file can reach System without naming it.
        var usings = new HashSet<string>(StringComparer.Ordinal) { "System" };

        foreach(var ns in unit.Usings.Concat(assembly.GlobalUsings))
        {
            // Standard is Mist's stdlib namespace and has no C# counterpart to import --
            // its types arrive through the System projection above.
            if(ns != "Standard")
            {
                _ = usings.Add(ns);
            }
        }

        foreach(var ns in usings.OrderBy(n => n, StringComparer.Ordinal))
        {
            writer.WriteLine($"using {ns};");
        }

        writer.WriteBlankLine();
    }

    private void EmitTopLevelStatements(CompilationUnit unit, CSharpWriter writer)
    {
        var declared = unit.Types
            .Select(type => type.NamespaceName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach(var name in declared)
        {
            writer.WriteLine($"using {name};");
        }

        if(declared.Count > 0)
        {
            writer.WriteBlankLine();
        }

        if(unit.TopLevelMethod?.Body is not null)
        {
            BodyEmitter.EmitBlock(unit.TopLevelMethod.Body, writer, diagnostics);
        }
    }

    /// <summary>
    /// A file's types under the namespaces that declare them, one Mist file to one C# file.
    /// <para>
    /// The file-scoped form can be written ONCE and must precede every other member, so it serves
    /// exactly the file that declares one namespace and no top-level statements. Anything else needs
    /// blocks: a second namespace has nowhere to go, and statements already emitted are members the
    /// file-scoped form may not follow.
    /// </para>
    /// </summary>
    /// <summary>
    /// The extension blocks this unit emits. The STANDARD LIBRARY'S ARE SKIPPED DELIBERATELY, not by
    /// accident: Standard/Unknown.mist declares <c>extension(unknown self)</c> whose GetType body is
    /// <c>return Type.Of(self);</c>, and Type.Of does not lower on v2 yet, so emitting that carrier
    /// puts a broken file into every single compilation. Named here rather than left to the fact that
    /// the stdlib assembly is otherwise skipped, because that fact is not what makes this safe.
    /// </summary>
    private static List<ExtensionBlock> EmittableBlocks(CompilationUnit unit, IAssembly assembly)
    {
        return assembly.Kind == MistAssemblyKind.Standard ? [] : unit.Extensions;
    }

    private void EmitFileTypes(
        List<DeclaredType> types,
        List<ExtensionBlock> blocks,
        bool hasTopLevelStatements,
        ConstraintClosureMap closures,
        CSharpWriter writer)
    {
        var namespaces = types.Select(type => type.NamespaceName)
            .Concat(blocks.Select(block => block.NamespaceName))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if(namespaces.Count == 1 && !hasTopLevelStatements)
        {
            writer.WriteLine($"namespace {namespaces[0]};");
            writer.WriteBlankLine();
            EmitDeclarations(types, blocks, closures, writer);
            return;
        }

        var first = true;
        foreach(var namespaceName in namespaces)
        {
            if(!first)
            {
                writer.WriteBlankLine();
            }

            first = false;
            writer.WriteLine($"namespace {namespaceName}");
            using(writer.Block())
            {
                EmitDeclarations(
                    types.Where(type => type.NamespaceName == namespaceName).ToList(),
                    blocks.Where(block => block.NamespaceName == namespaceName).ToList(),
                    closures,
                    writer);
            }
        }
    }

    private void EmitDeclarations(
        IEnumerable<DeclaredType> types,
        IEnumerable<ExtensionBlock> blocks,
        ConstraintClosureMap closures,
        CSharpWriter writer)
    {
        var anyEmitted = false;
        foreach(var type in types)
        {
            TypeEmitter.Emit(type, closures, VariadicUses, writer, diagnostics, ref anyEmitted);
        }

        foreach(var block in blocks)
        {
            ExtensionBlockEmitter.Emit(block, writer, diagnostics, ref anyEmitted);
        }
    }

    private static string DeriveOutputFileName(CompilationUnit unit)
    {
        return $"{Path.GetFileNameWithoutExtension(unit.SourcePath)}.cs";
    }
}