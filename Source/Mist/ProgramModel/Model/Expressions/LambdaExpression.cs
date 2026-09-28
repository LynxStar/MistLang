using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Statements;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A lambda, expression-bodied (<c>x =&gt; x * 2</c>) or block-bodied (<c>x =&gt; { ... }</c>). Exactly
/// one of <see cref="Result"/> and <see cref="Body"/> is set, the two being different C# forms rather
/// than one form with a choice of spelling.
/// <para>
/// <see cref="Parameters"/> are ordinary locals, and the SAME instances the body's references resolve
/// to, which is what makes a captured variable's mutation visible to the enclosing scope: nothing here
/// copies.
/// </para>
/// </summary>
internal sealed class LambdaExpression(
    SyntaxNode node,
    IReadOnlyList<LocalVariable> parameters,
    Expression? result,
    Block? body,
    bool isStatic,
    IMistType type) : Expression(node)
{
    public IReadOnlyList<LocalVariable> Parameters { get; } = parameters;

    /// <summary>The body of an expression-bodied lambda, or null when block-bodied.</summary>
    public Expression? Result { get; } = result;

    /// <summary>The body of a block-bodied lambda, or null when expression-bodied.</summary>
    public Block? Body { get; } = body;

    /// <summary>
    /// Whether <c>static</c> was written, which forbids capture rather than describing the lambda.
    /// </summary>
    public bool IsStatic { get; } = isStatic;

    /// <summary>
    /// Whether <c>async</c> was written. Like the modifier on a method it transforms what the body
    /// produces into a task, and it makes the body an async context, so the two spellings of a wait
    /// mean inside it what they mean inside an async method.
    /// </summary>
    public bool IsAsync { get; init; }

    public bool IsPure { get; init; }

    public bool IsLocal { get; init; }

    public IMistType? UnionCarrierDelegateType { get; init; }

    public override IMistType Type { get; } = type;
}