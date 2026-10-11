using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A value with no statement of its own -- a field or property initializer, an argument chained to a base or
/// sibling constructor -- that needs statements run ahead of it. They run in a private static method called
/// where the value stood, taking the constructor parameters the value reads. Static because C# lets none of
/// these positions read the instance; a method rather than a lambda because a lambda captures, and a
/// by-reference parameter cannot be captured.
/// </summary>
internal static class StatementHost
{
    public static Hosted? Emit(
        Expression value,
        string name,
        IMistType type,
        IReadOnlyList<Parameter> parameters,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        if(!BodyEmitter.NeedsStatements(value, discarded: false))
        {
            BodyEmitter.EmitEntering(value, type, writer, diagnostics);
            return null;
        }

        var read = Read(value, parameters);

        writer.Append($"{name}({string.Join(", ", read.Select(parameter => ArgumentKeywords.For(parameter) + parameter.Name))})");
        return new Hosted(name, value, type, read);
    }

    public static void EmitMethods(IEnumerable<Hosted> hosted, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var method in hosted)
        {
            writer.WriteBlankLine();
            writer.WriteLine(
                $"private static {TypeNameEmitter.Emit(method.Type)} {method.Name}({ParameterEmitter.Emit(method.Parameters, diagnostics)})");

            using(writer.Block())
            {
                BodyEmitter.EmitSequenced(method.Value, "return ", writer, diagnostics, slot: method.Type);
            }
        }
    }

    private static List<Parameter> Read(Expression value, IReadOnlyList<Parameter> parameters)
    {
        var names = BodyWalker.DescendantsAndSelf(value)
            .OfType<LocalReferenceExpression>()
            .Select(reference => reference.Local.Name)
            .ToHashSet(StringComparer.Ordinal);

        return [.. parameters.Where(parameter => names.Contains(parameter.Name))];
    }

    internal sealed record Hosted(string Name, Expression Value, IMistType Type, IReadOnlyList<Parameter> Parameters);
}