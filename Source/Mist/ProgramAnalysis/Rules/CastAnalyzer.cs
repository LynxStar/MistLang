using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A cast with no conversion to perform. Mist admits a cast only to a type the value already is, or to
/// one a declared cast operator reaches -- the cast-operators spec, Requirement 8 -- so that no cast can
/// fail at runtime. That rules out every conversion C# would check when it ran: a downcast, a numeric
/// narrowing, a union narrowed to one constituent, a value typed <c>unknown</c> narrowed to anything.
/// Narrowing is what an <c>is</c> pattern is for.
/// </summary>
internal sealed class CastAnalyzer : IProgramAnalyzer
{
    public string Id => "total-conversion-classification";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            if(node is CastExpression cast
                && ConversionClassifier.Classify(cast.Operand.Type, cast.Type, context.Model.TypeInterner) == ConversionKind.None)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.CastHasNoConversion,
                    cast.Node,
                    [cast, cast.Operand],
                    TypeDisplay.Mist(cast.Operand.Type),
                    TypeDisplay.Mist(cast.Type));
            }
        }
    }
}