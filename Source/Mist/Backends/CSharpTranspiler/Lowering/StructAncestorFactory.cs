using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The names and member order of the ancestor factory lowering: a private constructor plus an
/// internal static factory, emitted on every struct so a struct that absorbs it can rebuild an
/// ancestor value from the copy it carries.
/// <para>
/// A CONSTRUCTOR IS THE MECHANISM, not a convenience. Flattening leaves the derived struct with no
/// relationship to what it absorbed, so the derived-to-ancestor conversion has to write the
/// ancestor's state from outside the ancestor -- which C# forbids for a private field, and forbids
/// for a readonly field or a get-only auto-property from anywhere but a constructor of the
/// declaring type. Routing through one puts every write inside the only context that permits it,
/// and leaves the reads on the derived struct's own members, so no cross-type access is needed and
/// no field has to be widened.
/// </para>
/// <para>
/// Emitted unconditionally rather than only where something derives today, because a struct's own
/// compilation cannot see a derivation in a later one and a C# partial cannot span assemblies.
/// </para>
/// </summary>
internal static class StructAncestorFactory
{
    /// <summary>
    /// The parameter type that discriminates the generated constructor from anything the user
    /// declared. A marker rather than a dummy <c>bool</c>: the constructor's other parameters are
    /// the struct's own members, so a plausible user signature can match them exactly, and a
    /// constructor cannot be renamed out of the way the way <see cref="Name"/> can.
    /// </summary>
    public const string MarkerType = $"{ConstructorConstraintInterfaces.Namespace}.__StructInit";

    public const string MarkerTypeName = "__StructInit";

    /// <summary>
    /// The factory name, suffixed if the struct already uses it.
    /// </summary>
    public static string Name(FlattenedStruct flattened)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach(var method in flattened.Methods)
        {
            _ = taken.Add(method.Name);
        }

        foreach(var field in flattened.Fields)
        {
            _ = taken.Add(field.Name);
        }

        foreach(var property in flattened.Properties)
        {
            _ = taken.Add(property.Name);
        }

        var name = "__FromParts";
        var suffix = 2;

        while(taken.Contains(name))
        {
            name = $"__FromParts{suffix}";
            suffix++;
        }

        return name;
    }

    /// <summary>
    /// The members the factory carries, fields before properties. One order used by both the
    /// declaration and every call site, which is what keeps them matched.
    /// </summary>
    public static ImmutableArray<FactoryMember> Members(FlattenedStruct flattened)
    {
        var members = ImmutableArray.CreateBuilder<FactoryMember>();

        foreach(var field in flattened.Fields)
        {
            members.Add(new FactoryMember(field.Name, TypeNameEmitter.Emit(field.Type)));
        }

        foreach(var property in flattened.Properties)
        {
            if(HoldsState(property))
            {
                members.Add(new FactoryMember(property.Name, TypeNameEmitter.Emit(property.Type)));
            }
        }

        return members.ToImmutable();
    }

    /// <summary>
    /// Whether a property has storage of its own for the factory to write. Only an auto-property
    /// does: a computed one derives its value from members already carried, so transporting it would
    /// mean assigning a property with no setter -- which C# refuses even inside a constructor, and
    /// which would also be duplicating state the factory already moves.
    /// </summary>
    private static bool HoldsState(Property property)
    {
        return AccessorEmitter.IsBodiless(property.Getter) && AccessorEmitter.IsBodiless(property.Setter);
    }

    public static string Parameters(ImmutableArray<FactoryMember> members)
    {
        var parameters = members.Select((member, i) => $"{member.TypeName} p{i}").ToList();
        parameters.Add($"{MarkerType} marker");
        return string.Join(", ", parameters);
    }

    public static string Arguments(ImmutableArray<FactoryMember> members)
    {
        var arguments = members.Select((_, i) => $"p{i}").ToList();

        // TYPED rather than a bare `default`, which converts to every one-parameter constructor. On a
        // struct with no members this call passes the marker alone, so a bare default would leave it
        // ambiguous against any single-argument constructor the struct declares.
        arguments.Add($"default({MarkerType})");
        return string.Join(", ", arguments);
    }
}

/// <summary>
/// One member the factory transports, named as the struct declares it and typed as the struct
/// carries it after any generic substitution.
/// </summary>
internal sealed record FactoryMember(string Name, string TypeName);