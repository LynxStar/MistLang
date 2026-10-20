using System.Runtime.CompilerServices;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The C# name each local of a body is emitted under. Mist lets a nested block redeclare a name an
/// enclosing block or the parameter list holds; C# refuses that (CS0136) whichever of the two comes
/// first in the text, so the inner local is renamed apart once per body and every site that spells a
/// local reads the map. Siblings keep their names, since C# allows one name in two blocks that do not
/// contain each other. Hidden discard locals keep distinct names even with no map active. A local function's
/// name is a local of the block that declares it and is renamed by the same rule; a type's method is
/// never in the map.
/// </summary>
internal static class LocalNames
{
    private static readonly ConditionalWeakTable<LocalVariable, string> Discards = new();

    private static long nextDiscard;

    [ThreadStatic]
    private static Dictionary<LocalVariable, string>? active;

    [ThreadStatic]
    private static Dictionary<Method, string>? activeFunctions;

    public static Body? Enter(Block body, IEnumerable<string> parameterNames)
    {
        if(active is not null)
        {
            return null;
        }

        (active, activeFunctions) = Build(body, parameterNames);
        return new Body();
    }

    public static string Of(LocalVariable local)
    {
        if(active is { } map && map.TryGetValue(local, out var name))
        {
            return name;
        }

        return local.IsDiscard
            ? Discards.GetValue(local, _ => $"__discard_{Interlocked.Increment(ref nextDiscard)}")
            : IdentifierSpelling.Of(local.Name);
    }

    public static string Of(Method function)
    {
        return activeFunctions is { } map && map.TryGetValue(function, out var name) ? name : IdentifierSpelling.Of(function.Name);
    }

    internal sealed class Body : IDisposable
    {
        public void Dispose()
        {
            active = null;
            activeFunctions = null;
        }
    }

    private sealed class Scope(Scope? parent)
    {
        public Scope? Parent { get; } = parent;

        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Declared
    {
        public List<(LocalVariable Local, Scope Scope)> Locals { get; } = [];

        public List<(Method Function, Scope Scope)> Functions { get; } = [];
    }

    private static (Dictionary<LocalVariable, string>, Dictionary<Method, string>) Build(
        Block body, IEnumerable<string> parameterNames)
    {
        var parameters = new Scope(null);
        parameters.Names.UnionWith(parameterNames);

        var declared = new Declared();
        Walk(body, new Scope(parameters), declared);

        var taken = new HashSet<string>(parameters.Names, StringComparer.Ordinal);
        taken.UnionWith(declared.Locals.Select(entry => entry.Local.Name));
        taken.UnionWith(declared.Functions.Select(entry => entry.Function.Name));

        var locals = new Dictionary<LocalVariable, string>(ReferenceEqualityComparer.Instance);
        var functions = new Dictionary<Method, string>(ReferenceEqualityComparer.Instance);

        foreach(var (local, scope) in declared.Locals)
        {
            if(local.IsDiscard)
            {
                locals[local] = Apart("__discard", taken);
            }
            else if(local.Name != "_" && Shadows(local.Name, scope))
            {
                locals[local] = Apart(local.Name, taken);
            }
        }

        foreach(var (function, scope) in declared.Functions)
        {
            if(Shadows(function.Name, scope))
            {
                functions[function] = Apart(function.Name, taken);
            }
        }

        return (locals, functions);
    }

    private static string Apart(string name, HashSet<string> taken)
    {
        var suffix = 1;
        string candidate;

        do
        {
            candidate = $"{name}_{suffix++}";
        }
        while(!taken.Add(candidate));

        return candidate;
    }

    private static bool Shadows(string name, Scope scope)
    {
        for(var enclosing = scope.Parent; enclosing is not null; enclosing = enclosing.Parent)
        {
            if(enclosing.Names.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static void Declare(LocalVariable local, Scope scope, Declared declared)
    {
        if(!local.IsDiscard)
        {
            _ = scope.Names.Add(local.Name);
        }

        declared.Locals.Add((local, scope));
    }

    private static void Walk(Block block, Scope scope, Declared declared)
    {
        foreach(var statement in block.Statements)
        {
            Walk(statement, scope, declared);
        }
    }

    private static void Walk(Statement statement, Scope scope, Declared declared)
    {
        switch(statement)
        {
            case LocalDeclarationStatement declaration:
                Declare(declaration.Local, scope, declared);
                Walk(declaration.Initializer, scope, declared);
                break;

            case ExpressionStatement expression:
                Walk(expression.Expression, scope, declared);
                break;

            case ReturnStatement returned:
                Walk(returned.Value, scope, declared);
                break;

            case IfStatement conditional:
                {
                    var header = conditional.ConditionBinds ? new Scope(scope) : scope;
                    Walk(conditional.Condition, header, declared);
                    Walk(conditional.ThenBranch, new Scope(header), declared);

                    if(conditional.ElseBranch is Block elseBlock)
                    {
                        Walk(elseBlock, new Scope(header), declared);
                    }
                    else if(conditional.ElseBranch is { } elseStatement)
                    {
                        Walk(elseStatement, header, declared);
                    }

                    break;
                }

            case WhileStatement loop:
                {
                    var header = loop.ConditionBinds ? new Scope(scope) : scope;
                    Walk(loop.Condition, header, declared);
                    Walk(loop.Body, new Scope(header), declared);
                    break;
                }

            case ForStatement counted:
                {
                    var header = new Scope(scope);

                    if(counted.Initializer is { } initializer)
                    {
                        Walk(initializer, header, declared);
                    }

                    Walk(counted.Condition, header, declared);
                    Walk(counted.Iterator, header, declared);
                    Walk(counted.Body, new Scope(header), declared);
                    break;
                }

            case ForEachStatement iteration:
                {
                    Walk(iteration.Collection, scope, declared);
                    var header = new Scope(scope);
                    Declare(iteration.Variable, header, declared);
                    Walk(iteration.Body, new Scope(header), declared);
                    break;
                }

            case WithBlockStatement withBlock:
                {
                    Walk(withBlock.Value, scope, declared);
                    var header = new Scope(scope);

                    if(withBlock.Binding is { } binding)
                    {
                        Declare(binding, header, declared);
                    }

                    Walk(withBlock.Body, new Scope(header), declared);
                    break;
                }

            case TupleDeconstructionStatement deconstruction:
                foreach(var target in deconstruction.Targets)
                {
                    Declare(target, scope, declared);
                }

                Walk(deconstruction.Initializer, scope, declared);
                break;

            case Block nested:
                Walk(nested, new Scope(scope), declared);
                break;

            case LocalFunctionStatement function:
                {
                    _ = scope.Names.Add(function.Function.Name);
                    declared.Functions.Add((function.Function, scope));
                    var header = new Scope(scope);

                    foreach(var parameter in function.Parameters)
                    {
                        Declare(parameter, header, declared);
                    }

                    Walk(function.Body, new Scope(header), declared);
                    break;
                }
        }
    }

    private static void Walk(Expression? expression, Scope scope, Declared declared)
    {
        switch(expression)
        {
            case null:
                return;

            case LambdaExpression lambda:
                {
                    var header = new Scope(scope);

                    foreach(var parameter in lambda.Parameters)
                    {
                        Declare(parameter, header, declared);
                    }

                    if(lambda.Body is { } body)
                    {
                        Walk(body, new Scope(header), declared);
                    }

                    Walk(lambda.Result, header, declared);
                    return;
                }

            case AsyncBlockExpression asyncBlock:
                Walk(asyncBlock.Body, new Scope(scope), declared);
                return;

            case IsPatternExpression test:
                Walk(test.Scrutinee, scope, declared);
                Walk(test.Pattern, scope, declared);
                return;

            case SwitchExpression switched:
                Walk(switched.Scrutinee, scope, declared);

                foreach(var arm in switched.Arms)
                {
                    var armScope = new Scope(scope);
                    Walk(arm.Pattern, armScope, declared);
                    Walk(arm.Guard, armScope, declared);
                    Walk(arm.Result, armScope, declared);
                }

                return;
        }

        foreach(var child in BodyWalker.ChildrenOf(expression))
        {
            switch(child)
            {
                case Expression nested:
                    Walk(nested, scope, declared);
                    break;

                case InterpolatedStringHole hole:
                    Walk(hole.Expression, scope, declared);
                    break;

                case ObjectInitializerAssignment assignment:
                    Walk(assignment.Value, scope, declared);
                    break;
            }
        }
    }

    private static void Walk(Pattern pattern, Scope scope, Declared declared)
    {
        if(pattern.Binding is { } binding)
        {
            Declare(binding, scope, declared);
        }

        switch(pattern)
        {
            case TuplePattern tuple:
                foreach(var element in tuple.Elements)
                {
                    Walk(element, scope, declared);
                }

                break;

            case PropertyPattern property:
                foreach(var subpattern in property.Subpatterns)
                {
                    Walk(subpattern.Pattern, scope, declared);
                }

                break;

            case ListPattern list:
                foreach(var element in list.Elements)
                {
                    if(element.Subpattern is { } subpattern)
                    {
                        Walk(subpattern, scope, declared);
                    }
                }

                break;

            case ConjunctionPattern conjunction:
                foreach(var operand in conjunction.Operands)
                {
                    Walk(operand, scope, declared);
                }

                break;

            case DisjunctionPattern disjunction:
                foreach(var operand in disjunction.Operands)
                {
                    Walk(operand, scope, declared);
                }

                break;

            case NegatedPattern negated:
                Walk(negated.Operand, scope, declared);
                break;
        }
    }
}