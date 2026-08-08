using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
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
    /// Emits one type declaration into an open file. The using directives and the namespace
    /// belong to the file, not the type, so <see cref="CSharpEmitterV2"/> writes those once per
    /// compilation unit and calls this for each type the unit declares.
    /// </summary>
    public static void EmitTypeBody(
        MemberType type, ConstraintClosureMap closures, CSharpWriter writer, DiagnosticBag diagnostics)
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

        writer.WriteLine(
            $"{accessibility}{readonlyModifier}{typeKeyword} {typeName}{EmitBaseList(type, closedConstraints)}");
        ConstraintEmitter.Emit(type.TypeParametersList, writer);
        using(writer.Block())
        {
            var concrete = type as ConcreteType;
            var anyEmitted = false;
            var inStruct = type is StructType;
            var inReadonlyStruct = inStruct && type.IsReadonly;

            // Fields first so they read as the type's state before the members that use
            // it, and because C# requires nothing of the order. Each is a single line, so
            // they run together as one block -- a blank line between every one of them
            // reads as noise rather than as separation.
            if(flattened is not null)
            {
                foreach(var field in flattened.Fields)
                {
                    FieldEmitter.Emit(field, inReadonlyStruct, writer, diagnostics);
                    anyEmitted = true;
                }
            }
            else
            {
                foreach(var field in concrete?.FieldsList ?? [])
                {
                    FieldEmitter.Emit(field, inReadonlyStruct, writer, diagnostics);
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

            foreach(var indexer in type.IndexersList)
            {
                SeparateMember(writer, ref anyEmitted);
                IndexerEmitter.Emit(indexer, inStruct, writer, diagnostics);
            }

            var methods = flattened?.Methods ?? (IReadOnlyList<Method>)type.MethodsList;

            foreach(var method in methods)
            {
                SeparateMember(writer, ref anyEmitted);
                if(type is AbstractType)
                {
                    EmitInterfaceMethod(method, writer, diagnostics);
                }
                else
                {
                    MethodEmitter.Emit(method, type.DeclarationNode, inStruct, writer, diagnostics);
                }
            }

            foreach(var castOperator in concrete?.CastOperatorsList ?? [])
            {
                SeparateMember(writer, ref anyEmitted);
                CastOperatorEmitter.Emit(castOperator, type.DeclarationNode, writer, diagnostics);
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

            foreach(var signature in closedConstraints)
            {
                SeparateMember(writer, ref anyEmitted);
                EmitConstraintFactory(type, signature, writer);
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
        writer.WriteLine($"public {MethodEmitter.EmitSignature(method)}");
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

        foreach(var abstractBase in type.BaseAbstractTypes)
        {
            bases.Add(TypeNameEmitter.Emit(abstractBase));
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
    private static void EmitInterfaceMethod(Method method, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var signature = MethodEmitter.EmitSignature(method);

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
        if(type.TypeParametersList.Count == 0)
        {
            return type.Name;
        }

        var baseName = CleanName(type);
        var typeParams = string.Join(", ", type.TypeParametersList.Select(tp => tp.Name));
        return $"{baseName}<{typeParams}>";
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
        return MistType.StripArity(type.Name);
    }
}