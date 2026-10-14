using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
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
/// called through an UnsafeAccessor declared beside the conversion, or where <see cref="AccessorHolder"/> places
/// a generic definition's. Nothing the program's own code may touch is widened, and a member C# already admits
/// is written as it always is.
/// </summary>
internal sealed class UnsafeReach : IDisposable
{
    [ThreadStatic]
    private static UnsafeReach? current;

    private readonly ConcreteType home;
    private readonly IMistType receiver;
    private readonly IMistType result;
    private readonly UnsafeAccessors accessors;
    private int initializers;

    private UnsafeReach(ConcreteType home, IMistType receiver, IMistType result, string prefix, DiagnosticBag diagnostics)
    {
        this.home = home;
        this.receiver = receiver;
        this.result = result;
        accessors = new UnsafeAccessors(home, prefix, diagnostics);
    }

    public IReadOnlyList<IReadOnlyList<string>> Declarations => accessors.Declarations;

    public IEnumerable<AccessorHolder> Holders => accessors.Holders;

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
            ? $"{reach.accessors.Field(field, ClosedOwner(field.DeclaringType!, reach.receiver, reach.result))}({Leading(field.Modifiers.IsStatic, null)})"
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
        if(current is not { } reach)
        {
            return null;
        }

        var present = Present(access.Node);
        var target = access.IsConditional ? () => present : renderReceiver;

        var reached = access.Member switch
        {
            FieldResult { Field: var field }
                when !reach.Reaches(field.Modifiers.Accessibility, field.Modifiers.IsStatic, field.DeclaringType, access.Receiver.Type)
                => $"{reach.accessors.Field(field, ClosedOwner(field.DeclaringType!, access.Receiver.Type))}({Leading(field.Modifiers.IsStatic, target)})",
            PropertyResult { Property: var property }
                when !reach.Reaches(Narrowest(property), property.Modifiers.IsStatic, property.DeclaringType, access.Receiver.Type)
                => reach.Read(property, ClosedOwner(property.DeclaringType!, access.Receiver.Type), Leading(property.Modifiers.IsStatic, target)),
            _ => null,
        };

        return reached is not null && access.IsConditional
            ? $"({renderReceiver()} is {{ }} {present} ? ({TypeNameEmitter.Emit(access.Type)}){reached} : null)"
            : reached;
    }

    public static (string Open, string Close)? Call(InvocationExpression invocation, Func<Expression, string> render)
    {
        if(current is not { } reach || invocation.Method is not { } method)
        {
            return null;
        }

        var (receiverType, explicitReceiver, typeArguments) = invocation.Receiver switch
        {
            MethodReferenceExpression reference => (reach.receiver, null, reference.TypeArguments),
            MemberAccessExpression access => (access.Receiver.Type, access, access.TypeArguments),
            _ => (null, null, []),
        };

        if(receiverType is null || reach.Reaches(method.Modifiers.Accessibility, method.Modifiers.IsStatic, method.DeclaringType, receiverType))
        {
            return null;
        }

        var accessor = reach.accessors.Method(method, ClosedOwner(method.DeclaringType!, receiverType, reach.result))
            + (typeArguments.Count == 0 ? "" : $"<{string.Join(", ", typeArguments.Select(TypeNameEmitter.Emit))}>");
        var separator = invocation.Arguments.Count > 0 ? ", " : "";

        if(explicitReceiver is not { IsConditional: true })
        {
            var leading = Leading(method.Modifiers.IsStatic, explicitReceiver is null ? null : () => render(explicitReceiver.Receiver));
            return ($"{accessor}({leading}{separator}", ")");
        }

        var present = Present(invocation.Node);

        if(OptionalUnion.IsVoid(method.ReturnType))
        {
            return ($"{reach.When()}({render(explicitReceiver.Receiver)}, {present} => {accessor}({present}{separator}", "))");
        }

        return ($"({render(explicitReceiver.Receiver)} is {{ }} {present} ? ({TypeNameEmitter.Emit(invocation.Type)}){accessor}({present}{separator}", ") : null)");
    }

    public static string? Construction(NewExpression creation)
    {
        if(current is not { } reach
            || creation.Constructor is not { DeclaringType: ConcreteType owner } constructor
            || reach.Reaches(constructor.Modifiers.Accessibility, isStatic: true, owner, null))
        {
            return null;
        }

        return reach.accessors.Constructor(constructor, owner, creation.Type);
    }

    public static (string Helper, string Target)? Initializer(NewExpression creation)
    {
        if(current is not { } reach
            || Definition(creation.Type) is not MemberType created
            || !creation.Initializer.Any(entry => entry.Member is { } member && !reach.Reaches(member, created)))
        {
            return null;
        }

        var helper = reach.accessors.Helper("Initialized", name =>
        [
            $"private static __T {name}<__T>(__T target, System.Action<__T> initialize)",
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

        return entry.Member switch
        {
            FieldResult { Field: var field } when !reach.Reaches(field.Modifiers.Accessibility, false, field.DeclaringType, created)
                => $"{reach.accessors.Field(field, ClosedOwner(field.DeclaringType!, creation.Type))}({target}) = {value};",
            PropertyResult { Property: var property } when !reach.Reaches(Narrowest(property), false, property.DeclaringType, created)
                => reach.Write(property, ClosedOwner(property.DeclaringType!, creation.Type), target, value) + ";",
            _ => $"{target}.{name} = {value};",
        };
    }

    public static string? Assignment(AssignmentExpression assignment, Func<Expression, string> render)
    {
        if(current is not { } reach)
        {
            return null;
        }

        if(assignment.Target is IndexExpression { Indexer: { } indexer } index
            && !reach.Reaches(Narrowest(indexer), false, indexer.DeclaringType, index.Receiver.Type))
        {
            var owner = ClosedOwner(indexer.DeclaringType!, index.Receiver.Type, reach.result);
            var set = reach.accessors.Indexer(indexer, owner, isSetter: true);

            if(!assignment.IsCoalescing)
            {
                return $"{set}({render(index.Receiver)}, {render(index.Index)}, {render(assignment.Value)})";
            }

            var get = reach.accessors.Indexer(indexer, owner, isSetter: false);

            return reach.Coalesced(render(index.Receiver), render(index.Index), $"(__t, __k) => {get}(__t, __k)", $"(__t, __k, __v) => {set}(__t, __k, __v)", render(assignment.Value));
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

        var propertyOwner = ClosedOwner(property.DeclaringType!, receiverType!, reach.result);
        var setter = reach.accessors.Property(property, propertyOwner, isSetter: true);
        var target = Leading(property.Modifiers.IsStatic, renderReceiver);

        if(!assignment.IsCoalescing)
        {
            return $"{setter}({target}, {render(assignment.Value)})";
        }

        var getter = reach.accessors.Property(property, propertyOwner, isSetter: false);
        var typedTarget = property.Modifiers.IsStatic ? $"({TypeNameEmitter.Emit(propertyOwner)}?)null" : target;

        return reach.Coalesced(typedTarget, "(object?)null", $"(__t, __k) => {getter}(__t)", $"(__t, __k, __v) => {setter}(__t, __v)", render(assignment.Value));
    }

    public static string? Unreached(CountedSlice slice, IMistType receiverType)
    {
        if(current is not { } reach)
        {
            return null;
        }

        if(!reach.Reaches(Narrowest(slice.Count), false, slice.Count.DeclaringType, receiverType))
        {
            return slice.Count.Name;
        }

        if(slice.Indexer is { } indexer && !reach.Reaches(Narrowest(indexer), false, indexer.DeclaringType, receiverType))
        {
            return "this[]";
        }

        return slice.Method is { } method && !reach.Reaches(method.Modifiers.Accessibility, false, method.DeclaringType, receiverType)
            ? method.Name
            : null;
    }

    public static string? Index(IndexExpression index, Func<Expression, string> render)
    {
        if(current is not { } reach
            || index.Indexer is not { } indexer
            || reach.Reaches(Narrowest(indexer), false, indexer.DeclaringType, index.Receiver.Type))
        {
            return null;
        }

        var getter = reach.accessors.Indexer(indexer, ClosedOwner(indexer.DeclaringType!, index.Receiver.Type, reach.result), isSetter: false);

        return $"{getter}({render(index.Receiver)}, {render(index.Index)})";
    }

    /// <summary>
    /// A method named as a value is a lambda over its accessor, cast to the function type so it converts in any
    /// position; an explicit receiver is bound once, as a method group's is, rather than read at each call.
    /// </summary>
    public static string? Function(FunctionReferenceExpression function, Func<Expression, string> render)
    {
        var method = function.Method;
        var receiverType = function.Receiver?.Type;

        if(current is not { } reach
            || reach.Reaches(method.Modifiers.Accessibility, method.Modifiers.IsStatic, method.DeclaringType, receiverType ?? reach.receiver))
        {
            return null;
        }

        var accessor = reach.accessors.Method(method, ClosedOwner(method.DeclaringType!, receiverType ?? reach.receiver, reach.result))
            + (function.TypeArguments.Count == 0 ? "" : $"<{string.Join(", ", function.TypeArguments.Select(TypeNameEmitter.Emit))}>");
        var parameters = string.Join(", ", method.ParametersList.Select((_, i) => $"__p{i}"));
        var delegateType = TypeNameEmitter.Emit(function.Type);
        var completion = AsyncPromotion.IsPromoted(method) ? ".GetAwaiter().GetResult()" : "";

        if(function.Receiver is not { } bound || method.Modifiers.IsStatic)
        {
            var leading = Leading(method.Modifiers.IsStatic, null);
            return $"(({delegateType})(({parameters}) => {accessor}({string.Join(", ", [leading, .. method.ParametersList.Select((_, i) => $"__p{i}")])}){completion}))";
        }

        var helper = reach.accessors.Helper("Bound", name =>
        [
            $"private static __TResult {name}<__TTarget, __TResult>(__TTarget target, System.Func<__TTarget, __TResult> bind)",
            "{",
            "    return bind(target);",
            "}",
        ]);

        return $"{helper}({render(bound)}, __t => ({delegateType})(({parameters}) => {accessor}({string.Join(", ", ["__t", .. method.ParametersList.Select((_, i) => $"__p{i}")])}){completion}))";
    }

    private string When()
    {
        return accessors.Helper("When", name =>
        [
            $"private static void {name}<__T>(__T target, System.Action<__T> act)",
            "{",
            "    if(target is not null)",
            "    {",
            "        act(target);",
            "    }",
            "}",
        ]);
    }

    private string Coalesced(string target, string key, string get, string set, string value)
    {
        var helper = accessors.Helper("Coalesced", name =>
        [
            $"private static __T {name}<__TTarget, __TKey, __T>(__TTarget target, __TKey key, System.Func<__TTarget, __TKey, __T> get, System.Action<__TTarget, __TKey, __T> set, System.Func<__T> value)",
            "{",
            "    var existing = get(target, key);",
            "    if(existing is not null)",
            "    {",
            "        return existing;",
            "    }",
            "",
            "    var assigned = value();",
            "    set(target, key, assigned);",
            "    return assigned;",
            "}",
        ]);

        return $"{helper}({target}, {key}, {get}, {set}, () => {value})";
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

    internal static IMistType ClosedOwner(MemberType declaring, params IMistType[] starts)
    {
        if(declaring.TypeParametersList.Count == 0)
        {
            return declaring;
        }

        foreach(var start in starts)
        {
            for(var type = (IMistType?)(OptionalUnion.InnerType(start) ?? start); type is not null; type = BaseOf(type))
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

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }

    private static Accessibility Narrowest(Property property)
    {
        return Narrowest(property.Getter, property.Setter, property.Modifiers.Accessibility);
    }

    private static Accessibility Narrowest(Indexer indexer)
    {
        return Narrowest(indexer.Getter, indexer.Setter, indexer.Modifiers.Accessibility);
    }

    private static Accessibility Narrowest(PropertyAccessor? getter, PropertyAccessor? setter, Accessibility declared)
    {
        return new[] { getter, setter }.Any(accessor => accessor is { Accessibility: Accessibility.Private })
            ? Accessibility.Private
            : declared;
    }

    private static bool IsAuto(Property property)
    {
        return AccessorEmitter.IsBodiless(property.Getter) && AccessorEmitter.IsBodiless(property.Setter);
    }

    private static string Leading(bool isStatic, Func<string>? renderReceiver)
    {
        return isStatic ? "null" : renderReceiver?.Invoke() ?? ReceiverSpelling.Carrier;
    }

    private static string Present(Storm.Runtime.SyntaxNode node)
    {
        return $"__reached{node.Span.Start}";
    }

    private string Read(Property property, IMistType owner, string leading)
    {
        return IsAuto(property)
            ? $"{accessors.BackingField(property, owner)}({leading})"
            : $"{accessors.Property(property, owner, isSetter: false)}({leading})";
    }

    private string Write(Property property, IMistType owner, string target, string value)
    {
        return IsAuto(property)
            ? $"{accessors.BackingField(property, owner)}({target}) = {value}"
            : $"{accessors.Property(property, owner, isSetter: true)}({target}, {value})";
    }
}