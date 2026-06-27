using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Determines the optimal C# task type (<c>ValueTask&lt;T&gt;</c> vs <c>Task&lt;T&gt;</c>)
/// for each async method based on how callers use the returned task.
///
/// <para>
/// A method qualifies for <c>ValueTask&lt;T&gt;</c> when its returned task is:
/// <list type="bullet">
///   <item>Awaited at most once.</item>
///   <item>Not assigned to a variable that is read from multiple await sites.</item>
///   <item>Not passed as a method argument.</item>
///   <item>Not returned from the enclosing method.</item>
/// </list>
/// </para>
///
/// <para>
/// The conservative default is <c>Task&lt;T&gt;</c>. Only when the single-await pattern
/// is proven across all call sites does the pass mark the method for <c>ValueTask&lt;T&gt;</c>.
/// </para>
///
/// <para>
/// <strong>Reconciliation with explicit ValueTask return types:</strong> This optimization
/// applies ONLY to async methods whose return type was inferred (the compiler wrapped
/// the declared return type in <c>Task&lt;T&gt;</c>). Methods with an explicitly-written
/// <c>ValueTask&lt;TResult&gt;</c> return type are excluded — their emission is driven by
/// the intrinsic type path in <c>EmitAsyncReturnType</c>, not by <see cref="Symbol.UseValueTask"/>.
/// Use <see cref="HasExplicitValueTaskReturnType"/> to test for this condition.
/// </para>
///
/// <para>
/// <strong>Current implementation:</strong> Conservative — always emits <c>Task&lt;T&gt;</c>.
/// The <see cref="Symbol.UseValueTask"/> property remains <c>false</c> for all methods.
/// The infrastructure is in place for future optimization when the full usage analysis
/// is implemented.
/// </para>
///
/// Results are stored on each method symbol's <see cref="Symbol.UseValueTask"/> property.
/// </summary>
internal static class ValueTaskUsageAnalysisPass
{
    /// <summary>
    /// Runs the usage analysis pass over the semantic model.
    /// After this method returns, async method symbols that qualify for
    /// <c>ValueTask&lt;T&gt;</c> will have <see cref="Symbol.UseValueTask"/> set to <c>true</c>.
    ///
    /// <para>
    /// <strong>Current behavior:</strong> Conservative — no methods are marked for
    /// <c>ValueTask&lt;T&gt;</c>. All async methods emit <c>Task&lt;T&gt;</c>.
    /// </para>
    /// </summary>
    public static void Run()
    {
        // Conservative implementation: leave UseValueTask = false for all methods.
        //
        // Future optimization: walk all method bodies to analyze call sites and
        // determine which async methods have their returned task:
        //   - Awaited at most once (qualifies for ValueTask)
        //   - Awaited multiple times (disqualified → Task)
        //   - Assigned to a variable read from multiple await sites (disqualified → Task)
        //   - Passed as a method argument (disqualified → Task)
        //   - Returned from the enclosing method (disqualified → Task)
        //
        // The analysis is per-method: it determines what the callee's return type
        // should be based on how callers use the returned task. A method is
        // disqualified from ValueTask if ANY call site exhibits a multi-use pattern.
        //
        // IMPORTANT: The UseValueTask optimization applies ONLY to async methods whose
        // return type was inferred (i.e., the compiler wrapped the declared return type
        // in Task<T>). Methods with an explicitly-written ValueTask<TResult> return type
        // are excluded — their emission is driven by the intrinsic type path in
        // EmitAsyncReturnType, not by this flag. The two notions must not compete for
        // the same method.
        //
        // Guard: when the optimization is eventually enabled, skip any method whose
        // EffectiveReturnType is already ValueTask<T> (explicit user-written return type).
        // Such methods are identified by their EffectiveReturnType being a ConstructedType
        // whose GenericDefinition is the Standard.ValueTask class.
    }

    /// <summary>
    /// Returns <c>true</c> if the method's return type was explicitly written as
    /// <c>ValueTask&lt;TResult&gt;</c> (or the non-generic <c>ValueTask</c> void alias).
    /// Such methods are driven by the intrinsic type emission path in
    /// <see cref="CSharpEmitter"/> and must NOT have <see cref="Symbol.UseValueTask"/>
    /// set by this optimization pass.
    /// </summary>
    internal static bool HasExplicitValueTaskReturnType(Symbol methodSymbol)
    {
        if(methodSymbol.EffectiveReturnType is not ConstructedType ct || ct.TypeArguments.Count == 0)
        {
            return false;
        }

        return ct.GenericDefinition is ClassType { DisplayName: "ValueTask", ContainingNamespace: "Standard" };
    }
}