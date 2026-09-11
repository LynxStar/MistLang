using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A function declared as a statement. <see cref="Function"/> is a <see cref="Method"/> with no
/// declaring type, so that a call to it binds, is judged and is promoted exactly as a call to a
/// method is; <see cref="Parameters"/> are the body scope's own locals for its parameters, index-aligned
/// with the method's, because the flow graph introduces and the emitter renames a
/// <see cref="LocalVariable"/> and a <see cref="Parameter"/> is not one.
/// </summary>
internal sealed class LocalFunctionStatement(Method function, IReadOnlyList<LocalVariable> parameters) : Statement
{
    public Method Function { get; } = function;

    public IReadOnlyList<LocalVariable> Parameters { get; } = parameters;

    public Block Body => Function.Body!;
}