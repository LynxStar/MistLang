using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// The value a dependent slot was closed with where a type is written in a body: a readonly local no
/// scope declares, typed by the slot's contract and initialized by the argument as bound. A local
/// rather than a kind of its own, so that the flow declares it, proves its initializer against its
/// type and lets a lambda capture it exactly as it does any other; <see cref="Declaration"/> is the
/// statement those rules read, and no block holds it.
/// </summary>
internal sealed class SlotCapture : LocalVariable
{
    public SlotCapture(string writtenName, IMistType contract, IMistType domain, Expression initializer, int position)
        : base(writtenName, contract)
    {
        IsReadonly = true;
        Initializer = initializer;
        Declaration = new LocalDeclarationStatement(this, initializer);
        Value = new CapturedValue(this, domain, position);
    }

    public Expression Initializer { get; }

    public LocalDeclarationStatement Declaration { get; }

    public CapturedValue Value { get; }

    public static IReadOnlyList<SlotCapture> Of(object node)
    {
        return node switch
        {
            LocalDeclarationStatement declaration => declaration.Captures,
            NewExpression creation => creation.Captures,
            ArrayCreationExpression array => array.Captures,
            StackallocExpression stack => stack.Captures,
            TypeTestPattern test => test.Captures,
            InvocationExpression invocation => invocation.Captures,
            MemberAccessExpression access => access.Captures,
            CastExpression cast => cast.Captures,
            AsExpression conversion => conversion.Captures,
            _ => [],
        };
    }
}