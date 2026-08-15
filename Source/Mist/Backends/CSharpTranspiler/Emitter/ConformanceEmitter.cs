using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The members that make a retroactive conformance real in C#: one per mapping, typed from the SLOT
/// and bodied by a call to the member mapped onto it.
/// <para>
/// EXPLICIT rather than public, which is what keeps Mist and the emitted C# reachable through the
/// same names. A with block does not add the slot to what the conforming type answers to -- Mist
/// member lookup does not walk
/// <see cref="ConcreteType.ConformancesList"/> and neither does classic -- so a public member would
/// make <c>logger.Name()</c> compile in C# while failing in Mist. It also has to be explicit
/// whenever the mapped member differs from the slot only in name, since a public method of the
/// slot's own signature would collide with nothing and simply not implement it.
/// </para>
/// </summary>
internal static class ConformanceEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        // Seeded from the declared abstracts, so one comparison catches both spellings of the same
        // fault: a with block restating what the base list says, and two blocks naming one pair.
        var reached = new HashSet<string>(type.BaseAbstractTypes.Select(TypeNameEmitter.Emit), StringComparer.Ordinal);

        foreach(var conformance in type.ConformancesList)
        {
            if(!reached.Add(TypeNameEmitter.Emit(conformance.AbstractType)))
            {
                diagnostics.ReportAtNode(
                    BackendDiagnostics.RedundantConformance,
                    conformance.DeclarationNode,
                    null,
                    TypeDisplay.Mist(type),
                    TypeDisplay.Mist(conformance.AbstractType));
                continue;
            }

            foreach(var mapping in conformance.Mappings)
            {
                TypeEmitter.SeparateMember(writer, ref anyEmitted);

                var interfaceName = TypeNameEmitter.Emit(mapping.Declarer);

                switch(mapping)
                {
                    case MethodMapping method:
                        EmitMethod(method, interfaceName, writer);
                        break;

                    case PropertyMapping property:
                        EmitProperty(property, interfaceName, writer);
                        break;
                }
            }
        }
    }

    private static void EmitMethod(MethodMapping mapping, string interfaceName, CSharpWriter writer)
    {
        var slot = mapping.Target;
        var returnType = TypeNameEmitter.Emit(slot.ReturnType);
        var typeParams = slot.TypeParametersList.Count > 0
            ? $"<{string.Join(", ", slot.TypeParametersList.Select(tp => tp.Name))}>"
            : "";
        var parameters = ParameterEmitter.Emit(slot.ParametersList);
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