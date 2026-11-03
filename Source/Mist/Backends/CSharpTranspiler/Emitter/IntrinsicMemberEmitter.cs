using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The Mist-bodied members of an intrinsic type, carried by an extension block on the backing type
/// because the backing type itself is the target's and cannot be reopened. Written into one file in
/// the global namespace so a call resolves from any file whatever it imports, exactly as MistArray
/// and MistTasks are supplied; the intrinsic members are the backing type's own and are not written.
/// </summary>
internal static class IntrinsicMemberEmitter
{
    public static bool Carries(ConcreteType type)
    {
        return Methods(type).Any() || Properties(type).Any() || Operators(type).Any();
    }

    public static void EmitFile(
        ProgramModelClass programModel, string outputDir, List<string> emittedFiles, DiagnosticBag diagnostics)
    {
        var carried = IntrinsicSupply.IntrinsicTypes(programModel).Where(Carries).ToList();

        if(carried.Count == 0)
        {
            return;
        }

        var writer = new CSharpWriter();
        Emit(carried, writer, diagnostics);

        var path = Path.Combine(outputDir, "__Intrinsics.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    public static void Emit(IReadOnlyList<ConcreteType> types, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistIntrinsics");

        using(writer.Block())
        {
            var first = true;

            foreach(var type in types)
            {
                EmitBlock(type, isStatic: false, writer, diagnostics, ref first);
                EmitBlock(type, isStatic: true, writer, diagnostics, ref first);
            }

            EmitOperatorBlocks(types, writer, diagnostics, ref first);
        }
    }

    /// <summary>
    /// A non-intrinsic operator on an intrinsic type is carried as a C# extension operator, grouped by
    /// an operand type it can extend: the backing primitive cannot be reopened, so the composition
    /// overloads (exceptions Requirement 4) land on their Result operand, which is present in every
    /// arrangement including Result on both sides.
    /// </summary>
    private static void EmitOperatorBlocks(
        IReadOnlyList<ConcreteType> types, CSharpWriter writer, DiagnosticBag diagnostics, ref bool first)
    {
        var groups = types
            .SelectMany(type => Operators(type).Select(op => (Op: op, Receiver: Receiver(op, type))))
            .GroupBy(entry => TypeNameEmitter.Emit(entry.Receiver))
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach(var group in groups)
        {
            if(!first)
            {
                writer.WriteBlankLine();
            }

            first = false;

            writer.WriteLine($"extension({group.Key})");

            using(writer.Block())
            {
                var firstMember = true;

                foreach(var entry in group)
                {
                    Separate(writer, ref firstMember);
                    OperatorEmitter.Emit(entry.Op, writer, diagnostics);
                }
            }
        }
    }

    private static IMistType Receiver(Operator op, ConcreteType declaring)
    {
        foreach(var parameter in op.ParametersList)
        {
            if(parameter.Type.FullyQualifiedName != declaring.FullyQualifiedName)
            {
                return parameter.Type;
            }
        }

        return declaring;
    }

    private static void EmitBlock(
        ConcreteType type, bool isStatic, CSharpWriter writer, DiagnosticBag diagnostics, ref bool first)
    {
        var methods = Methods(type).Where(method => method.Modifiers.IsStatic == isStatic).ToList();
        var properties = Properties(type).Where(property => property.Modifiers.IsStatic == isStatic).ToList();

        if(methods.Count == 0 && properties.Count == 0)
        {
            return;
        }

        if(!first)
        {
            writer.WriteBlankLine();
        }

        first = false;

        var parameters = type.TypeParametersList.Count > 0
            ? $"<{string.Join(", ", type.TypeParametersList.Select(parameter => parameter.Name))}>"
            : "";
        var receiver = TypeNameEmitter.Emit(type) + (isStatic ? "" : $" {ReceiverSpelling.Carrier}");

        writer.WriteLine($"extension{parameters}({receiver})");

        using(writer.Block())
        using(ReceiverSpelling.Enter(type))
        {
            var firstMember = true;

            foreach(var method in methods)
            {
                Separate(writer, ref firstMember);
                MethodEmitter.Emit(method, emitReadonly: false, writer, diagnostics);
            }

            foreach(var property in properties)
            {
                Separate(writer, ref firstMember);
                PropertyEmitter.Emit(property, emitReadonly: false, readonlyGetter: false, writer, diagnostics);
            }
        }
    }

    private static void Separate(CSharpWriter writer, ref bool first)
    {
        if(!first)
        {
            writer.WriteBlankLine();
        }

        first = false;
    }

    private static IEnumerable<Method> Methods(ConcreteType type)
    {
        return type.MethodsList.Where(method => !method.Modifiers.IsIntrinsic);
    }

    private static IEnumerable<Property> Properties(ConcreteType type)
    {
        return type.PropertiesList.Where(property => !property.Modifiers.IsIntrinsic);
    }

    private static IEnumerable<Operator> Operators(ConcreteType type)
    {
        return type.OperatorsList.Where(op => !op.Modifiers.IsIntrinsic);
    }
}