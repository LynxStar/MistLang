using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The length slot of an array a method returns, closed with one of that method's <c>readonly</c> value
/// parameters (runtime-dependent-values Requirement 8 items 7 to 10). Inside the body it is the value the
/// caller passed, which nothing can write; a call puts a capture of the argument in its place, so it never
/// reaches a caller.
/// </summary>
internal sealed class ParameterValue : MistType
{
    public ParameterValue(Parameter parameter, IMistType domain, Storm.Runtime.SyntaxNode written)
        : base($"{parameter.Name}@parameter", new Scope(null), declaringType: null)
    {
        Parameter = parameter;
        Domain = domain;
        Written = written;
    }

    public Parameter Parameter { get; }

    public IMistType Domain { get; }

    public Storm.Runtime.SyntaxNode Written { get; }
}