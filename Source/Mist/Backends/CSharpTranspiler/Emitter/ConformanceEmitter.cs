using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// EXPLICIT rather than public: a mapping may carry the slot's own name, where a public forwarder
/// would redeclare the member it forwards to. Why the Mist-visible and C#-visible member sets have to
/// agree is at <see cref="ConcreteType.ConformancesList"/>.
/// </summary>
internal static class ConformanceEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        var reached = new HashSet<string>(type.BaseAbstractTypes.Select(TypeNameEmitter.Emit), StringComparer.Ordinal);

        foreach(var conformance in type.ConformancesList)
        {
            if(!reached.Add(TypeNameEmitter.Emit(conformance.AbstractType)))
            {
                continue;
            }

            foreach(var mapping in conformance.Mappings)
            {
                TypeEmitter.SeparateMember(writer, ref anyEmitted);

                var interfaceName = TypeNameEmitter.Emit(mapping.Declarer);

                switch(mapping)
                {
                    case MethodMapping method:
                        EmitMethod(method, interfaceName, writer, diagnostics);
                        break;

                    case PropertyMapping property:
                        EmitProperty(property, interfaceName, writer);
                        break;
                }
            }
        }
    }

    private static void EmitMethod(
        MethodMapping mapping, string interfaceName, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var slot = mapping.Target;
        var returnType = TypeNameEmitter.Emit(slot.ReturnType);
        var typeParams = slot.TypeParametersList.Count > 0
            ? $"<{string.Join(", ", slot.TypeParametersList.Select(tp => tp.Name))}>"
            : "";
        var parameters = ParameterEmitter.Emit(slot.ParametersList, diagnostics);
        var arguments = string.Join(", ", slot.ParametersList.Select(parameter => parameter.Name));

        writer.WriteLine(
            $"{returnType} {interfaceName}.{slot.Name}{typeParams}({parameters})"
            + $" => this.{mapping.Source.Name}({arguments});");
    }

    private static void EmitProperty(PropertyMapping mapping, string interfaceName, CSharpWriter writer)
    {
        var slot = mapping.Target;
        var accessors = new List<string>(2);

        if(slot.Getter is not null)
        {
            accessors.Add($"get => this.{mapping.Source.Name};");
        }

        if(slot.Setter is not null)
        {
            accessors.Add($"set => this.{mapping.Source.Name} = value;");
        }

        writer.WriteLine(
            $"{TypeNameEmitter.Emit(slot.Type)} {interfaceName}.{slot.Name}"
            + $" {{ {string.Join(" ", accessors)} }}");
    }
}