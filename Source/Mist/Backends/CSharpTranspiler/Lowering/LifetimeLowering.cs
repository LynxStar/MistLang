using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A guarded local's flag changes inside the expression, for the reason <see cref="LifetimePlan"/> gives.
/// </summary>
internal static class LifetimeLowering
{
    [ThreadStatic]
    private static LifetimePlan? active;

    [ThreadStatic]
    private static Dictionary<object, string>? flags;

    [ThreadStatic]
    private static bool used;

    [ThreadStatic]
    private static Dictionary<Expression, (string Temp, string Flag)>? lent;

    [ThreadStatic]
    private static HashSet<Statement>? lending;

    [ThreadStatic]
    private static Dictionary<object, string>? receivers;

    [ThreadStatic]
    private static Dictionary<Block, List<(string Temp, string Flag, IMistType Type)>>? renewed;

    public static LifetimePlan Active => active ?? LifetimePlan.Empty;

    public static void Reset()
    {
        used = false;
    }

    public static bool Used()
    {
        return used;
    }

    public static Body? Enter(Block body, Method? method = null, ConcreteType? holder = null)
    {
        if(active is not null)
        {
            return null;
        }

        active = LocalLifetimes.Plan(body, method, holder);
        flags = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        lent = new Dictionary<Expression, (string, string)>(ReferenceEqualityComparer.Instance);
        lending = new HashSet<Statement>(ReferenceEqualityComparer.Instance);
        renewed = new Dictionary<Block, List<(string, string, IMistType)>>(ReferenceEqualityComparer.Instance);
        receivers = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        return new Body();
    }

    /// <summary>
    /// The temporaries holding what a statement lends -- see <see cref="LentValues"/> -- declared ahead of
    /// it and empty until the value is made, so a release finds nothing to drop on a path that never made
    /// it. Each statement is lent for once; the second visit is the emission of its own body.
    /// </summary>
    public static List<(string Temp, string Flag, IMistType Type)> Lend(Statement statement, CSharpWriter writer)
    {
        if(lent is null || lending is null || renewed is null || !lending.Add(statement))
        {
            return [];
        }

        var held = LentValues.Once(statement).Select(value => Hold(value, writer)).ToList();
        var repeated = LentValues.Repeated(statement).Select(value => Hold(value, writer)).ToList();

        if(repeated.Count > 0)
        {
            renewed[statement is WhileStatement loop ? loop.Body : ((ForStatement)statement).Body] = repeated;
        }

        return [.. held, .. repeated];
    }

    public static List<(string Temp, string Flag, IMistType Type)> Lend(Expression root, CSharpWriter writer)
    {
        return lent is null ? [] : [.. LentValues.Within(root).Select(value => Hold(value, writer))];
    }

    private static (string Temp, string Flag, IMistType Type) Hold(Expression value, CSharpWriter writer)
    {
        used = true;
        var temp = BodyEmitter.TemporaryName();
        var flag = BodyEmitter.TemporaryName();
        writer.WriteLine($"{TypeNameEmitter.EmitInferable(value.Type)} {temp} = default!;");
        writer.WriteLine($"var {flag} = false;");
        lent![value] = (temp, flag);
        return (temp, flag, value.Type);
    }

    public static (string Temp, string Flag)? LentAt(Expression value)
    {
        return lent is not null && lent.TryGetValue(value, out var held) ? held : null;
    }

    public static IReadOnlyList<(string Temp, string Flag, IMistType Type)> Renewed(Block body)
    {
        return renewed?.GetValueOrDefault(body) ?? [];
    }

    public static LifetimeRelease ReleaseOf(LocalDeclarationStatement declaration, IReadOnlyList<Statement> remainder)
    {
        return DropInsertion.ReleaseOf(declaration, remainder, Active);
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

        return Flag(local, declaration.Initializer is not null, writer);
    }

    public static string? Flag(object owner, bool owned, CSharpWriter writer)
    {
        if(flags is null || !Active.NeedsFlag(owner))
        {
            return null;
        }

        used = true;
        var flag = BodyEmitter.TemporaryName();
        flags[owner] = flag;
        writer.WriteLine($"var {flag} = {(owned ? "true" : "false")};");
        return flag;
    }

    public static string? FlagOf(object owner)
    {
        return flags?.GetValueOrDefault(owner);
    }

    public static IDisposable? EnterScope(WithBlockStatement scope, CSharpWriter writer)
    {
        if(Active.ReleaseAt(scope) is not var (owner, _) || FlagOf(owner) is not null || !Active.NeedsFlag(owner))
        {
            return null;
        }

        _ = Flag(owner, owned: true, writer);
        return new ScopeFlag(owner);
    }

    public static IReadOnlyList<(string Flag, bool Owned, string? Receiver)> MarksAfter(Expression node)
    {
        if(active is null || flags is null || active.MarksAfter(node) is not { Count: > 0 } planned)
        {
            return [];
        }

        var marks = new List<(string, bool, string?)>();

        foreach(var mark in planned)
        {
            if(flags.TryGetValue(mark.Owner, out var flag))
            {
                marks.Add((flag, mark.Owned, mark.Gate is { } gate ? receivers?.GetValueOrDefault(gate) : null));
            }
        }

        return marks;
    }

    public static void Capture(InvocationExpression call)
    {
        if(receivers is not null && call.Receiver is MemberAccessExpression access && Active.Gates(call))
        {
            used = true;
            var name = BodyEmitter.TemporaryName();
            receivers[call] = name;
            receivers[access.Receiver] = name;
        }
    }

    public static string? Captured(Expression receiver)
    {
        return receivers is not null && receivers.Remove(receiver, out var name) ? name : null;
    }

    public static void Open(IReadOnlyList<(string Flag, bool Owned, string? Receiver)> marks, CSharpWriter writer)
    {
        foreach(var (_, owned, receiver) in marks)
        {
            writer.Append(owned ? "MistOwnership.Taken(" : receiver is null ? "MistOwnership.Handed(" : $"MistOwnership.HandedTo({receiver}, ");
        }
    }

    public static void Close(IReadOnlyList<(string Flag, bool Owned, string? Receiver)> marks, CSharpWriter writer)
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
            writer.WriteLine("public static T Handed<T>(T value, scoped ref bool owned) where T : allows ref struct");
            using(writer.Block())
            {
                writer.WriteLine("owned = false;");
                writer.WriteLine("return value;");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static T Taken<T>(T value, scoped ref bool owned) where T : allows ref struct");
            using(writer.Block())
            {
                writer.WriteLine("owned = true;");
                writer.WriteLine("return value;");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static T HandedTo<TReceiver, T>(TReceiver receiver, T value, scoped ref bool owned) where T : allows ref struct");
            using(writer.Block())
            {
                writer.WriteLine("if (receiver is not null)");
                using(writer.Block())
                {
                    writer.WriteLine("owned = false;");
                }

                writer.WriteLine("return value;");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static T Present<T>(T value, out T present)");
            using(writer.Block())
            {
                writer.WriteLine("present = value;");
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
            lent = null;
            lending = null;
            renewed = null;
            receivers = null;
        }
    }

    private sealed class ScopeFlag(object owner) : IDisposable
    {
        public void Dispose()
        {
            _ = flags?.Remove(owner);
        }
    }
}