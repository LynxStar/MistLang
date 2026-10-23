using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// One C# declaration per (variadic family, arity) the program uses.
/// <para>
/// MONOMORPHIZATION IS THE TARGET'S REQUIREMENT AND NOT THE LANGUAGE'S. Mist writes a variadic
/// family once and its arity is unbounded; C# has no pack, so a program that used the family at
/// two widths needs two declarations. Nothing about this reaches the model, which keeps the pack
/// symbolic -- what changes is only what is WRITTEN, and only here.
/// </para>
/// <para>
/// Each is written in its family's namespace, and <see cref="MonomorphReference.Emit"/> names it
/// qualified by that namespace: the specialized name adds only widths to the simple one, so two
/// families of one name in two namespaces would otherwise be one C# type.
/// </para>
/// </summary>
internal static class MonomorphEmitter
{
    public static bool Emit(
        IReadOnlyList<(MistType Definition, IReadOnlyList<int> Widths)> monomorphs,
        ConstraintClosureMap closures,
        VariadicUseRegistry uses,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        var anyNamespace = false;

        foreach(var group in monomorphs.GroupBy(monomorph => MonomorphReference.NamespaceOf(monomorph.Definition), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var body = new CSharpWriter();
            var anyEmitted = false;

            if(group.Key.Length > 0)
            {
                body.Indent();
            }

            foreach(var (definition, widths) in group)
            {
                if(definition is not MemberType member
                    || UnionSpecialization.Specializes(member)
                    || MonomorphSet.TypeSpecialization(member, widths) is not { } monomorph)
                {
                    continue;
                }

                using(monomorph.Enter())
                {
                    TypeEmitter.Emit(member, closures, uses, body, diagnostics, ref anyEmitted);
                }
            }

            if(!anyEmitted)
            {
                continue;
            }

            if(anyNamespace)
            {
                writer.WriteBlankLine();
            }

            anyNamespace = true;

            if(group.Key.Length == 0)
            {
                writer.Append(body.ToString());
                continue;
            }

            writer.WriteLine($"namespace {IdentifierSpelling.Qualified(group.Key)}");
            writer.WriteLine("{");
            writer.Append(body.ToString());
            writer.WriteLine("}");
        }

        return anyNamespace;
    }
}