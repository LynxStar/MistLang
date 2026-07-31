using System.Collections.Immutable;
using Mist.Common.Diagnostics;
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

        CollectInto(structType, fields, methods);

        return new FlattenedStruct(fields.ToImmutable(), methods.ToImmutable())
        {
            Ancestors = CollectAncestors(structType),
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
        ImmutableArray<FlattenedField>.Builder fields,
        ImmutableArray<Method>.Builder methods)
    {
        var (baseStruct, constructedBase) = UnwrapBase(structType.BaseType);
        if(baseStruct is not null)
        {
            var inheritedFields = ImmutableArray.CreateBuilder<FlattenedField>();
            var inheritedMethods = ImmutableArray.CreateBuilder<Method>();

            CollectInto(baseStruct, inheritedFields, inheritedMethods);

            // Before this struct's own fields, which is the ordering FlattenedStruct.Fields
            // guarantees.
            foreach(var inherited in inheritedFields)
            {
                fields.Add(constructedBase is null
                    ? inherited
                    : inherited with { Type = constructedBase.Substitute(inherited.Type) });
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
        }
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

        var reference = structType.BaseType;
        var (current, _) = UnwrapBase(reference);

        while(current is not null && reference is not null)
        {
            ancestors.Insert(0, new AbsorbedAncestor(current, reference));

            reference = current.BaseType;
            (current, _) = UnwrapBase(reference);
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
                diagnostics.ReportAtNode(
                    BackendDiagnostics.StructInheritanceFieldCollision, node, null,
                    field.Name, existing.DeclaringStruct.Name, declaringStruct.Name);
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
        return baseType switch
        {
            StructType direct => (direct, null),
            ConstructedType constructed when constructed.Definition is StructType definition
                => (definition, constructed),
            _ => (null, null),
        };
    }
}