using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class StructCopyEmitter
{
    public const string MethodName = "__CopyOwned";

    public static void EmitExpression(CloneExpression copy, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clones = CloneDemand.Clones!;
        var plan = OwnedStructCopies.Of(copy.Type, clones);
        var source = BodyEmitter.RenderAt(copy.Source, BodyEmitter.PostfixPrecedence, diagnostics);
        var counter = 0;
        if(plan.Kind == OwnedCopyKind.Struct)
        {
            writer.Append(Copy(source, plan, clones, ref counter));
        }
        else
        {
            var bound = BodyEmitter.TemporaryName();
            writer.Append($"({source} switch {{ var {bound} => {Copy(bound, plan, clones, ref counter)} }})");
        }
    }

    public static void Emit(StructType type, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clones = CloneDemand.Clones!;
        IMistType self = type.TypeParametersList.Count == 0 ? type : new ConstructedType(type, type.TypeParametersList);
        var plan = OwnedStructCopies.Of(TypeNameEmitter.Closed(self), clones);
        var slots = GenericStructConstruction.Slots(type, plan);
        var flattened = new StructFlattener(diagnostics).Flatten(type);
        writer.WriteLine($"public {DerivedCloneEmitter.SelfName(type)} {MethodName}()");

        using(writer.Block())
        {
            writer.WriteLine("var copy = this;");
            var counter = 0;

            foreach(var slot in slots)
            {
                var storage = slot.Storage;
                var name = storage.Field is { } field
                    ? flattened.Fields.First(candidate => ReferenceEquals(candidate.Field, field)).Name
                    : storage.Name;
                var target = storage.Property is null ? $"copy.{IdentifierSpelling.Of(name)}" : $"__OwnedBacking_{name}(ref copy)";
                var copied = Copy(target, slot.Copy, clones, ref counter);

                if(storage.Property is not null)
                {
                    writer.WriteLine($"__OwnedBacking_{name}(ref copy) = {copied};");
                }
                else
                {
                    writer.WriteLine(storage.Field!.Modifiers.IsReadonly || type.IsReadonly
                        ? $"System.Runtime.CompilerServices.Unsafe.AsRef(in {target}) = {copied};"
                        : $"{target} = {copied};");
                }
            }

            writer.WriteLine("return copy;");
        }

        foreach(var slot in slots.Where(slot => slot.Storage.Property is not null))
        {
            writer.WriteLine($"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = \"<{slot.Storage.Name}>k__BackingField\")]");
            writer.WriteLine($"private static extern ref {TypeNameEmitter.Emit(slot.Copy.Type)} __OwnedBacking_{slot.Storage.Name}(ref {DerivedCloneEmitter.SelfName(type)} target);");
        }

        if(!DerivedCloneEmitter.IsEmitted(type) && slots.Any(slot => slot.Copy.Kind != OwnedCopyKind.Generic && GenericCloneEmitter.NeedsHelper(slot.Copy.Type)))
        {
            GenericCloneEmitter.Emit(type, writer);
        }

        if(slots.Any(slot => slot.Copy.Kind == OwnedCopyKind.Generic))
        {
            GenericStructConstruction.Emit(writer);
        }
    }

    public static string Copy(string read, OwnedCopy plan, CloneDerivation clones, ref int counter)
    {
        switch(plan.Kind)
        {
            case OwnedCopyKind.Share:
                return read;

            case OwnedCopyKind.Generic:
                return $"__ConstructSlot({read})";

            case OwnedCopyKind.Clone:
                return DerivedCloneEmitter.Copy(read, plan.Type, clones, ref counter) ?? read;

            case OwnedCopyKind.Struct:
                return plan.Required ? $"{read}.{MethodName}()" : read;

            case OwnedCopyKind.Tuple:
                var elements = new List<string>();
                for(var i = 0; i < plan.Elements.Count; i++)
                {
                    elements.Add(Copy($"{read}.Item{i + 1}", plan.Elements[i], clones, ref counter));
                }

                return $"({string.Join(", ", elements)})";

            case OwnedCopyKind.Union when OptionalUnion.InnerType(plan.Type) is not null:
                var bound = $"__ownedPresent{counter++}";
                var element = plan.Elements.First(element => !OptionalUnion.IsVoid(element.Type));
                var spelled = TypeNameEmitter.Emit(plan.Type);
                return $"({read} is {{ }} {bound} ? ({spelled})({Copy(bound, element, clones, ref counter)}) : default({spelled}))";

            case OwnedCopyKind.Union:
                var carrier = TypeNameEmitter.Emit(plan.Type);
                var union = (UnionType)plan.Type;
                var arms = new List<string>();
                foreach(var alternative in plan.Elements.Where(element => element.Required))
                {
                    var name = UnionCarrierNames.ConstituentName(alternative.Type, union);
                    var copied = Copy($"{read}.As{name}", alternative, clones, ref counter);
                    var entered = UnionEntry.Factory(alternative.Type, union) is { } factory ? $"{factory}({copied})" : $"({carrier})({copied})";
                    arms.Add($"{read}.Is{name} ? {entered} : ");
                }

                return $"({string.Concat(arms)}{read})";

            default:
                throw new InvalidOperationException($"Unknown owned copy kind {plan.Kind}");
        }
    }
}