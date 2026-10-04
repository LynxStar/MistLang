using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A guarded local's flag changes inside the expression, for the reason <see cref="LifetimePlan"/> gives.
/// </summary>
internal static class LifetimeLowering
{
    [ThreadStatic]
    private static LifetimePlan? active;

    [ThreadStatic]
    private static Dictionary<LocalVariable, string>? flags;

    [ThreadStatic]
    private static bool used;

    public static void Reset()
    {
        used = false;
    }

    public static bool Used()
    {
        return used;
    }

    public static Body? Enter(Block body)
    {
        if(active is not null)
        {
            return null;
        }

        active = LocalLifetimes.Plan(body);
        flags = new Dictionary<LocalVariable, string>(ReferenceEqualityComparer.Instance);
        return new Body();
    }

    public static LifetimeRelease ReleaseOf(LocalDeclarationStatement declaration, IReadOnlyList<Statement> remainder)
    {
        return DropInsertion.ReleaseOf(declaration, remainder, active ?? LifetimePlan.Empty);
    }

    public static string? Declare(
        LocalDeclarationStatement declaration, LifetimeRelease release, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var local = declaration.Local;

        if(release is not LifetimeRelease.Guarded || flags is null)
        {
            BodyEmitter.EmitStatement(declaration, writer, diagnostics);
            return null;
        }

        if(declaration.Initializer is null)
        {
            writer.WriteLine($"{TypeNameEmitter.EmitInferable(local.Type)} {LocalNames.Of(local)} = default!;");
        }
        else
        {
            BodyEmitter.EmitStatement(declaration, writer, diagnostics);
        }

        used = true;
        var flag = BodyEmitter.TemporaryName();
        flags[local] = flag;
        writer.WriteLine($"var {flag} = {(declaration.Initializer is null ? "false" : "true")};");
        return flag;
    }

    public static IReadOnlyList<(string Flag, bool Owned)> MarksAfter(Expression node)
    {
        if(active is null || flags is null || active.MarksAfter(node) is not { Count: > 0 } planned)
        {
            return [];
        }

        var marks = new List<(string, bool)>();

        foreach(var mark in planned)
        {
            if(flags.TryGetValue(mark.Local, out var flag))
            {
                marks.Add((flag, mark.Owned));
            }
        }

        return marks;
    }

    public static void Open(IReadOnlyList<(string Flag, bool Owned)> marks, CSharpWriter writer)
    {
        foreach(var (_, owned) in marks)
        {
            writer.Append(owned ? "MistOwnership.Taken(" : "MistOwnership.Handed(");
        }
    }

    public static void Close(IReadOnlyList<(string Flag, bool Owned)> marks, CSharpWriter writer)
    {
        for(var i = marks.Count - 1; i >= 0; i--)
        {
            writer.Append($", ref {marks[i].Flag})");
        }
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistOwnership");

        using(writer.Block())
        {
            writer.WriteLine("public static T Handed<T>(T value, ref bool owned)");
            using(writer.Block())
            {
                writer.WriteLine("owned = false;");
                writer.WriteLine("return value;");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static T Taken<T>(T value, ref bool owned)");
            using(writer.Block())
            {
                writer.WriteLine("owned = true;");
                writer.WriteLine("return value;");
            }
        }
    }

    internal sealed class Body : IDisposable
    {
        public void Dispose()
        {
            active = null;
            flags = null;
        }
    }
}