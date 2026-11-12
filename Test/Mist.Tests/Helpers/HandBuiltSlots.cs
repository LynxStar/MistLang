using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class HandBuiltSlots
{
    private int position;

    public HandBuiltRefinements Refinements { get; } = new();

    public IMistType Int => Refinements.Int;

    public RefinedType ZeroOrPositive { get; }

    public ClassType Array { get; }

    public HandBuiltSlots()
    {
        ZeroOrPositive = Refinements.Refine(Int, ComparisonKind.GreaterThanOrEqual, 0L, "ZeroOrPositiveInt");
        Array = (ClassType)((ConstructedType)Refinements.Sequence()).Definition;
    }

    public static MemberType Generic(MemberType definition, string slot, IMistType domain)
    {
        definition.TypeParametersList.Add(new TypeParameter(slot, 0, definition.Scope, definition) { IsDependent = true, Domain = (MistType)domain });
        return definition;
    }

    public TypeParameter Dependent(string name)
    {
        return new TypeParameter(name, 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)Int };
    }

    public SlotCapture Capture(LocalVariable read, IMistType? contract = null)
    {
        position++;
        return new SlotCapture(read.Name, contract ?? ZeroOrPositive, Int, HandBuiltRefinements.Read(read, position), position);
    }

    public LiftedType Literal(long value)
    {
        return Refinements.Built.Interner.LiftLiteral(Int, value);
    }

    public ConstructedType Closed(IMistType definition, params IMistType[] arguments)
    {
        return Refinements.Built.Interner.GetOrCreateConstructedType(definition, arguments);
    }

    public ConstructedType ArrayOf(IMistType length)
    {
        return Closed(Array, Int, length);
    }

    public ArrayCreationExpression Create(SlotCapture length)
    {
        return new ArrayCreationExpression(HandBuiltModel.Node(), Int, length.Initializer, ArrayOf(length.Value)) { Captures = [length] };
    }

    public static LocalDeclarationStatement Declare(LocalVariable local, Expression value, params SlotCapture[] captures)
    {
        return new LocalDeclarationStatement(local, value) { Captures = captures };
    }
}