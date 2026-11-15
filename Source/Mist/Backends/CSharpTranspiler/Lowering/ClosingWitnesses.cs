using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which type parameters take a closing witness, decided once over the whole program (runtime-dependent-values design,
/// <em>Identity by value: a closing as a type argument</em>): a parameter <see cref="SlotSensitivity"/> marks for a
/// reason a description answers, that some closing in the program fills with a type mentioning a closing of a
/// value-routed type or a parameter that takes a witness itself, to a fixed point. An abstract's method slot and every
/// method answering it take one together, because C# matches an implementation to its interface member by signature.
/// The witness travels as the value-routed slots do: a method takes it as a hidden leading parameter, a type keeps it
/// in a hidden field set from a hidden constructor parameter, and a static member takes the type's as a hidden
/// parameter. Its runtime is <see cref="Runtime"/>, a closed C# type's description holding the slot values in order, a
/// test and the stamp; two witnesses of one C# type are the same closing exactly when their values are equal. A
/// declaration no such closing reaches keeps the C# it has always had.
/// </summary>
internal sealed class ClosingWitnesses : IDisposable
{
    public const string Runtime = "MistClosing";

    [ThreadStatic]
    private static ClosingWitnesses? current;

    [ThreadStatic]
    private static bool used;

    private readonly HashSet<TypeParameter> witnessed;

    private ClosingWitnesses(HashSet<TypeParameter> witnessed)
    {
        this.witnessed = witnessed;
    }

    public static IDisposable Enter(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        var sensitivity = SlotSensitivity.Discover(model);
        sensitivity.Report(SourceBodies.Of(model), SlotSensitivity.SourceDeclared(model), SourceTypes.ExtensionsOf(model), diagnostics);
        var roots = SourceBodies.Of(model).Select(root => root.Root).ToList();

        return Enter(Decide(sensitivity, SlotSensitivity.Closings(model, roots), SlotRoutes.Answers(model, slot => slot.TypeParametersList.Count > 0)));
    }

    public static IDisposable Enter(IEnumerable<TypeParameter> parameters)
    {
        current = new ClosingWitnesses(new HashSet<TypeParameter>(parameters, ReferenceEqualityComparer.Instance));
        return current;
    }

    public void Dispose()
    {
        current = null;
    }

    public static void Reset()
    {
        used = false;
    }

    public static bool Used()
    {
        return used;
    }

    public static HashSet<TypeParameter> Decide(
        SlotSensitivity sensitivity,
        IEnumerable<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)> closings,
        IEnumerable<(Method Slot, Method Answer)> answers)
    {
        var needing = Needing(sensitivity);
        var found = new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance);
        var fills = closings.Where(fill => !SlotSensitivity.IsClrGeneric(fill.Definition)).ToList();
        var linked = answers.ToList();
        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var (definition, arguments) in fills)
            {
                for(var i = 0; i < arguments.Count && i < definition.TypeParametersList.Count; i++)
                {
                    var parameter = definition.TypeParametersList[i];

                    if(!found.Contains(parameter) && needing.Contains(parameter) && !Specialized(parameter) && Carries(arguments[i], found))
                    {
                        changed |= found.Add(parameter);
                    }
                }
            }

            foreach(var (slot, answer) in linked)
            {
                for(var i = 0; i < slot.TypeParametersList.Count && i < answer.TypeParametersList.Count; i++)
                {
                    if(found.Contains(slot.TypeParametersList[i]) || found.Contains(answer.TypeParametersList[i]))
                    {
                        changed |= found.Add(slot.TypeParametersList[i]) | found.Add(answer.TypeParametersList[i]);
                    }
                }
            }
        }

        return found;
    }

    private static bool Specialized(TypeParameter parameter)
    {
        return parameter.DeclaringMethod is { } method ? UnionSpecialization.Specializes(method) : parameter.DeclaringType is { } owner && UnionSpecialization.Specializes(owner);
    }

    private static HashSet<TypeParameter> Needing(SlotSensitivity sensitivity)
    {
        var needing = new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance);
        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var parameter in sensitivity.Sensitive)
            {
                if(!needing.Contains(parameter) && sensitivity.ReasonsOf(parameter).Any(reason => reason.Kind switch
                {
                    SlotSensitivity.Kind.Test or SlotSensitivity.Kind.Storage or SlotSensitivity.Kind.Static or SlotSensitivity.Kind.Nested => true,
                    SlotSensitivity.Kind.Forwarded or SlotSensitivity.Kind.Answered => needing.Contains(reason.Target!),
                    _ => false,
                }))
                {
                    changed |= needing.Add(parameter);
                }
            }
        }

        return needing;
    }

    public static bool Carries(IMistType type, IReadOnlySet<TypeParameter> witnessedParameters)
    {
        return SlotSensitivity.RoutedClosings(type).Count > 0
            || SlotSensitivity.Parameters(type).Any(witnessedParameters.Contains);
    }

    public static bool Carries(IMistType type)
    {
        return current is not null && Carries(type, current.witnessed);
    }

    public static bool IsWitnessed(TypeParameter parameter)
    {
        return current?.witnessed.Contains(parameter is PackElementType element ? element.Pack : parameter) == true;
    }

    public static IReadOnlyList<TypeParameter> Witnessed(IReadOnlyList<TypeParameter> parameters)
    {
        return current is null ? [] : [.. parameters.Where(parameter => !parameter.IsDependent && IsWitnessed(parameter))];
    }

    public static IReadOnlyList<TypeParameter> Stored(IMistType? type)
    {
        var definition = type is ConstructedType constructed ? constructed.Definition : type;

        if(current is null || definition is not MemberType member)
        {
            return [];
        }

        return [.. member.DeclaringType is ConcreteType enclosing ? Stored(enclosing) : [], .. Witnessed(member.TypeParametersList)];
    }

    public static string Name(TypeParameter parameter)
    {
        return $"__closing_{DependentCarrierNames.Parameter(parameter)}";
    }

    public static string Type(TypeParameter parameter)
    {
        return Of(DependentCarrierNames.Parameter(parameter));
    }

    public static string Of(string type)
    {
        used = true;
        return $"{Runtime}<{type}>";
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine($"public sealed class {Runtime}<T> : System.IEquatable<{Runtime}<T>>");

        using(writer.Block())
        {
            writer.WriteLine($"public static readonly {Runtime}<T> Plain = new {Runtime}<T>(System.Array.Empty<object>(), static (value, values) => value is T, null);");
            writer.WriteBlankLine();
            writer.WriteLine("public readonly object[] Values;");
            writer.WriteBlankLine();
            writer.WriteLine("private readonly System.Func<object, object[], bool> test;");
            writer.WriteBlankLine();
            writer.WriteLine("private readonly System.Func<object[], T> stamp;");
            writer.WriteBlankLine();
            writer.WriteLine($"public {Runtime}(object[] values, System.Func<object, object[], bool> test, System.Func<object[], T> stamp)");

            using(writer.Block())
            {
                writer.WriteLine("Values = values;");
                writer.WriteLine("this.test = test;");
                writer.WriteLine("this.stamp = stamp;");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public bool Is(object value)");

            using(writer.Block())
            {
                writer.WriteLine("return test(value, Values);");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public T Stamp()");

            using(writer.Block())
            {
                writer.WriteLine("return stamp == null ? default(T) : stamp(Values);");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public bool Same(object[] values)");

            using(writer.Block())
            {
                writer.WriteLine("if (values.Length != Values.Length)");

                using(writer.Block())
                {
                    writer.WriteLine("return false;");
                }

                writer.WriteBlankLine();
                writer.WriteLine("for (var i = 0; i < Values.Length; i++)");

                using(writer.Block())
                {
                    writer.WriteLine("if (!object.Equals(Values[i], values[i]))");

                    using(writer.Block())
                    {
                        writer.WriteLine("return false;");
                    }
                }

                writer.WriteBlankLine();
                writer.WriteLine("return true;");
            }

            writer.WriteBlankLine();
            writer.WriteLine($"public bool Equals({Runtime}<T> other)");

            using(writer.Block())
            {
                writer.WriteLine("return other != null && Same(other.Values);");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public override bool Equals(object other)");

            using(writer.Block())
            {
                writer.WriteLine($"return Equals(other as {Runtime}<T>);");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public override int GetHashCode()");

            using(writer.Block())
            {
                writer.WriteLine("var hash = new System.HashCode();");
                writer.WriteBlankLine();
                writer.WriteLine("foreach (var value in Values)");

                using(writer.Block())
                {
                    writer.WriteLine("hash.Add(value);");
                }

                writer.WriteBlankLine();
                writer.WriteLine("return hash.ToHashCode();");
            }
        }
    }
}