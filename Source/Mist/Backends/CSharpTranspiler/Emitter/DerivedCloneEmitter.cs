using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The C# of a derived <c>Clone</c> (ownership-v2 Requirements 8.9 and 8.10). A class is copied by
/// <c>MemberwiseClone</c>, which keeps the value's actual class, and each class in its chain then replaces what its
/// own slots hold with copies, through a helper it declares so that a private or readonly slot is reached by the class
/// that owns it. Derivation admits no object reachable twice, so nothing records what was already copied.
/// </summary>
internal static class DerivedCloneEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clones = CloneDemand.Clones!;
        var self = TypeNameEmitter.Emit(type.DerivedClone!.ReturnType);

        if(type is StructType structType)
        {
            writer.WriteLine($"public {self} Clone()");

            using(writer.Block())
            {
                writer.WriteLine("var copy = this;");
                var flattened = new StructFlattener(diagnostics).Flatten(structType);

                foreach(var field in flattened.Fields.Where(field => !field.Field.Modifiers.IsStatic))
                {
                    var counter = 0;

                    if(Copy($"copy.{IdentifierSpelling.Of(field.Name)}", field.Type, clones, ref counter) is { } copied)
                    {
                        writer.WriteLine(Write($"copy.{IdentifierSpelling.Of(field.Name)}", copied, field.Field.Modifiers.IsReadonly || structType.IsReadonly));
                    }
                }

                foreach(var slot in InstanceStorage.Of(structType).Where(slot => slot.Property is not null))
                {
                    var counter = 0;

                    if(Copy($"copy.{IdentifierSpelling.Of(slot.Name)}", slot.Type, clones, ref counter) is { } copied)
                    {
                        writer.WriteLine($"{BackingName(slot)}(ref copy) = {copied};");
                    }
                }

                writer.WriteLine("return copy;");
            }

            EmitBackingAccessors(structType, self, writer);
            return;
        }

        var overrides = CloneDemand.Overrides(type);
        var modifier = overrides ? "override " : !type.Modifiers.IsSealed && CloneDemand.IsOverridden(type) ? "virtual " : "";

        writer.WriteLine($"public {modifier}{self} Clone()");

        using(writer.Block())
        {
            writer.WriteLine(overrides ? $"var copy = ({self})base.Clone();" : $"var copy = ({self})MemberwiseClone();");

            var chain = new List<ConcreteType>();

            for(var current = type; current is not null && (chain.Count == 0 || !overrides); current = (current.BaseType is ConstructedType constructed ? constructed.Definition : current.BaseType) as ConcreteType)
            {
                chain.Insert(0, current);
            }

            foreach(var link in chain.Where(CloneDemand.CopiesSlotsOf))
            {
                writer.WriteLine($"copy.{HelperName(link)}();");
            }

            writer.WriteLine("return copy;");
        }
    }

    public static void EmitSlotCopier(ConcreteType type, CSharpWriter writer)
    {
        var clones = CloneDemand.Clones!;
        var self = TypeNameEmitter.Emit(type.TypeParametersList.Count == 0 ? type : new ConstructedType(type, type.TypeParametersList));

        writer.WriteLine($"{(type.Modifiers.IsSealed ? "private" : "protected")} void {HelperName(type)}()");

        using(writer.Block())
        {
            foreach(var slot in CloneDemand.SlotsNeedingCopy(type, clones))
            {
                var counter = 0;
                var read = $"this.{IdentifierSpelling.Of(slot.Name)}";

                if(Copy(read, slot.Type, clones, ref counter) is not { } copied)
                {
                    continue;
                }

                writer.WriteLine(slot.Field is { } field
                    ? Write(read, copied, field.Modifiers.IsReadonly || type.IsReadonly)
                    : $"{BackingName(slot)}(this) = {copied};");
            }
        }

        EmitBackingAccessors(type, self, writer);
    }

    private static void EmitBackingAccessors(ConcreteType type, string self, CSharpWriter writer)
    {
        var clones = CloneDemand.Clones!;

        foreach(var slot in CloneDemand.SlotsNeedingCopy(type, clones).Where(slot => slot.Property is not null))
        {
            writer.WriteBlankLine();
            writer.WriteLine($"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = \"<{slot.Name}>k__BackingField\")]");
            writer.WriteLine($"private static extern ref {TypeNameEmitter.Emit(slot.Type)} {BackingName(slot)}({(type is StructType ? "ref " : "")}{self} target);");
        }
    }

    private static string Write(string target, string copied, bool isReadonly)
    {
        return isReadonly
            ? $"System.Runtime.CompilerServices.Unsafe.AsRef(in {target}) = {copied};"
            : $"{target} = {copied};";
    }

    private static string HelperName(ConcreteType type)
    {
        return $"__CloneSlots_{MistType.StripArity(type.Name)}";
    }

    private static string BackingName(InstanceStorage slot)
    {
        return $"__Backing_{slot.Name}";
    }

    private static string? Copy(string read, IMistType type, CloneDerivation clones, ref int counter)
    {
        type = type switch
        {
            TypeAlias { AliasedType: { } aliased } => aliased,
            RefinedType refined => refined.BaseType,
            _ => type,
        };

        if(clones.Judge(type).Kind == CloneKind.Unchangeable)
        {
            return null;
        }

        switch(type)
        {
            case TypeParameter parameter:
                {
                    var bound = $"__clonable{counter++}";
                    var clonable = TypeNameEmitter.Emit(new ConstructedType(Clonable(), [parameter]));

                    return $"({read} is {clonable} {bound} ? {bound}.Clone() : {read})";
                }

            case UnionType union when OptionalUnion.InnerType(union) is { } inner:
                {
                    var bound = $"__present{counter++}";
                    var spelled = TypeNameEmitter.Emit(union);

                    return Copy(bound, inner, clones, ref counter) is { } copied
                        ? $"({read} is {{ }} {bound} ? ({spelled})({copied}) : default({spelled}))"
                        : null;
                }

            case UnionType union:
                {
                    var carrier = TypeNameEmitter.Emit(union);
                    var arms = new List<string>();

                    foreach(var constituent in union.Constituents)
                    {
                        var name = UnionCarrierNames.ConstituentName(constituent, union);

                        if(Copy($"{read}.As{name}", constituent, clones, ref counter) is { } copied)
                        {
                            var entered = UnionEntry.Factory(constituent, union) is { } factory ? $"{factory}({copied})" : $"({carrier})({copied})";
                            arms.Add($"{read}.Is{name} ? {entered} : ");
                        }
                    }

                    return arms.Count == 0 ? null : $"({string.Concat(arms)}{read})";
                }

            case ITupleType tuple:
                {
                    var elements = new List<string>();
                    var any = false;

                    for(var i = 0; i < tuple.ElementTypes.Count; i++)
                    {
                        var element = $"{read}.Item{i + 1}";
                        var copied = Copy(element, tuple.ElementTypes[i], clones, ref counter);
                        any |= copied is not null;
                        elements.Add(copied ?? element);
                    }

                    return any ? $"({string.Join(", ", elements)})" : null;
                }
        }

        return TypeNameEmitter.IsReferenceKind(type) ? $"{read}?.Clone()!" : $"{read}.Clone()";
    }

    private static AbstractType Clonable()
    {
        return CloneDemand.ClonableAbstract
            ?? throw new InvalidOperationException("ICE: a derived Clone over a type parameter reached emission without Standard's IClonable.");
    }
}