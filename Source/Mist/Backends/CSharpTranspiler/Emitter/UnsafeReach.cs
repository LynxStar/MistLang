using Mist.Common.Diagnostics;
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
/// is widened, and a member C# already admits is written as it always is.
/// </summary>
internal sealed class UnsafeReach : IDisposable
{
    [ThreadStatic]
    private static UnsafeReach? current;

    private readonly ConcreteType home;
    private readonly MemberType receiver;
    private readonly string prefix;
    private readonly DiagnosticBag diagnostics;
    private readonly Dictionary<object, string> names = [];
    private readonly List<IReadOnlyList<string>> declarations = [];
    private int initializers;

    private UnsafeReach(ConcreteType home, MemberType receiver, string prefix, DiagnosticBag diagnostics)
    {
        this.home = home;
        this.receiver = receiver;
        this.prefix = prefix;
        this.diagnostics = diagnostics;
    }

    public IReadOnlyList<IReadOnlyList<string>> Declarations => declarations;

    public static UnsafeReach Enter(ConcreteType home, MemberType receiver, string prefix, DiagnosticBag diagnostics)
    {
        current = new UnsafeReach(home, receiver, prefix, diagnostics);
        return current;
    }

    public void Dispose()
    {
        current = null;
    }

    public static string? Implicit(Field field)
    {
        return current is { } reach && !reach.Reaches(field.Modifiers.Accessibility, field.Modifiers.IsStatic, field.DeclaringType, reach.receiver)
            ? $"{reach.FieldAccessor(field)}({Leading(field.Modifiers.IsStatic, null)})"
            : null;
    }

    public static string? Implicit(Property property)
    {
        return current is { } reach && !reach.Reaches(Narrowest(property), property.Modifiers.IsStatic, property.DeclaringType, reach.receiver)
            ? reach.Read(property, Leading(property.Modifiers.IsStatic, null))
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
                => $"{reach.FieldAccessor(field)}({Leading(field.Modifiers.IsStatic, renderReceiver)})",
            PropertyResult { Property: var property }
                when !reach.Reaches(Narrowest(property), property.Modifiers.IsStatic, property.DeclaringType, access.Receiver.Type)
                => reach.Read(property, Leading(property.Modifiers.IsStatic, renderReceiver)),
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

        return (reach.MethodAccessor(method), leading);
    }

    public static string? Construction(Constructor? constructor)
    {
        if(current is not { } reach
            || constructor is not { DeclaringType: ConcreteType owner }
            || reach.Reaches(constructor.Modifiers.Accessibility, isStatic: true, owner, null))
        {
            return null;
        }

        return reach.Declare(constructor, $"{Flat(owner)}_new", name =>
        [
            "[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]",
            $"private static extern {TypeNameEmitter.Emit(owner)} {name}({ParameterEmitter.Emit(constructor.ParametersList, reach.diagnostics)});",
        ]);
    }

    public static (string Helper, string Target)? Initializer(NewExpression creation)
    {
        if(current is not { } reach
            || creation.Type is not MemberType created
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
        var created = (MemberType)creation.Type;

        if(entry.MemberName is not { } name)
        {
            return $"{target}.Add({value});";
        }

        return Lookup(created, name) switch
        {
            FieldResult { Field: var field } when !reach.Reaches(field.Modifiers.Accessibility, false, field.DeclaringType, created)
                => $"{reach.FieldAccessor(field)}({target}) = {value};",
            PropertyResult { Property: var property } when !reach.Reaches(Narrowest(property), false, property.DeclaringType, created)
                => reach.Write(property, target, value) + ";",
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
            PropertyReferenceExpression implicitProperty => (implicitProperty.Property, (IMistType)reach.receiver, (Func<string>?)null),
            MemberAccessExpression { Member: PropertyResult found, IsConditional: false } access
                => (found.Property, access.Receiver.Type, () => render(access.Receiver)),
            _ => (null, null, null),
        };

        if(property is null || IsAuto(property) || reach.Reaches(Narrowest(property), property.Modifiers.IsStatic, property.DeclaringType, receiverType))
        {
            return null;
        }

        return (reach.PropertyAccessor(property, isSetter: true), Leading(property.Modifiers.IsStatic, renderReceiver));
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

    private string Read(Property property, string leading)
    {
        return IsAuto(property)
            ? $"{FieldAccessor(property)}({leading})"
            : $"{PropertyAccessor(property, isSetter: false)}({leading})";
    }

    private string Write(Property property, string target, string value)
    {
        return IsAuto(property)
            ? $"{FieldAccessor(property)}({target}) = {value}"
            : $"{PropertyAccessor(property, isSetter: true)}({target}, {value})";
    }

    private string FieldAccessor(Field field)
    {
        return StorageAccessor(field, field.Name, field.Name, field.Type, field.DeclaringType!, field.Modifiers.IsStatic);
    }

    private string FieldAccessor(Property property)
    {
        return StorageAccessor(
            property, $"<{property.Name}>k__BackingField", property.Name, property.Type, property.DeclaringType!, property.Modifiers.IsStatic);
    }

    private string StorageAccessor(object member, string storage, string memberName, IMistType type, MemberType owner, bool isStatic)
    {
        var kind = isStatic ? "StaticField" : "Field";

        return Declare(member, $"{Flat(owner)}_{memberName}", name =>
        [
            $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.{kind}, Name = \"{storage}\")]",
            $"private static extern ref {TypeNameEmitter.Emit(type)} {name}({Target(owner, isStatic)});",
        ]);
    }

    private string PropertyAccessor(Property property, bool isSetter)
    {
        var owner = property.DeclaringType!;
        var kind = property.Modifiers.IsStatic ? "StaticMethod" : "Method";
        var accessor = isSetter ? "set" : "get";
        var signature = isSetter
            ? $"void {{0}}({Target(owner, property.Modifiers.IsStatic)}, {TypeNameEmitter.Emit(property.Type)} value)"
            : $"{TypeNameEmitter.Emit(property.Type)} {{0}}({Target(owner, property.Modifiers.IsStatic)})";

        return Declare((property, isSetter), $"{Flat(owner)}_{accessor}_{property.Name}", name =>
        [
            $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.{kind}, Name = \"{accessor}_{property.Name}\")]",
            $"private static extern {string.Format(System.Globalization.CultureInfo.InvariantCulture, signature, name)};",
        ]);
    }

    private string MethodAccessor(Method method)
    {
        var owner = method.DeclaringType!;
        var kind = method.Modifiers.IsStatic ? "StaticMethod" : "Method";
        var parameters = method.ParametersList.Count == 0
            ? ""
            : ", " + ParameterEmitter.Emit(method.ParametersList, diagnostics);

        return Declare(method, $"{Flat(owner)}_{method.Name}", name =>
        [
            $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.{kind}, Name = \"{method.Name}\")]",
            $"private static extern {TypeNameEmitter.Emit(method.ReturnType)} {name}({Target(owner, method.Modifiers.IsStatic)}{parameters});",
        ]);
    }

    private static string Target(MemberType owner, bool isStatic)
    {
        return $"{TypeNameEmitter.Emit(owner)}{(isStatic ? "?" : "")} __target";
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

    private static string Flat(MistType owner)
    {
        return new string([.. owner.FullyQualifiedName.Select(character => char.IsLetterOrDigit(character) ? character : '_')]);
    }
}