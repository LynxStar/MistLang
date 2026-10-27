using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Folds a struct's inherited members into it, producing the member set the C# backend emits.
/// See <see cref="FlattenedStruct"/> for why this exists and what it guarantees.
/// </summary>
internal sealed class StructFlattener
{
    private readonly DiagnosticBag diagnostics;

    public StructFlattener(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Flattens a struct. Safe to call on a struct with no base, which yields its own members with
    /// accessibility remapped -- so the emitter has one code path rather than two.
    /// </summary>
    public FlattenedStruct Flatten(StructType structType)
    {
        var fields = ImmutableArray.CreateBuilder<FlattenedField>();
        var methods = ImmutableArray.CreateBuilder<Method>();
        var properties = ImmutableArray.CreateBuilder<Property>();
        var methodPaths = new Dictionary<Method, IReadOnlyList<ConstructedType>>(ReferenceEqualityComparer.Instance);
        var propertyPaths = new Dictionary<Property, IReadOnlyList<ConstructedType>>(ReferenceEqualityComparer.Instance);

        CollectInto(structType, [], fields, methods, properties, methodPaths, propertyPaths);

        return new FlattenedStruct(fields.ToImmutable(), methods.ToImmutable(), properties.ToImmutable())
        {
            Ancestors = CollectAncestors(structType),
            MethodPaths = methodPaths,
            PropertyPaths = propertyPaths,
        };
    }

    /// <summary>
    /// Appends <paramref name="structType"/>'s chain root-first, then its own members.
    /// <para>
    /// Recursive rather than iterative because a generic ancestor's type parameters have to be
    /// substituted once per level on the way back down. Recursion puts each level's substitution
    /// exactly where the members it applies to are returned, so a chain like
    /// <c>C : B&lt;int&gt;</c> over <c>B&lt;T&gt; : A&lt;T&gt;</c> composes without the caller
    /// tracking a substitution stack.
    /// </para>
    /// </summary>
    private void CollectInto(
        StructType structType,
        IReadOnlyList<ConstructedType> path,
        ImmutableArray<FlattenedField>.Builder fields,
        ImmutableArray<Method>.Builder methods,
        ImmutableArray<Property>.Builder properties,
        Dictionary<Method, IReadOnlyList<ConstructedType>> methodPaths,
        Dictionary<Property, IReadOnlyList<ConstructedType>> propertyPaths)
    {
        var (baseStruct, constructedBase) = UnwrapBase(structType.BaseType);
        if(baseStruct is not null)
        {
            var inheritedFields = ImmutableArray.CreateBuilder<FlattenedField>();
            var inheritedMethods = ImmutableArray.CreateBuilder<Method>();
            var inheritedProperties = ImmutableArray.CreateBuilder<Property>();

            CollectInto(
                baseStruct,
                constructedBase is null ? path : [.. path, constructedBase],
                inheritedFields, inheritedMethods, inheritedProperties, methodPaths, propertyPaths);

            // Before this struct's own fields, which is the ordering FlattenedStruct.Fields
            // guarantees.
            var substitution = TypeSubstitution.ForReceiver(constructedBase);

            foreach(var inherited in inheritedFields)
            {
                fields.Add(inherited with { Type = substitution.Apply(inherited.Type, interner: null) });
            }

            foreach(var inheritedMethod in inheritedMethods)
            {
                // A redeclared signature is an override, and the derived one wins. Flattened
                // structs are not polymorphic -- a base-typed value holding a converted derived
                // value IS a base value, by copy -- so static dispatch to the derived body is the
                // correct reading rather than a limitation.
                if(!DeclaresSignature(structType, inheritedMethod))
                {
                    methods.Add(inheritedMethod);
                }
            }

            foreach(var inheritedProperty in inheritedProperties)
            {
                if(DeclaresProperty(structType, inheritedProperty))
                {
                    continue;
                }

                var inheritable = Inheritable(inheritedProperty);

                if(!ReferenceEquals(inheritable, inheritedProperty)
                    && propertyPaths.TryGetValue(inheritedProperty, out var reached))
                {
                    propertyPaths[inheritable] = reached;
                }

                properties.Add(inheritable);
            }
        }

        foreach(var field in structType.FieldsList)
        {
            ReportIfCollides(field, structType, fields);

            fields.Add(new FlattenedField(field, field.Type, Remap(field.Modifiers.Accessibility))
            {
                DeclaringStruct = structType,
            });
        }

        foreach(var method in structType.MethodsList)
        {
            methods.Add(method);

            if(path.Count > 0)
            {
                methodPaths[method] = path;
            }
        }

        foreach(var property in structType.PropertiesList)
        {
            properties.Add(property);

            if(path.Count > 0)
            {
                propertyPaths[property] = path;
            }
        }
    }

    private static bool DeclaresProperty(StructType structType, Property candidate)
    {
        foreach(var declared in structType.PropertiesList)
        {
            if(string.Equals(declared.Name, candidate.Name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The property as the absorbing struct must declare it. An ancestor's constructor becomes an
    /// ordinary initializer METHOD on that struct (see <see cref="Emitter.StructInitEmitter"/>), and
    /// C# allows writing a get-only or init-only auto-property only from a constructor -- so an
    /// inherited one needs a plain setter for that initializer to reach, private because nothing
    /// outside the struct gained the right to write it.
    /// </summary>
    private static Property Inheritable(Property property)
    {
        if(property.Setter is { IsInit: false })
        {
            return property;
        }

        return new Property(property.Name, property.Type, property.Modifiers)
        {
            Getter = property.Getter,
            Setter = new PropertyAccessor
            {
                IsAuto = true,
                Accessibility = Accessibility.Private,
            },
            Initializer = property.Initializer,
        };
    }

    /// <summary>
    /// The base chain root first, excluding <paramref name="structType"/> itself.
    /// <para>
    /// Each ancestor's reference is taken from the level that named it, so a grandparent is named
    /// as the PARENT spelled it. For a generic ancestor more than one level up that means its type
    /// arguments are the parent's, which is only correct where they pass straight through unchanged.
    /// </para>
    /// </summary>
    private static ImmutableArray<AbsorbedAncestor> CollectAncestors(StructType structType)
    {
        var ancestors = new List<AbsorbedAncestor>();
        var path = new List<ConstructedType>();

        var reference = structType.BaseType;
        var (current, constructed) = UnwrapBase(reference);

        while(current is not null && reference is not null)
        {
            if(constructed is not null)
            {
                path.Add(constructed);
            }

            ancestors.Insert(0, new AbsorbedAncestor(current, reference) { Path = [.. path] });

            reference = current.BaseType;
            (current, constructed) = UnwrapBase(reference);
        }

        return [.. ancestors];
    }

    /// <summary>
    /// A field name already present from an ancestor. Legal Mist -- two levels declaring a private
    /// field cannot see each other -- but flattening puts both in one struct, where the name can
    /// only mean one of them. Refused rather than resolved by picking, which would silently drop a
    /// field.
    /// </summary>
    private void ReportIfCollides(
        Field field,
        StructType declaringStruct,
        ImmutableArray<FlattenedField>.Builder alreadyFlattened)
    {
        foreach(var existing in alreadyFlattened)
        {
            if(!string.Equals(existing.Name, field.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if(declaringStruct.DeclarationNode is { } node)
            {
                diagnostics.ReportAtNodeTyped(
                    BackendDiagnostics.StructInheritanceFieldCollision, node, null,
                    new NameValue(field.Name), DiagnosticSnapshot.Type(existing.DeclaringStruct, existing.DeclaringStruct.Name), DiagnosticSnapshot.Type(declaringStruct, declaringStruct.Name));
            }

            return;
        }
    }

    /// <summary>
    /// Whether the struct declares a method matching <paramref name="candidate"/>'s name and
    /// parameter types, which is what makes the inherited one an override to be skipped.
    /// </summary>
    private static bool DeclaresSignature(StructType structType, Method candidate)
    {
        foreach(var declared in structType.MethodsList)
        {
            if(SignatureKey(declared) == SignatureKey(candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Name plus parameter types. Return type is excluded because it does not distinguish
    /// overloads, so two methods differing only in it would collide in C# as well.
    /// </summary>
    private static string SignatureKey(Method method)
    {
        var parameters = method.ParametersList.Select(p => p.Type.FullyQualifiedName);
        return $"{method.Name}({string.Join(",", parameters)})";
    }

    /// <summary>
    /// The accessibility to emit. Only <c>protected</c> moves; see
    /// <see cref="FlattenedField.Accessibility"/> for why it becomes private.
    /// </summary>
    private static Accessibility Remap(Accessibility declared)
    {
        return declared == Accessibility.Protected ? Accessibility.Private : declared;
    }

    /// <summary>
    /// Splits a base type reference into the struct it names and, when the reference was generic,
    /// the construction to substitute its type arguments through. Both null when there is no base,
    /// or when the base is not a struct -- an abstract in the base list is a conformance rather
    /// than something to flatten, and is left for the base list to emit.
    /// </summary>
    private static (StructType? BaseStruct, ConstructedType? Constructed) UnwrapBase(MistType? baseType)
    {
        // An intrinsic ancestor is dropped rather than flattened, so no fields are collected from it
        // and no conversion to it is emitted. Reported by ReportIntrinsicBase, which runs once per
        // struct rather than once per flatten.
        return baseType switch
        {
            StructType { IsIntrinsic: true } => (null, null),
            ConstructedType { Definition: StructType { IsIntrinsic: true } } => (null, null),
            StructType direct => (direct, null),
            ConstructedType constructed when constructed.Definition is StructType definition
                => (definition, constructed),
            _ => (null, null),
        };
    }

    /// <summary>
    /// Reports a struct that inherits an intrinsic struct. The immediate base only: every struct in a
    /// chain is flattened in its own right, so a deeper intrinsic ancestor is reported against the
    /// struct that names it, which is where the declaration a reader has to change actually sits.
    /// </summary>
    public static void ReportIntrinsicBase(StructType structType, DiagnosticBag diagnostics)
    {
        var declared = structType.BaseType switch
        {
            StructType direct => direct,
            ConstructedType { Definition: StructType definition } => definition,
            _ => null,
        };

        if(declared is not { IsIntrinsic: true } || structType.DeclarationNode is not { } node)
        {
            return;
        }

        diagnostics.ReportAtNodeTyped(
            BackendDiagnostics.IntrinsicStructBase, node, null, DiagnosticSnapshot.Type(structType, structType.Name), DiagnosticSnapshot.Type(declared, declared.Name));
    }
}