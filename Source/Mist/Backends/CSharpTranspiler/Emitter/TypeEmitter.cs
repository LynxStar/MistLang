using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
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
    public static void EmitTypeBody(MemberType type, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var typeKeyword = type switch
        {
            AbstractType => "interface",
            StructType => "struct",
            ClassType => "class",
            _ => throw new InvalidOperationException($"ICE: unexpected MemberType kind '{type.GetType().Name}' in TypeEmitter"),
        };
        var accessibility = type.Modifiers.Accessibility == Accessibility.Public ? "public " : "";
        var typeName = EmitTypeName(type);

        // Every struct, not only the ones with a base: flattening is also what remaps a struct's
        // own protected members, which C# rejects outright.
        var flattened = type is StructType structType
            ? new StructFlattener(diagnostics).Flatten(structType)
            : null;

        if(type is StructType)
        {
            // Half of the offset guarantee FlattenedStruct.Fields describes; the field order is the
            // other half. Fully qualified rather than importing InteropServices, which would add a
            // using to every emitted file for the sake of the ones declaring a struct.
            writer.WriteLine(
                "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]");
        }

        writer.WriteLine($"{accessibility}{typeKeyword} {typeName}{EmitBaseList(type)}");
        ConstraintEmitter.Emit(type.TypeParametersList, writer);
        using(writer.Block())
        {
            var concrete = type as ConcreteType;
            var anyEmitted = false;

            // Fields first so they read as the type's state before the members that use
            // it, and because C# requires nothing of the order. Each is a single line, so
            // they run together as one block -- a blank line between every one of them
            // reads as noise rather than as separation.
            if(flattened is not null)
            {
                foreach(var field in flattened.Fields)
                {
                    FieldEmitter.Emit(field, writer, diagnostics);
                    anyEmitted = true;
                }
            }
            else
            {
                foreach(var field in concrete?.FieldsList ?? [])
                {
                    FieldEmitter.Emit(field, writer, diagnostics);
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

            foreach(var ancestor in flattened?.Ancestors ?? [])
            {
                SeparateMember(writer, ref anyEmitted);
                StructConversionEmitter.Emit((StructType)type, ancestor, writer, diagnostics);
            }

            foreach(var property in type.PropertiesList)
            {
                SeparateMember(writer, ref anyEmitted);
                PropertyEmitter.Emit(property, writer, diagnostics);
            }

            foreach(var indexer in type.IndexersList)
            {
                SeparateMember(writer, ref anyEmitted);
                IndexerEmitter.Emit(indexer, writer, diagnostics);
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
                    MethodEmitter.Emit(method, type.DeclarationNode, writer, diagnostics);
                }
            }

            foreach(var inherited in concrete is null ? [] : DefaultMemberDuplication.For(concrete, methods))
            {
                SeparateMember(writer, ref anyEmitted);
                EmitRestatedDefault(inherited, writer, diagnostics);
            }
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
    private static string EmitBaseList(MemberType type)
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