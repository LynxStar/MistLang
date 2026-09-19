using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The declarations <see cref="UnsafeReach"/> calls through, each declared once per conversion however often
/// the body reaches it. A member of a generic type is declared in an <see cref="AccessorHolder"/> over that
/// type's own parameters, for the reason given there, and called on the construction the body reaches --
/// unless that type is emitted per instantiation (<see cref="UnionSpecialization"/>), when the construction
/// is a closed C# type and is named directly.
/// </summary>
internal sealed class UnsafeAccessors(MemberType home, string prefix, DiagnosticBag diagnostics)
{
    private const string Attribute = "[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind";

    private readonly Dictionary<object, string> names = [];
    private readonly List<IReadOnlyList<string>> declarations = [];
    private readonly Dictionary<MemberType, List<(string Name, string[] Lines)>> holders = [];

    public IReadOnlyList<IReadOnlyList<string>> Declarations => declarations;

    public IEnumerable<AccessorHolder> Holders => holders.Select(
        holder => new AccessorHolder(HolderName(holder.Key), holder.Key, [.. holder.Value.SelectMany(member => member.Lines)]));

    public string Field(Field field, IMistType owner)
    {
        return Storage(field, field.Name, field.Name, field.Type, field.DeclaringType!, owner, field.Modifiers.IsStatic);
    }

    public string BackingField(Property property, IMistType owner)
    {
        return Storage(
            property, $"<{property.Name}>k__BackingField", property.Name, property.Type, property.DeclaringType!, owner, property.Modifiers.IsStatic);
    }

    public string Property(Property property, IMistType owner, bool isSetter)
    {
        var declaring = property.DeclaringType!;
        var accessor = $"{(isSetter ? "set" : "get")}_{property.Name}";
        var kind = property.Modifiers.IsStatic ? "StaticMethod" : "Method";

        return Declare((property, isSetter), declaring, owner, accessor, (accessibility, name) =>
        [
            $"{Attribute}.{kind}, Name = \"{accessor}\")]",
            isSetter
                ? $"{accessibility} static extern void {name}({Target(declaring, property.Modifiers.IsStatic)}, {TypeNameEmitter.Emit(property.Type)} value);"
                : $"{accessibility} static extern {TypeNameEmitter.Emit(property.Type)} {name}({Target(declaring, property.Modifiers.IsStatic)});",
        ]);
    }

    public string Indexer(Indexer indexer, IMistType owner, bool isSetter)
    {
        var declaring = indexer.DeclaringType!;
        var accessor = isSetter ? "set_Item" : "get_Item";
        return Declare((indexer, isSetter), declaring, owner, accessor, (accessibility, name) =>
        [
            $"{Attribute}.Method, Name = \"{accessor}\")]",
            isSetter
                ? $"{accessibility} static extern void {name}({Target(declaring, false)}, {ParameterEmitter.Emit(indexer.ParametersList, diagnostics)}, {TypeNameEmitter.Emit(indexer.Type)} value);"
                : $"{accessibility} static extern {TypeNameEmitter.Emit(indexer.Type)} {name}({Target(declaring, false)}, {ParameterEmitter.Emit(indexer.ParametersList, diagnostics)});",
        ]);
    }

    public string Method(Method method, IMistType owner)
    {
        var declaring = method.DeclaringType!;
        var kind = method.Modifiers.IsStatic ? "StaticMethod" : "Method";
        var typeParameters = method.TypeParametersList.Count == 0
            ? ""
            : $"<{string.Join(", ", method.TypeParametersList.Select(TypeNameEmitter.Emit))}>";

        return Declare(method, declaring, owner, method.Name, (accessibility, name) =>
        {
            var constraints = new CSharpWriter();
            ConstraintEmitter.Emit(method.TypeParametersList, constraints, liftSlots: true);
            var parameters = method.ParametersList.Count == 0 ? "" : ", " + ParameterEmitter.Emit(method.ParametersList, diagnostics);

            string[] lines =
            [
                $"{Attribute}.{kind}, Name = \"{UnionSpecialization.CollidingName(method, TypeNameEmitter.Closed(owner)) ?? method.Name}\")]",
                $"{accessibility} static extern {MethodEmitter.EmitReturnType(method)} {name}{typeParameters}({Target(declaring, method.Modifiers.IsStatic)}{parameters})",
                .. constraints.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd()),
            ];

            lines[^1] += ";";
            return lines;
        });
    }

    public string Constructor(Constructor constructor, ConcreteType declaring, IMistType created)
    {
        return Declare(constructor, declaring, created, "new", (accessibility, name) =>
        [
            $"{Attribute}.Constructor)]",
            $"{accessibility} static extern {TypeNameEmitter.Emit(Itself(declaring))} {name}({ParameterEmitter.Emit(constructor.ParametersList, diagnostics)});",
        ]);
    }

    public string Helper(string stem, Func<string, string[]> declaration)
    {
        return Declare(stem, stem, declaration);
    }

    private string Storage(object member, string storage, string memberName, IMistType type, MemberType declaring, IMistType owner, bool isStatic)
    {
        return Declare(member, declaring, owner, memberName, (accessibility, name) =>
        [
            $"{Attribute}.{(isStatic ? "StaticField" : "Field")}, Name = \"{storage}\")]",
            $"{accessibility} static extern ref {TypeNameEmitter.Emit(type)} {name}({Target(declaring, isStatic)});",
        ]);
    }

    private static string Target(MemberType declaring, bool isStatic)
    {
        return $"{TypeNameEmitter.Emit(Itself(declaring))}{(isStatic ? "?" : "")} __target";
    }

    private static IMistType Itself(MemberType declaring)
    {
        return declaring.TypeParametersList.Count == 0 ? declaring : new ConstructedType(declaring, declaring.TypeParametersList);
    }

    private string Declare(object member, MemberType declaring, IMistType owner, string stem, Func<string, string, string[]> declaration)
    {
        owner = TypeNameEmitter.Closed(owner);

        if(declaring.TypeParametersList.Count == 0)
        {
            return Declare(member, $"{AccessorHolder.Flat(declaring)}_{stem}", name => declaration("private", name));
        }

        if(owner is ConstructedType instance && UnionSpecialization.NameOf(instance) is not null)
        {
            using var closing = TypeNameEmitter.Substituting(TypeSubstitution.ForReceiver(instance));
            return Declare((member, instance.FullyQualifiedName), $"{AccessorHolder.Flat(instance)}_{stem}", name => declaration("private", name));
        }

        if(!holders.TryGetValue(declaring, out var members))
        {
            members = [];
            holders[declaring] = members;
        }

        if(!names.TryGetValue(member, out var known))
        {
            known = $"__{stem}";

            for(var suffix = 2; members.Exists(declared => declared.Name == known); suffix++)
            {
                known = $"__{stem}_{suffix}";
            }

            names[member] = known;
            members.Add((known, declaration("public", known)));
        }

        IEnumerable<IMistType> arguments = owner is ConstructedType construction ? construction.TypeArguments : declaring.TypeParametersList;

        return $"{HolderName(declaring)}<{string.Join(", ", arguments.Select(TypeNameEmitter.Emit))}>.{known}";
    }

    private string Declare(object member, string stem, Func<string, string[]> declaration)
    {
        if(names.TryGetValue(member, out var known))
        {
            return known;
        }

        var name = $"__{prefix}_{stem}";

        for(var suffix = 2; names.ContainsValue(name); suffix++)
        {
            name = $"__{prefix}_{stem}_{suffix}";
        }

        names[member] = name;
        declarations.Add(declaration(name));

        return name;
    }

    private string HolderName(MemberType declaring)
    {
        return AccessorHolder.NameFor(home, $"__{prefix}_{AccessorHolder.Flat(declaring)}_Access");
    }
}