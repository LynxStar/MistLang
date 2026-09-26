using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A chain of <c>&amp;&amp;</c> or of <c>||</c> split into guards, a new guard starting at each link that needs
/// statements run ahead of it, so those statements run only once every link before them has decided the chain
/// is not yet settled. The guards nest -- or, in a loop condition, break out one at a time -- rather than
/// storing each link's value in a boolean, because a pattern variable an earlier link binds must still read as
/// assigned in the later links and in the body, which C# cannot see through a stored boolean.
/// </summary>
internal static class GuardChain
{
    public static IEnumerable<Expression> Links(Expression expression, BinaryOperatorKind kind)
    {
        return expression is BinaryExpression binary && binary.OperatorKind == kind
            ? [.. Links(binary.Left, kind), .. Links(binary.Right, kind)]
            : [expression];
    }

    public static List<List<Expression>> Split(Expression chain, BinaryOperatorKind kind = BinaryOperatorKind.LogicalAnd)
    {
        var guards = new List<List<Expression>>();

        foreach(var link in Links(chain, kind))
        {
            if(guards.Count == 0 || !UnitHoisting.Plan(link).IsEmpty)
            {
                guards.Add([]);
            }

            guards[^1].Add(link);
        }

        return guards;
    }

    public static void EmitBreaks(List<List<Expression>> guards, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var guard in guards)
        {
            writer.WriteLine($"if (!({Test(guard, BinaryOperatorKind.LogicalAnd, writer, diagnostics)}))");

            using(writer.Block())
            {
                writer.WriteLine("break;");
            }

            writer.WriteBlankLine();
        }
    }

    public static void EmitIf(
        IfStatement ifStmt, List<List<Expression>> guards, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var taken = ifStmt.ElseBranch is null ? null : BodyEmitter.TemporaryName();

        if(taken is not null)
        {
            writer.WriteLine($"bool {taken} = false;");
        }

        EmitNested(guards, BinaryOperatorKind.LogicalAnd, writer, diagnostics, () =>
        {
            if(taken is not null)
            {
                writer.WriteLine($"{taken} = true;");
            }

            BodyEmitter.EmitBlock(ifStmt.ThenBranch, writer, diagnostics);
        });

        if(taken is null)
        {
            return;
        }

        writer.WriteLine($"if (!{taken})");

        using(writer.Block())
        {
            if(ifStmt.ElseBranch is Block elseBlock)
            {
                BodyEmitter.EmitBlock(elseBlock, writer, diagnostics);
            }
            else
            {
                BodyEmitter.EmitStatement(ifStmt.ElseBranch!, writer, diagnostics);
            }
        }
    }

    public static void EmitNested(
        List<List<Expression>> guards, BinaryOperatorKind kind, CSharpWriter writer, DiagnosticBag diagnostics, Action innermost)
    {
        EmitNested(guards, 0, kind, writer, diagnostics, innermost);
    }

    private static void EmitNested(
        List<List<Expression>> guards,
        int index,
        BinaryOperatorKind kind,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        Action innermost)
    {
        var test = Test(guards[index], kind, writer, diagnostics);
        writer.WriteLine(kind is BinaryOperatorKind.LogicalAnd ? $"if ({test})" : $"if (!({test}))");

        using(writer.Block())
        {
            if(index + 1 < guards.Count)
            {
                EmitNested(guards, index + 1, kind, writer, diagnostics, innermost);
                return;
            }

            innermost();
        }
    }

    private static string Test(List<Expression> guard, BinaryOperatorKind kind, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var hoisted = guard.Select(link => BodyEmitter.EmitHoisted(link, writer, diagnostics)).ToList();

        try
        {
            return string.Join(
                kind is BinaryOperatorKind.LogicalAnd ? " && " : " || ",
                guard.Select(link => BodyEmitter.RenderAt(link, BodyEmitter.GetPrecedence(kind), diagnostics)));
        }
        finally
        {
            foreach(var hoisting in hoisted)
            {
                hoisting.Dispose();
            }
        }
    }
}