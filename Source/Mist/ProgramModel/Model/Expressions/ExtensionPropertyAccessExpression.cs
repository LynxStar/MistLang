using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// Reading an extension property: <c>receiver.Name</c> where Name is declared on an extension block
/// rather than on the receiver's type.
/// <para>
/// Its own node rather than a <see cref="MemberAccessExpression"/> because the two do not EMIT
/// alike. C# has no extension property, so this is not an access at all in the output -- it becomes
/// a call to the carrier's generated getter with the receiver as its argument, and a node that
/// emitted as written would produce a member C# cannot find.
/// </para>
/// <para>
/// The carrier is named rather than resolved through C#'s own extension lookup, which the METHOD
/// case relies on. That option does not exist here: <c>get_Name</c> is a name the user never wrote
/// and dot-notation on it would not compile.
/// </para>
/// </summary>
internal sealed class ExtensionPropertyAccessExpression(
    SyntaxNode node,
    Expression receiver,
    Property property,
    string carrierName,
    IMistType type) : Expression(node)
{
    public Expression Receiver { get; } = receiver;

    public Property Property { get; } = property;

    /// <summary>
    /// The static class the block emitted as. Carried rather than derived, because the block that
    /// declared the property is the only thing that knows it and the emitter has no route back.
    /// </summary>
    public string CarrierName { get; } = carrierName;

    public override IMistType Type { get; } = type;
}