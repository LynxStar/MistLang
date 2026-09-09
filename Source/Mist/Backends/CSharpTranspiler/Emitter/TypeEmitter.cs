using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
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
        return type is (MemberType or EnumType or Newtype) and not ConcreteType { IsIntrinsic: true };
    }

    /// <summary>
    /// Total over every declaration kind, and it OWNS the blank line that precedes what it writes --
    /// so a kind that writes nothing leaves no separator behind and no caller has to ask first. An
    /// intrinsic is skipped because the backend supplies its implementation, and an alias because
    /// it names an existing type and its uses emit as that type.
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
        switch(type)
        {
            case ConcreteType { IsIntrinsic: true }:
            case TypeAlias:
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

        var typeKeyword = type switch
        {
            AbstractType => "interface",
            StructType => "struct",
            ClassType => "class",
            _ => throw new InvalidOperationException($"ICE: unexpected MemberType kind '{type.GetType().Name}' in TypeEmitter"),
        };
        var accessibility = type.Modifiers.Accessibility == Accessibility.Public ? "public " : "";
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
        SlotLift.ReportUnfilled(type, diagnostics);

        writer.WriteLine(
            $"{accessibility}{readonlyModifier}{refModifier}{typeKeyword} {typeName}{EmitBaseList(type, closedConstraints)}");
        ConstraintEmitter.Emit(Parameters(type), writer);
        using(writer.Block())
        {
            var concrete = type as ConcreteType;
            var anyEmitted = false;
            var inStruct = type is StructType;
            var inReadonlyType = type.IsReadonly;

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

            // Named before any constructor is emitted, because a constructor's chain call has to
            // name an initializer that is only emitted further down.
            var initNames = flattened is not null
                ? StructInitEmitter.BuildNames(flattened)
                : null;

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
                SeparateMember(writer, ref anyEmitted);
                PropertyEmitter.Emit(property, inStruct, writer, diagnostics);
            }

            SpreadProjectionEmitter.Emit(type, writer, ref anyEmitted);

            foreach(var indexer in type.IndexersList)
            {
                SeparateMember(writer, ref anyEmitted);
                IndexerEmitter.Emit(indexer, inStruct, writer, diagnostics);
            }

            var methods = flattened?.Methods ?? (IReadOnlyList<Method>)type.MethodsList;

            foreach(var method in methods)
            {
                // A method declaring its OWN pack is emitted once per width it was called at, for
                // the same reason its declaring type is: C# has no pack to write it once with. The
                // widths come from the call sites, since neither the declaration nor an inferred
                // call names one.
                foreach(var monomorph in MonomorphSet.MethodSpecializations(method, uses))
                {
                    using(monomorph.Enter())
                    {
                        SeparateMember(writer, ref anyEmitted);
                        EmitMethod(method, type, inStruct, writer, diagnostics);
                    }
                }

                if(Monomorph.PacksIn(method.TypeParametersList).Count > 0)
                {
                    continue;
                }

                SeparateMember(writer, ref anyEmitted);
                EmitMethod(method, type, inStruct, writer, diagnostics);
            }

            foreach(var castOperator in concrete?.CastOperatorsList ?? [])
            {
                SeparateMember(writer, ref anyEmitted);
                CastOperatorEmitter.Emit(castOperator, writer, diagnostics);
            }

            if(concrete is not null && DefaultSynthesis.IsDefaultable(type))
            {
                DefaultEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
            }

            foreach(var inherited in concrete is null ? [] : DefaultMemberDuplication.For(concrete, methods))
            {
                SeparateMember(writer, ref anyEmitted);
                EmitRestatedDefault(inherited, writer, diagnostics);
            }

            if(concrete is not null)
            {
                ConformanceEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
                SlotBridgeEmitter.Emit(concrete, writer, diagnostics, ref anyEmitted);
                IteratorBoilerplateEmitter.Emit(concrete, writer, ref anyEmitted);
            }

            foreach(var signature in closedConstraints)
            {
                SeparateMember(writer, ref anyEmitted);
                EmitConstraintFactory(type, signature, writer);
            }

            foreach(var nested in type.NestedTypes.OfType<DeclaredType>())
            {
                Emit(nested, closures, uses, writer, diagnostics, ref anyEmitted);
            }
        }
    }

    private static void EmitAncestorFactory(
        StructType type, FlattenedStruct flattened, CSharpWriter writer, ref bool anyEmitted)
    {
        var self = EmitTypeName(type);
        var members = StructAncestorFactory.Members(flattened);
        var parameters = StructAncestorFactory.Parameters(members);
        var arguments = StructAncestorFactory.Arguments(members);
        var name = StructAncestorFactory.Name(flattened);

        SeparateMember(writer, ref anyEmitted);
        writer.WriteLine($"private {CleanName(type)}({parameters})");
        using(writer.Block())
        {
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

    private static void EmitRestatedDefault(Method method, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.WriteLine($"public {MethodEmitter.EmitSignature(method, diagnostics)}");
        using(writer.Block())
        {
            BodyEmitter.EmitBlock(method.Body!, writer, diagnostics);
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
    private static string EmitBaseList(MemberType type, IReadOnlyList<IReadOnlyList<IMistType>> closedConstraints)
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

        foreach(var signature in closedConstraints)
        {
            bases.Add(ConstructorConstraintInterfaces.Closed(signature, EmitTypeName(type)));
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
        Method method, MemberType type, bool inStruct, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        MethodEmitter.ReportReadonlyRefReturn(method, type.DeclarationNode, diagnostics);

        if(type is AbstractType)
        {
            EmitInterfaceMethod(method, writer, diagnostics);
        }
        else
        {
            MethodEmitter.Emit(method, inStruct, writer, diagnostics);
        }
    }

    private static void EmitInterfaceMethod(Method method, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var signature = MethodEmitter.EmitSignature(method, diagnostics);

        if(method.Body is null)
        {
            writer.WriteLine($"{signature};");
            return;
        }

        writer.WriteLine(signature);
        using(writer.Block())
        {
            BodyEmitter.EmitBlock(method.Body, writer, diagnostics);
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
            : type.TypeParametersList;

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

        return MistType.StripArity(type.Name);
    }
}