using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which Mist-SYNC methods must be emitted as C# async ones. A sync context drops the tasks it calls,
/// and C# has only two ways to complete one: an <c>await</c>, which requires the enclosing method be
/// async, or a blocking get, which occupies a thread and deadlocks under any synchronization context.
/// So the obligation travels: a method dropping a task becomes async, its callers must await IT, and
/// they become async in turn.
/// <para>
/// A TARGET CONCERN, NOT A LANGUAGE ONE. Mist's method is sync and its callers see a sync method; this
/// is only how C# is made to honour that. A backend with blocking primitives would read the same
/// registry and emit none of it.
/// </para>
/// <para>
/// Ambient for the duration of an emission, for the reason <see cref="Monomorph"/> gives: the
/// alternative is a parameter threaded through every emitter that writes a signature or a call.
/// </para>
/// </summary>
internal sealed class AsyncPromotion
{
    [ThreadStatic]
    private static AsyncPromotion? inScope;

    private readonly IReadOnlySet<Method> promoted;
    private readonly ProgramModelClass programModel;

    private AsyncPromotion(IReadOnlySet<Method> promoted, ProgramModelClass programModel)
    {
        this.promoted = promoted;
        this.programModel = programModel;
    }

    /// <summary>
    /// A promoted method's emitted return type. Interned through the same definition the frontend's
    /// own async transform uses, so a promoted `void` and a declared `async void` reach emission as
    /// one type rather than two that only spell alike.
    /// </summary>
    public static IMistType WrapReturn(IMistType result)
    {
        if(inScope is not { } scope
            || scope.programModel.TypesByFQN.GetValueOrDefault(
                WellKnownTypes.TaskGeneric.FullyQualifiedName) is not MistType definition)
        {
            return result;
        }

        return scope.programModel.TypeInterner.GetOrCreateConstructedType(definition, new[] { result });
    }

    /// <summary>
    /// Whether a method Mist declared SYNC is emitted async, which is also exactly when a call to it
    /// has to be awaited: its emitted return type is a task the model never mentions.
    /// </summary>
    public static bool IsPromoted(Method? method)
    {
        return method is not null && method.DeclaringType is not AbstractType
            && inScope is { } scope && scope.promoted.Contains(Declaration(method));
    }

    private static Method Declaration(Method method)
    {
        return method.SourceDeclaration is { } source ? Declaration(source) : method;
    }

    /// <summary>
    /// Whether the emitted method carries the <c>async</c> modifier -- because Mist said so, or
    /// because the obligation reached it.
    /// </summary>
    public static bool EmitsAsync(Method method)
    {
        return method.Modifiers.IsAsync || IsPromoted(method);
    }

    public static IDisposable Enter(ProgramModelClass programModel)
    {
        inScope = new AsyncPromotion(Compute(programModel.TaskDrops, programModel), programModel);
        return new Scope();
    }

    /// <summary>
    /// The bodies dropping a task, then everything that reaches one. Seeded from bodies rather than from
    /// declarations because an inserted drop is what makes a sync method need this, and a method
    /// Mist declared async is excluded throughout -- it already carries the modifier and its callers
    /// already await it, so promoting it again would wrap its return type twice.
    /// </summary>
    private static IReadOnlySet<Method> Compute(TaskDropRegistry drops, ProgramModelClass programModel)
    {
        var promoted = new HashSet<Method>(drops.Dropping.Select(Declaration)
            .Where(method => !method.Modifiers.IsAsync && method.DeclaringType is not AbstractType));

        // RETROACTIVE, and the binder could not have known: a scope holding a task owes its completion,
        // and completing it is an await this pass inserts. So the obligation makes the method async
        // after the fact, exactly as a written await would have.
        foreach(var method in Methods(programModel))
        {
            if(!method.Modifiers.IsAsync && method.DeclaringType is not AbstractType
                && method.Body is { } body
                && DropInsertion.ReleasedBindings(body).Any(
                    declaration => DropInsertion.IsAwaitable(declaration.Local.Type)))
            {
                _ = promoted.Add(method);
            }
        }

        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var caller in drops.Callers)
            {
                var declaration = Declaration(caller);
                if(declaration.Modifiers.IsAsync || declaration.DeclaringType is AbstractType || promoted.Contains(declaration))
                {
                    continue;
                }

                if(drops.CalleesOf(caller).Any(callee => promoted.Contains(Declaration(callee))))
                {
                    _ = promoted.Add(declaration);
                    changed = true;
                }
            }
        }

        return promoted;
    }

    /// <summary>
    /// Every body that can owe an await: type and extension methods, the top-level statements, and every local
    /// function declared inside any of them, since a local function is emitted as a C# function of its
    /// own and can be made async on its own.
    /// </summary>
    private static IEnumerable<Method> Methods(ProgramModelClass programModel)
    {
        foreach(var assembly in programModel.Assemblies)
        {
            foreach(var unit in assembly.CompilationUnits)
            {
                if(unit.TopLevelMethod is { } topLevel)
                {
                    foreach(var function in LocalFunctionsOf(topLevel))
                    {
                        yield return function;
                    }
                }

                foreach(var type in unit.Types.OfType<MemberType>())
                {
                    foreach(var method in type.MethodsList)
                    {
                        yield return method;

                        foreach(var function in LocalFunctionsOf(method))
                        {
                            yield return function;
                        }
                    }
                }

                foreach(var method in unit.Extensions.SelectMany(block => block.MethodsList))
                {
                    yield return method;

                    foreach(var function in LocalFunctionsOf(method))
                    {
                        yield return function;
                    }
                }
            }
        }
    }

    private static IEnumerable<Method> LocalFunctionsOf(Method method)
    {
        return method.Body is { } body
            ? BodyWalker.DescendantsAndSelf(body).OfType<LocalFunctionStatement>().Select(statement => statement.Function)
            : [];
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            inScope = null;
        }
    }
}