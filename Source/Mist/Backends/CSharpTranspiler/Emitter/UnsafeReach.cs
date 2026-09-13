using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The members a body reaches that C# refuses from where the body is written. A conversion running a
/// <c>Clone</c> body is declared on the newtype, while the body is its source's own code and reaches the
/// private members of either type (newtypes spec Requirement 10.2), so each member C# would refuse there is
/// called through an UnsafeAccessor declared beside the conversion. Nothing the program's own code may touch
/// is widened, and a member C# already admits is written as it always is. The runtime matches an accessor
/// against a generic type's member only in that type's own parameters, so a member of a generic source is
/// declared in a holder class over those parameters and called on the construction the body reaches.
/// </summary>
internal sealed class UnsafeReach : IDisposable
{
    [ThreadStatic]
    private static UnsafeReach? current;

    private readonly ConcreteType home;
    private readonly IMistType receiver;
    private readonly IMistType result;
    private readonly string prefix;
    private readonly DiagnosticBag diagnostics;
    private readonly Dictionary<object, string> names = [];
    private readonly List<IReadOnlyList<string>> declarations = [];
    private readonly Dictionary<MemberType, List<(string Name, string[] Lines)>> holders = [];
    private int initializers;

    private UnsafeReach(ConcreteType home, IMistType receiver, IMistType result, string prefix, DiagnosticBag diagnostics)
    {
        this.home = home;
        this.receiver = receiver;
        this.result = result;
        this.prefix = prefix;
        this.diagnostics = diagnostics;
    }

    public IReadOnlyList<IReadOnlyList<string>> Declarations => [.. declarations, .. holders.Select(Holder)];

    public static UnsafeReach Enter(ConcreteType home, CastOperator conversion, string prefix, DiagnosticBag diagnostics)
    {
        current = new UnsafeReach(home, conversion.SourceParameter.Type, conversion.TargetType, prefix, diagnostics);
        return current;
    }

    public void Dispose()
    {
        current = null;
    }

    public static IMistType Closed(MemberType declaring)
    {
        return current is { } reach ? ClosedOwner(declaring, reach.receiver, reach.result) : declaring;
    }

    public static string? Implicit(Field field)
    {
        return current is { } reach && !reach.Reaches(field.Modifiers.Accessibility, field.Modifiers.IsStatic, field.DeclaringType, reach.receiver)
            ? $"{reach.FieldAccessor(field, ClosedOwner(field.DeclaringType!, reach.receiver, reach.result))}({Leading(field.Modifiers.IsStatic, null)})"
            : null;
    }

    public static string? Implicit(Property property)
    {
        return current is { } reach && !reach.Reaches(Narrowest(property), property.Modifiers.IsStatic, property.DeclaringType, reach.receiver)
            ? reach.Read(property, ClosedOwner(property.DeclaringType!, reach.receiver, reach.result), Leading(property.Modifiers.IsStatic, null))
            : null;
    }

    public static string? Access(MemberAccessExpression access, Func<string> renderReceiver)
    {
        if(current is not { } reach || access.IsConditional)
        {
            return null;
        }

        return access.Member switch
        {
            FieldResult { Field: var field }
                when !reach.Reaches(field.Modifiers.Accessibility, field.Modifiers.IsStatic, field.DeclaringType, access.Receiver.Type)
                => $"{reach.FieldAccessor(field, ClosedOwner(field.DeclaringType!, access.Receiver.Type))}({Leading(field.Modifiers.IsStatic, renderReceiver)})",
            PropertyResult { Property: var property }
                when !reach.Reaches(Narrowest(property), property.Modifiers.IsStatic, property.DeclaringType, access.Receiver.Type)
                => reach.Read(property, ClosedOwner(property.DeclaringType!, access.Receiver.Type), Leading(property.Modifiers.IsStatic, renderReceiver)),
            _ => null,
        };
    }

    public static (string Accessor, string Leading)? Call(InvocationExpression invocation, Func<Expression, string> render)
    {
        if(current is not { } reach || invocation.Method is not { TypeParametersList.Count: 0 } method)
        {
            return null;
        }

        var receiverType = invocation.Receiver switch
        {
            MethodReferenceExpression => reach.receiver,
            MemberAccessExpression { IsConditional: false } access => access.Receiver.Type,
            _ => null,
        };

        if(receiverType is null || reach.Reaches(method.Modifiers.Accessibility, method.Modifiers.IsStatic, method.DeclaringType, receiverType))
        {
            return null;
        }

        var leading = invocation.Receiver is MemberAccessExpression explicitReceiver
            ? Leading(method.Modifiers.IsStatic, () => render(explicitReceiver.Receiver))
            : Leading(method.Modifiers.IsStatic, null);

        return (reach.MethodAccessor(method, ClosedOwner(method.DeclaringType!, receiverType, reach.result)), leading);
    }

    public static string? Construction(NewExpression creation)
    {
        if(current is not { } reach
            || creation.Constructor is not { DeclaringType: ConcreteType owner } constructor
            || reach.Reaches(constructor.Modifiers.Accessibility, isStatic: true, owner, null))
        {
            return null;
        }

        return reach.Declare(constructor, owner, creation.Type, "new", (accessibility, name) =>
        [
            "[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]",
            $"{accessibility} static extern {TypeNameEmitter.Emit(Itself(owner))} {name}({ParameterEmitter.Emit(constructor.ParametersList, reach.diagnostics)});",
        ]);
    }

    public static (string Helper, string Target)? Initializer(NewExpression creation)
    {
        if(current is not { } reach
            || Definition(creation.Type) is not MemberType created
            || !creation.Initializer.Any(entry => entry.MemberName is { } name && !reach.Reaches(Lookup(created, name), created)))
        {
            return null;
        }

        var helper = reach.Declare(nameof(Initializer), "Initialized", name =>
        [
            $"private static T {name}<T>(T target, System.Action<T> initialize)",
            "{",
            "    initialize(target);",
            "    return target;",
            "}",
        ]);

        return (helper, $"__initialized{reach.initializers++}");
    }

    public static string Entry(NewExpression creation, ObjectInitializerAssignment entry, string target, string value)
    {
        var reach = current!;
        var created = (MemberType)Definition(creation.Type);

        if(entry.MemberName is not { } name)
        {
            return $"{target}.Add({value});";
        }

        return Lookup(created, name) switch
        {
            FieldResult { Field: var field } when !reach.Reaches(field.Modifiers.Accessibility, false, field.DeclaringType, created)
                => $"{reach.FieldAccessor(field, ClosedOwner(field.DeclaringType!, creation.Type))}({target}) = {value};",
            PropertyResult { Property: var property } when !reach.Reaches(Narrowest(property), false, property.DeclaringType, created)
                => reach.Write(property, ClosedOwner(property.DeclaringType!, creation.Type), target, value) + ";",
            _ => $"{target}.{name} = {value};",
        };
    }

    public static (string Accessor, string Leading)? Assignment(AssignmentExpression assignment, Func<Expression, string> render)
    {
        if(current is not { } reach || assignment.IsCoalescing)
        {
            return null;
        }

        var (property, receiverType, renderReceiver) = assignment.Target switch
        {
            PropertyReferenceExpression implicitProperty => (implicitProperty.Property, reach.receiver, (Func<string>?)null),
            MemberAccessExpression { Member: PropertyResult found, IsConditional: false } access
                => (found.Property, access.Receiver.Type, () => render(access.Receiver)),
            _ => (null, null, null),
        };

        if(property is null || IsAuto(property) || reach.Reaches(Narrowest(property), property.Modifiers.IsStatic, property.DeclaringType, receiverType))
        {
            return null;
        }

        var owner = ClosedOwner(property.DeclaringType!, receiverType!, reach.result);

        return (reach.PropertyAccessor(property, owner, isSetter: true), Leading(property.Modifiers.IsStatic, renderReceiver));
    }

    private bool Reaches(LookupResult member, MemberType created)
    {
        return member switch
        {
            FieldResult { Field: var field } => Reaches(field.Modifiers.Accessibility, false, field.DeclaringType, created),
            PropertyResult { Property: var property } => Reaches(Narrowest(property), false, property.DeclaringType, created),
            _ => true,
        };
    }

    private bool Reaches(Accessibility accessibility, bool isStatic, MemberType? declaring, IMistType? receiverType)
    {
        return declaring is null || accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Internal => declaring.Assembly == home.Assembly,
            Accessibility.Private => Encloses(declaring),
            _ => DerivesFrom(home, declaring) && (isStatic || DerivesFrom(receiverType, home)),
        };
    }

    private bool Encloses(MistType declaring)
    {
        for(MistType? type = home; type is not null; type = type.DeclaringType)
        {
            if(ReferenceEquals(type, declaring))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(IMistType? type, MistType ancestor)
    {
        for(var current = type; current is not null; current = (Definition(current) as ConcreteType)?.BaseType)
        {
            if(ReferenceEquals(Definition(current), ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static IMistType ClosedOwner(MemberType declaring, params IMistType[] starts)
    {
        if(declaring.TypeParametersList.Count == 0)
        {
            return declaring;
        }

        foreach(var start in starts)
        {
            for(var type = (IMistType?)start; type is not null; type = BaseOf(type))
            {
                if(type is ConstructedType construction && ReferenceEquals(construction.Definition, declaring))
                {
                    return construction;
                }
            }
        }

        return declaring;
    }

    private static IMistType? BaseOf(IMistType type)
    {
        return (type, Definition(type)) switch
        {
            (ConstructedType construction, ConcreteType { BaseType: { } baseType })
                => TypeSubstitution.ForReceiver(construction).Apply(baseType, interner: null),
            (_, ConcreteType { BaseType: var baseType }) => baseType,
            _ => null,
        };
    }

    private static IMistType Itself(MemberType declaring)
    {
        return declaring.TypeParametersList.Count == 0 ? declaring : new ConstructedType(declaring, declaring.TypeParametersList);
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }

    private static Accessibility Narrowest(Property property)
    {
        return new[] { property.Getter, property.Setter }.Any(accessor => accessor is { Accessibility: Accessibility.Private })
            ? Accessibility.Private
            : property.Modifiers.Accessibility;
    }

    private static bool IsAuto(Property property)
    {
        return AccessorEmitter.IsBodiless(property.Getter) && AccessorEmitter.IsBodiless(property.Setter);
    }

    private static LookupResult Lookup(MemberType type, string name)
    {
        return TypeMemberLookup.Find(type, LookupRequest.Member(name, MemberVisibility.InstanceOnly, null));
    }

    private static string Leading(bool isStatic, Func<string>? renderReceiver)
    {
        return isStatic ? "null" : renderReceiver?.Invoke() ?? ReceiverSpelling.Carrier;
    }

    private string Read(Property property, IMistType owner, string leading)
    {
        return IsAuto(property)
            ? $"{FieldAccessor(property, owner)}({leading})"
            : $"{PropertyAccessor(property, owner, isSetter: false)}({leading})";
    }

    private string Write(Property property, IMistType owner, string target, string value)
    {
        return IsAuto(property)
            ? $"{FieldAccessor(property, owner)}({target}) = {value}"
            : $"{PropertyAccessor(property, owner, isSetter: true)}({target}, {value})";
    }

    private string FieldAccessor(Field field, IMistType owner)
    {
        return StorageAccessor(field, field.Name, field.Name, field.Type, field.DeclaringType!, owner, field.Modifiers.IsStatic);
    }

    private string FieldAccessor(Property property, IMistType owner)
    {
        return StorageAccessor(
            property, $"<{property.Name}>k__BackingField", property.Name, property.Type, property.DeclaringType!, owner, property.Modifiers.IsStatic);
    }

    private string StorageAccessor(
        object member, string storage, string memberName, IMistType type, MemberType declaring, IMistType owner, bool isStatic)
    {
        var kind = isStatic ? "StaticField" : "Field";

        return Declare(member, declaring, owner, memberName, (accessibility, name) =>
        [
            $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.{kind}, Name = \"{storage}\")]",
            $"{accessibility} static extern ref {TypeNameEmitter.Emit(type)} {name}({Target(declaring, isStatic)});",
        ]);
    }

    private string PropertyAccessor(Property property, IMistType owner, bool isSetter)
    {
        var declaring = property.DeclaringType!;
        var kind = property.Modifiers.IsStatic ? "StaticMethod" : "Method";
        var accessor = isSetter ? "set" : "get";
        var signature = isSetter
            ? $"void {{0}}({Target(declaring, property.Modifiers.IsStatic)}, {TypeNameEmitter.Emit(property.Type)} value)"
            : $"{TypeNameEmitter.Emit(property.Type)} {{0}}({Target(declaring, property.Modifiers.IsStatic)})";

        return Declare((property, isSetter), declaring, owner, $"{accessor}_{property.Name}", (accessibility, name) =>
        [
            $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.{kind}, Name = \"{accessor}_{property.Name}\")]",
            $"{accessibility} static extern {string.Format(System.Globalization.CultureInfo.InvariantCulture, signature, name)};",
        ]);
    }

    private string MethodAccessor(Method method, IMistType owner)
    {
        var declaring = method.DeclaringType!;
        var kind = method.Modifiers.IsStatic ? "StaticMethod" : "Method";
        var parameters = method.ParametersList.Count == 0
            ? ""
            : ", " + ParameterEmitter.Emit(method.ParametersList, diagnostics);

        return Declare(method, declaring, owner, method.Name, (accessibility, name) =>
        [
            $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.{kind}, Name = \"{method.Name}\")]",
            $"{accessibility} static extern {TypeNameEmitter.Emit(method.ReturnType)} {name}({Target(declaring, method.Modifiers.IsStatic)}{parameters});",
        ]);
    }

    private static string Target(MemberType declaring, bool isStatic)
    {
        return $"{TypeNameEmitter.Emit(Itself(declaring))}{(isStatic ? "?" : "")} __target";
    }

    private string Declare(object member, MemberType declaring, IMistType owner, string stem, Func<string, string, string[]> declaration)
    {
        if(declaring.TypeParametersList.Count == 0)
        {
            return Declare(member, $"{Flat(declaring)}_{stem}", name => declaration("private", name));
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

    private string[] Holder(KeyValuePair<MemberType, List<(string Name, string[] Lines)>> holder)
    {
        return
        [
            $"private static class {HolderName(holder.Key)}<{string.Join(", ", holder.Key.TypeParametersList.Select(TypeNameEmitter.Emit))}>",
            "{",
            .. holder.Value.SelectMany(member => member.Lines).Select(line => $"    {line}"),
            "}",
        ];
    }

    private string HolderName(MemberType declaring)
    {
        return $"__{prefix}_{Flat(declaring)}_Access";
    }

    private static string Flat(MistType owner)
    {
        var stem = owner.FullyQualifiedName.Where(character => character is not ('<' or '>' or ',' or ' '));

        return new string([.. stem.Select(character => char.IsLetterOrDigit(character) ? character : '_')]);
    }
}