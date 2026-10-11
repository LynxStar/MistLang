using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits one class, struct or abstract as a C# type declaration, with its fields,
/// constructors and methods.
/// </summary>
internal static class TypeEmitter
{
    /// <summary>
    /// Narrows a file's declarations to the ones this backend writes, at the boundary where it takes
    /// its input. The model holds every declared kind on purpose, including the ones erased here, so
    /// the filtering belongs to the backend rather than to the model. Not a general question about a
    /// type -- <see cref="Emit"/> is total, and anything walking declarations should call that.
    /// </summary>
    public static bool ProducesDeclaration(DeclaredType type)
    {
        return type is MemberType or EnumType or Newtype && !SuppliedByBackend(type);
    }

    /// <summary>
    /// The declarations whose C# the target already has, so that emitting one would collide with it
    /// rather than add anything: an intrinsic type, whose implementation is the backend's by
    /// declaration and whose Mist-bodied members <see cref="IntrinsicMemberEmitter"/> carries
    /// elsewhere; Standard.Void, whose shape needs members Mist cannot spell (see
    /// <see cref="VoidStandIn"/>); and an abstract that IS a CLR interface, where a structurally
    /// identical duplicate would satisfy no framework API (see <see cref="ClrProjection"/>).
    /// </summary>
    private static bool SuppliedByBackend(DeclaredType type)
    {
        return type is ConcreteType { IsIntrinsic: true, CloneSource: null }
            || OptionalUnion.IsVoid(type)
            || (type is AbstractType && ClrProjection.Projects(type));
    }

    /// <summary>
    /// Total over every declaration kind, and it OWNS the blank line that precedes what it writes --
    /// so a kind that writes nothing leaves no separator behind and no caller has to ask first. A
    /// declaration <see cref="SuppliedByBackend"/> answers for is skipped, and an alias because it
    /// names an existing type and its uses emit as that type.
    /// <para>
    /// The arms that write must agree with <see cref="ProducesDeclaration"/>; the throwing default is
    /// what keeps that agreement honest, failing the first program that declares a kind only one of
    /// them knows.
    /// </para>
    /// </summary>
    public static void Emit(
        DeclaredType type,
        ConstraintClosureMap closures,
        VariadicUseRegistry uses,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        ref bool anyEmitted)
    {
        using var emitting = DelegateCeiling.Emitting(Declarations.NodeOf(type), MistType.StripArity(type.Name));

        switch(type)
        {
            case TypeAlias:
                break;

            case DeclaredType when SuppliedByBackend(type):
                break;

            case Newtype newtype:
                SeparateMember(writer, ref anyEmitted);
                NewtypeEmitter.Emit(newtype, writer, diagnostics);
                break;

            case EnumType enumType:
                SeparateMember(writer, ref anyEmitted);
                EnumEmitter.Emit(enumType, writer, diagnostics);
                break;

            case MemberType memberType:
                SeparateMember(writer, ref anyEmitted);
                UnspellableTypeGuard.Report(memberType, diagnostics);
                EmitTypeBody(memberType, closures, uses, writer, diagnostics);
                break;

            default:
                throw new InvalidOperationException(
                    $"ICE: unexpected DeclaredType kind '{type.GetType().Name}' in TypeEmitter.Emit");
        }
    }

    /// <summary>
    /// Emits one type declaration into an open file. The using directives and the namespace
    /// belong to the file, not the type, so <see cref="CSharpEmitterV2"/> writes those once per
    /// compilation unit and calls this for each type the unit declares.
    /// </summary>
    internal static void EmitTypeBody(
        MemberType type,
        ConstraintClosureMap closures,
        VariadicUseRegistry uses,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        var closedConstraints = closures.For(type);

        if(type.DeclaringType is null)
        {
            AccessorHolder.Forget();
        }

        var typeKeyword = type switch
        {
            AbstractType => "interface",
            StructType => "struct",
            ClassType => "class",
            _ => throw new InvalidOperationException($"ICE: unexpected MemberType kind '{type.GetType().Name}' in TypeEmitter"),
        };
        var accessibility = MemberModifierEmitter.Spell(type.Modifiers.Accessibility);
        var sealedModifier = type is ClassType && type.Modifiers.IsSealed ? "sealed " : "";
        var readonlyModifier = type.IsReadonly && type is StructType ? "readonly " : "";

        // C# fixes this pair's order, and a dropped `ref` is invisible downstream: the type still
        // compiles, runs and prints the same, it just stops being stack-only.
        var refModifier = type is StructType { IsRef: true } ? "ref " : "";
        var typeName = EmitTypeName(type);

        // Every struct, not only the ones with a base: flattening is also what remaps a struct's
        // own protected members, which C# rejects outright.
        FlattenedStruct? flattened = null;
        if(type is StructType structType)
        {
            StructFlattener.ReportIntrinsicBase(structType, diagnostics);
            flattened = new StructFlattener(diagnostics).Flatten(structType);
        }

        // Mist has no object base and no `override`, so a type declaring == has no Equals or
        // GetHashCode it could keep in step, and C#'s demand for them is the target's rather than the
        // program's. Suppressed instead of synthesized.
        var declaresEquality = type is ConcreteType { OperatorsList: var declared }
            && declared.Exists(op => op.Kind is OperatorKind.EqualEqual or OperatorKind.NotEqual);

        if(declaresEquality)
        {
            writer.WriteLine("#pragma warning disable CS0660, CS0661");
        }

        if(type is StructType)
        {
            // Half of the offset guarantee FlattenedStruct.Fields describes; the field order is the
            // other half. Fully qualified rather than importing InteropServices, which would add a
            // using to every emitted file for the sake of the ones declaring a struct.
            writer.WriteLine(
                "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]");
        }

        AttributeEmitter.EmitLines(type.DeclaredAttributesList, writer, diagnostics);

        SlotLift.ReportUnboundedParameterized(type, diagnostics);

        writer.WriteLine(
            $"{accessibility}{sealedModifier}{readonlyModifier}{refModifier}{typeKeyword} {typeName}{EmitBaseList(type, closedConstraints)}");
        ConstraintEmitter.Emit(Parameters(type), writer);
        using(writer.Block())
        {
            var concrete = type as ConcreteType;
            var anyEmitted = false;
            var inStruct = type is StructType;
            var inReadonlyType = type.IsReadonly;
            var readonlyMembers = ReadonlyEmission.For(type);

            if(concrete is not null)
            {
                DefaultWitnesses.EmitStorage(concrete, writer);
                anyEmitted = DefaultWitnesses.Stored(concrete).Count > 0 || ConstructorWitnesses.Stored(concrete).Count > 0;
            }

            // Fields first so they read as the type's state before the members that use
            // it, and because C# requires nothing of the order. Each is a single line, so
            // they run together as one block -- a blank line between every one of them
            // reads as noise rather than as separation.
            if(flattened is not null)
            {
                foreach(var field in flattened.Fields)
                {
                    FieldEmitter.Emit(field, inReadonlyType, writer, diagnostics);
                    anyEmitted = true;
                }
            }
            else
            {
                foreach(var field in concrete?.FieldsList ?? [])
                {
                    FieldEmitter.Emit(field, inReadonlyType, writer, diagnostics);
                    anyEmitted = true;
                }
            }

            foreach(var parameter in Parameters(type))
            {
                if(DependentCarrierNames.ValueProperty(parameter) is { } projection)
                {
                    writer.WriteLine(projection);
                    anyEmitted = true;
                }
            }

            // Named before any constructor is emitted, because a constructor's chain call has to
            // name an initializer that is only emitted further down.
            var initNames = flattened is not null
                ? StructInitEmitter.BuildNames(flattened)
                : null;

            if(IntrinsicCloneEmitter.Wraps(type))
            {
                SeparateMember(writer, ref anyEmitted);
                IntrinsicCloneEmitter.EmitWrapper(concrete!, writer);
            }

            foreach(var constructor in concrete?.ConstructorsList ?? [])
            {
                SeparateMember(writer, ref anyEmitted);
                ConstructorEmitter.Emit(constructor, type, writer, diagnostics, initNames);
            }

            foreach(var ancestor in flattened?.Ancestors ?? [])
            {
                StructInitEmitter.EmitFor(ancestor, initNames!, writer, diagnostics, ref anyEmitted);
            }

            if(flattened is not null)
            {
                EmitAncestorFactory((StructType)type, flattened, writer, ref anyEmitted);
            }

            foreach(var ancestor in flattened?.Ancestors ?? [])
            {
                SeparateMember(writer, ref anyEmitted);
                StructConversionEmitter.Emit((StructType)type, ancestor, writer, diagnostics);
            }

            var properties = flattened?.Properties ?? (IReadOnlyList<Property>)type.PropertiesList;

            foreach(var property in properties)
            {
                using var inherited = Substituting(flattened?.PropertyPaths.GetValueOrDefault(property));
                SeparateMember(writer, ref anyEmitted);
                PropertyEmitter.Emit(
                    property, readonlyMembers.OnProperty(property), readonlyMembers.OnGetterOf(property),
                    writer, diagnostics);
            }

            SpreadProjectionEmitter.Emit(type, flattened, writer, ref anyEmitted);

            foreach(var indexer in type.IndexersList)
            {
                SeparateMember(writer, ref anyEmitted);
                IndexerEmitter.Emit(
                    indexer, readonlyMembers.OnIndexer(indexer), readonlyMembers.OnGetterOf(indexer),
                    writer, diagnostics);
            }

            var methods = flattened?.Methods ?? (IReadOnlyList<Method>)type.MethodsList;

            foreach(var method in methods)
            {
                using var inherited = Substituting(flattened?.MethodPaths.GetValueOrDefault(method));

                if(UnionSpecialization.Specializes(method))
                {
                    foreach(var arguments in UnionSpecialization.InstancesOf(method))
                    {
                        using var specializing = Specializing(method, arguments);
                        SeparateMember(writer, ref anyEmitted);
                        EmitMethod(method, type, readonlyMembers.OnMethod(method), writer, diagnostics);
                    }

                    continue;
                }

                // A method declaring its OWN pack is emitted once per width it was called at, for
                // the same reason its declaring type is: C# has no pack to write it once with. The
                // widths come from the call sites, since neither the declaration nor an inferred
                // call names one.
                foreach(var monomorph in MonomorphSet.MethodSpecializations(method, uses))
                {
                    using(monomorph.Enter())
                    {
                        SeparateMember(writer, ref anyEmitted);
                        EmitMethod(method, type, readonlyMembers.OnMethod(method), writer, diagnostics);
                    }
                }

                if(Monomorph.PacksIn(method.TypeParametersList).Count > 0)
                {
                    continue;
                }

                SeparateMember(writer, ref anyEmitted);
                EmitMethod(method, type, readonlyMembers.OnMethod(method), writer, diagnostics);
            }

            foreach(var castOperator in concrete?.CastOperatorsList.Where(castOperator => castOperator is not { ClonesThrough: not null, IsCalled: false }) ?? [])
            {
                SeparateMember(writer, ref anyEmitted);
                CastOperatorEmitter.Emit(castOperator, writer, diagnostics);
            }

            foreach(var op in concrete?.OperatorsList ?? [])
            {
                SeparateMember(writer, ref anyEmitted);
                OperatorEmitter.Emit(op, writer, diagnostics);
            }

            if(concrete is not null && DefaultSynthesis.IsDefaultable(type))
            {
                DefaultEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
            }
            else if(concrete is not null && !DefaultSynthesis.NeedsSentinel(concrete) && DefaultWitnesses.FactoryParameters(concrete).Count > 0)
            {
                DefaultEmitter.EmitImplicitConstruction(concrete, writer, diagnostics, ref anyEmitted);
            }

            if(concrete is not null && DropInsertion.SynthesizesDrop(concrete))
            {
                DropEmitter.Emit(concrete, writer, ref anyEmitted);
            }

            foreach(var inherited in concrete is null ? [] : DefaultMemberDuplication.For(concrete, methods))
            {
                using var through = Substituting(inherited.Path);
                SeparateMember(writer, ref anyEmitted);
                EmitRestatedDefault(inherited.Method, writer, diagnostics, owner: type);
            }

            if(concrete is not null)
            {
                ConformanceEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
                AsyncSlotBridgeEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
                ConstraintSlotBridgeEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
                SlotBridgeEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
                PackSlotBridgeEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
                IteratorBoilerplateEmitter.Emit(concrete, writer, ref anyEmitted);
            }

            foreach(var (witness, i) in closedConstraints.Select((witness, i) => (witness, i)))
            {
                if(closedConstraints.Take(i).Any(earlier => ConstraintClosureMap.Same(earlier.Closed, witness.Closed)))
                {
                    continue;
                }

                SeparateMember(writer, ref anyEmitted);
                EmitConstraintFactory(type, witness.Closed, writer);
            }

            foreach(var nested in type.NestedTypes.OfType<DeclaredType>())
            {
                Emit(nested, closures, uses, writer, diagnostics, ref anyEmitted);
            }

            AccessorHolder.WritePending(type, writer);
        }

        if(declaresEquality)
        {
            writer.WriteLine("#pragma warning restore CS0660, CS0661");
        }

        if(type.DeclaringType is null)
        {
            AccessorHolder.WritePending(home: null, writer);
        }
    }

    /// <summary>
    /// The spelling scope an inherited member is emitted in, or nothing for a member reached through
    /// no generic base.
    /// </summary>
    public static IDisposable? Substituting(IReadOnlyList<ConstructedType>? path)
    {
        return path is { Count: > 0 }
            ? TypeNameEmitter.Substituting(TypeSubstitution.ForPath(receiver: null, path, interner: null))
            : null;
    }

    private static void EmitAncestorFactory(
        StructType type, FlattenedStruct flattened, CSharpWriter writer, ref bool anyEmitted)
    {
        var self = EmitTypeName(type);
        var members = StructAncestorFactory.Members(flattened);
        var defaults = DefaultWitnesses.Stored(type);
        var constructors = ConstructorWitnesses.Stored(type);
        var parameters = string.Join(", ", defaults.Select(DefaultConstraintWitness.Parameter)
            .Concat(ConstructorWitnesses.Parameters(constructors)).Append(StructAncestorFactory.Parameters(members)));
        var arguments = string.Join(", ", defaults.Select(DefaultConstraintWitness.Name)
            .Concat(ConstructorWitnesses.Names(constructors)).Append(StructAncestorFactory.Arguments(members)));
        var name = StructAncestorFactory.Name(flattened);

        SeparateMember(writer, ref anyEmitted);
        writer.WriteLine($"private {CleanName(type)}({parameters})");
        using(writer.Block())
        {
            DefaultWitnesses.EmitAssignments(type, writer);
            for(var i = 0; i < members.Length; i++)
            {
                writer.WriteLine($"this.{members[i].Name} = p{i};");
            }
        }

        SeparateMember(writer, ref anyEmitted);
        writer.WriteLine($"internal static {self} {name}({parameters})");
        using(writer.Block())
        {
            writer.WriteLine($"return new {self}({arguments});");
        }
    }

    /// <summary>
    /// The static abstract member that satisfies a synthesized constructor constraint interface: an
    /// ordinary constructor call, which is what lets the constraint be granted without the CLR
    /// needing a way to invoke a type parameter's constructor.
    /// </summary>
    private static void EmitConstraintFactory(
        MemberType type, IReadOnlyList<IMistType> signature, CSharpWriter writer)
    {
        var self = EmitTypeName(type);
        var parameters = ConstructorConstraintInterfaces.Parameters(signature);
        var arguments = ConstructorConstraintInterfaces.Arguments(signature);

        writer.WriteLine($"public static {self} {ConstructorConstraintInterfaces.MemberName}({parameters})");
        using(writer.Block())
        {
            writer.WriteLine($"return new {self}({arguments});");
        }
    }

    internal static void EmitRestatedDefault(Method method, CSharpWriter writer, DiagnosticBag diagnostics, MemberType? owner = null)
    {
        writer.WriteLine($"public {(method.Modifiers.IsAsync ? "async " : "")}{MethodEmitter.EmitSignature(method, diagnostics)}");
        using(writer.Block())
        {
            EmitDefaultBody(method, writer, diagnostics, owner);
        }
    }

    /// <summary>
    /// The <c>: Base, IFirst, ISecond</c> clause, or empty when the type has neither a base nor an
    /// abstract to implement.
    /// <para>
    /// The concrete base comes first because C# requires it to. Mist keeps the two apart --
    /// <see cref="ConcreteType.BaseType"/> against <see cref="MemberType.BaseAbstractTypes"/> --
    /// so the ordering does not depend on how the declaration was written.
    /// </para>
    /// </summary>
    private static string EmitBaseList(MemberType type, IReadOnlyList<ConstructorWitness> closedConstraints)
    {
        var bases = new List<string>();

        // A struct's base is flattened in, and naming it here would have C# read it as an
        // interface for the struct to implement.
        if(type is ConcreteType { BaseType: { } baseType } and not StructType)
        {
            bases.Add(TypeNameEmitter.Emit(baseType));
        }

        foreach(var abstractRef in SlotLift.Reached(type))
        {
            bases.Add(TypeNameEmitter.Emit(SlotLift.Base(abstractRef, type) ?? abstractRef));
        }

        foreach(var witness in closedConstraints)
        {
            bases.Add(ConstructorConstraintInterfaces.Closed(witness.Signature, witness.Closed, EmitTypeName(type)));
        }

        return bases.Count > 0 ? $" : {string.Join(", ", bases)}" : "";
    }

    /// <summary>
    /// Writes the blank line that precedes a block-bodied member, unless that member opens
    /// the type body. Called per member rather than per section, so it also supplies the
    /// break between the field block and whatever follows it.
    /// </summary>
    internal static void SeparateMember(CSharpWriter writer, ref bool anyEmitted)
    {
        if(anyEmitted)
        {
            writer.WriteBlankLine();
        }

        anyEmitted = true;
    }

    /// <summary>
    /// A member of an abstract type, with no accessibility modifier because C# interface members
    /// are implicitly public.
    /// <para>
    /// A body makes this a default implementation rather than a slot, and needs no lowering: a
    /// C# default interface method is reachable on exactly the abstract-typed references Mist
    /// expects it on. Reaching it through a concrete-typed reference is what
    /// <c>default-member-forwarding-stubs</c> is for, and it is a separate capability.
    /// </para>
    /// </summary>
    private static void EmitMethod(
        Method method, MemberType type, bool emitReadonly, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        using var emitting = DelegateCeiling.Emitting(method.DeclarationNode, $"{MistType.StripArity(type.Name)}.{method.Name}");
        MethodEmitter.ReportReadonlyRefReturn(method, type.DeclarationNode, diagnostics);

        if(type is AbstractType)
        {
            EmitInterfaceMethod(method, writer, diagnostics);
        }
        else
        {
            MethodEmitter.Emit(method, emitReadonly, writer, diagnostics, type);
        }
    }

    internal static void EmitInterfaceMethod(Method method, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var signature = MethodEmitter.EmitSignature(method, diagnostics);
        var constraints = MethodEmitter.ConstraintLines(method);

        if(method.Body is null)
        {
            writer.WriteLine(constraints.Count == 0 ? $"{signature};" : signature);
            writer.Indent();

            for(var i = 0; i < constraints.Count; i++)
            {
                writer.WriteLine(i == constraints.Count - 1 ? $"{constraints[i]};" : constraints[i]);
            }

            writer.Dedent();
            return;
        }

        writer.WriteLine($"{(method.Modifiers.IsAsync ? "async " : "")}{signature}");
        writer.Indent();

        foreach(var constraint in constraints)
        {
            writer.WriteLine(constraint);
        }

        writer.Dedent();
        using(writer.Block())
        {
            EmitDefaultBody(method, writer, diagnostics);
        }
    }

    private static void EmitDefaultBody(Method method, CSharpWriter writer, DiagnosticBag diagnostics, MemberType? owner = null)
    {
        if(method.Modifiers.IsAsync)
        {
            BodyEmitter.EmitMethodBody(method, method.Body!, writer, diagnostics, owner: owner);
        }
        else
        {
            BodyEmitter.EmitSynchronousMethodBody(method, method.Body!, writer, diagnostics, owner: owner);
        }
    }

    /// <summary>
    /// The C# spelling of the declaration's name: <c>GenericBox&lt;T&gt;</c>, naming the
    /// parameters that <see cref="CleanName"/> strips the placeholders for.
    /// </summary>
    internal static string EmitTypeName(MemberType type)
    {
        var parameters = Parameters(type);

        if(parameters.Count == 0)
        {
            return CleanName(type);
        }

        var baseName = CleanName(type);
        var typeParams = string.Join(", ", parameters.Select(DependentCarrierNames.Parameter));
        return $"{baseName}<{typeParams}>";
    }

    /// <summary>
    /// The type parameters the C# declaration carries: those written in Mist, then the ones
    /// <see cref="SlotLift"/> adds. Appending keeps a written argument at the position it was
    /// written, so lifting a slot onto an already-generic abstract does not move <c>TIn</c>.
    /// </summary>
    internal static IReadOnlyList<TypeParameter> Parameters(MemberType type)
    {
        var lifted = SlotLift.Lifted(type);

        // A specialization declares the pack's expansion in the pack's place, which is what makes
        // it a C# generic of a definite width.
        var declared = Monomorph.InScope() is { } monomorph && monomorph.Owns(type.TypeParametersList)
            ? monomorph.Parameters(type.TypeParametersList)
            : IsSpecializing(type) ? [] : type.TypeParametersList;

        return lifted.Count == 0 ? declared : [.. declared, .. lifted];
    }

    /// <summary>
    /// The name a C# declaration is emitted under, via <see cref="MistType.StripArity"/>.
    /// <para>
    /// The registered name is not a C# identifier, so nothing may emit it verbatim -- a
    /// declaration names its parameters instead (<see cref="EmitTypeName"/>) and a constructor
    /// names neither.
    /// </para>
    /// </summary>
    internal static string CleanName(MemberType type)
    {
        // A specialization's name carries its arity, and every member that WRITES the type name
        // rather than referring to it -- a constructor above all -- has to use the same one.
        if(Monomorph.InScope() is { } monomorph && monomorph.Owns(type.TypeParametersList))
        {
            return MonomorphSet.SpecializedName(type.Name, monomorph.Widths);
        }

        if(IsSpecializing(type))
        {
            return UnionSpecialization.Name(UnionSpecialization.Current()!);
        }

        return MistType.StripArity(type.Name);
    }

    internal static IDisposable Specializing(ConstructedType instance)
    {
        return new Both(UnionSpecialization.For(instance), TypeNameEmitter.Substituting(TypeSubstitution.ForReceiver(instance)));
    }

    private static IDisposable Specializing(Method method, IReadOnlyList<IMistType> arguments)
    {
        return new Both(
            UnionSpecialization.For(method, arguments),
            TypeNameEmitter.Substituting(TypeSubstitution.ForMethod(method, arguments)));
    }

    private static bool IsSpecializing(MemberType type)
    {
        return ReferenceEquals(UnionSpecialization.Current()?.Definition, type);
    }

    private sealed class Both(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
    }
}