using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits the members <c>default(T)</c> resolves through: an initializer holding each field's default,
/// and a factory returning an instance built from it. See <see cref="DefaultSynthesis"/> for which
/// types get them and why the sentinel exists.
/// </summary>
internal static class DefaultEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        using var witnessing = DefaultWitnesses.Witnessing(type);
        var self = TypeEmitter.EmitTypeName(type);
        var name = TypeEmitter.CleanName(type);
        var sentinel = DefaultSynthesis.UsesSentinel(type);
        var witnesses = DefaultWitnesses.FactoryNames(type);
        // The marker argument is TYPED rather than a bare `default`, which converts to every
        // one-parameter constructor and so leaves the call ambiguous on any type declaring one.
        var marker = $"default({DefaultSynthesis.MarkerType})";
        var chain = DefaultSynthesis.ChainsToBaseSentinel(type)
            ? $" : base({string.Join(", ", DefaultSynthesis.BaseWitnesses(type).Prepend(sentinel ? "marker" : marker))})"
            : "";

        if(sentinel && !DefaultSynthesis.NeedsSentinel(type))
        {
            SeparateMember(writer, ref anyEmitted);
            writer.WriteLine($"public {name}() : this({string.Join(", ", DefaultWitnesses.CompatibilityArguments(type, factory: true).Prepend(marker))})");
            using(writer.Block())
            {
            }

            SeparateMember(writer, ref anyEmitted);
            writer.WriteLine($"public {name}({string.Join(", ", DefaultWitnesses.FactoryParameters(type))}) : this({string.Join(", ", witnesses.Prepend(marker))})");
            using(writer.Block())
            {
            }
        }

        SeparateMember(writer, ref anyEmitted);

        if(sentinel)
        {
            var parameters = DefaultWitnesses.FactoryParameters(type).Prepend($"{DefaultSynthesis.MarkerType} marker");
            writer.WriteLine($"{DefaultSynthesis.SentinelAccessibility(type)} {name}({string.Join(", ", parameters)}){chain}");
        }
        else
        {
            writer.WriteLine($"public {name}(){chain}");
        }

        using(writer.Block())
        {
            DefaultWitnesses.EmitAssignments(type, writer);
            EmitAssignments(DefaultSynthesis.FlattenedAssignments(type), writer, diagnostics);
        }

        SeparateMember(writer, ref anyEmitted);
        var hiding = HidesParameterlessFactory(type) ? "new " : "";
        writer.WriteLine($"public {hiding}static {self} {DefaultSynthesis.FactoryName}({string.Join(", ", DefaultWitnesses.FactoryParameters(type))})");
        using(writer.Block())
        {
            var arguments = sentinel ? string.Join(", ", witnesses.Prepend(marker)) : "";
            writer.WriteLine($"return new {self}({arguments});");
        }
    }

    private static bool HidesParameterlessFactory(ConcreteType type)
    {
        if(type is not ClassType || DefaultWitnesses.FactoryParameters(type).Count != 0)
        {
            return false;
        }

        for(var written = type.BaseType;
            written is not null && Defaultability.DeclarationOf(written) is ClassType { Assembly.Kind: not MistAssemblyKind.Ambient } ancestor;
            written = ancestor.BaseType)
        {
            if(DefaultSynthesis.IsDefaultable(ancestor) && DefaultWitnesses.FactoryParameters(ancestor).Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static void EmitImplicitConstruction(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        var name = TypeEmitter.CleanName(type);
        var parameters = DefaultWitnesses.FactoryParameters(type);
        SeparateMember(writer, ref anyEmitted);
        writer.WriteLine($"public {name}() : this({string.Join(", ", DefaultWitnesses.CompatibilityArguments(type, factory: true))})");
        using(writer.Block())
        {
        }

        SeparateMember(writer, ref anyEmitted);
        writer.WriteLine($"public {name}({string.Join(", ", parameters)})");
        using(writer.Block())
        {
            DefaultWitnesses.EmitAssignments(type, writer);
            using var witnessing = DefaultWitnesses.Witnessing(type);
            EmitAssignments(DefaultSynthesis.FlattenedAssignments(type), writer, diagnostics);
        }
    }

    private static void EmitAssignments(List<DefaultAssignment> assignments, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var assignment in assignments)
        {
            var (slot, value, binding) = assignment;
            using var through = TypeEmitter.Substituting(assignment.Path);

            if(binding is not null)
            {
                EmitBinding(slot, binding, writer, diagnostics);
                continue;
            }

            // FieldEmitter and PropertyEmitter write the initializer onto the declaration, and C# runs
            // initializers in every constructor that does not chain to another -- this one included -- so
            // the slot already holds it here, and assigning it again would run the initializer twice.
            if(value is not null && ReferenceEquals(value, slot.Initializer) && !DefaultWitnesses.Deferred(value))
            {
                continue;
            }

            if(value is not null)
            {
                BodyEmitter.EmitSequenced(value, $"this.{slot.Name} = ", writer, diagnostics);
                continue;
            }

            writer.WriteLine($"this.{slot.Name} = {DefaultSynthesis.StorageZero(slot.Type)!};");
        }
    }

    /// <summary>
    /// A field bound to a concrete type: that type's own default, then the nested overrides, then the
    /// assignment. Built through a LOCAL rather than assigned and then overridden through the field,
    /// because the field's declared type is the abstract one the binding exists to widen from, and its
    /// members would not be reachable through it.
    /// </summary>
    private static void EmitBinding(
        InstanceStorage field, DefaultBindingEntry binding, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var local = $"__default_{field.Name}";

        writer.WriteLine(
            $"var {local} = {DefaultSynthesis.Expression(binding.BoundType!)};");

        foreach(var nested in binding.NestedEntries.OfType<DefaultValueEntry>())
        {
            BodyEmitter.EmitSequenced(nested.Value, $"{local}.{nested.FieldName} = ", writer, diagnostics);
        }

        writer.WriteLine($"this.{field.Name} = {local};");
    }

    private static void SeparateMember(CSharpWriter writer, ref bool anyEmitted)
    {
        if(anyEmitted)
        {
            writer.WriteBlankLine();
        }

        anyEmitted = true;
    }
}