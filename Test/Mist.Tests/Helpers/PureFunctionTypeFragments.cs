using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Tests.Helpers;

internal static class PureFunctionTypeFragments
{
    public static IReadOnlyList<Diagnostic> Analyze(string name)
    {
        var fragments = new DeclarationFragments();
        var model = fragments.Built;
        var integer = model.Type("Int32");
        var plain = model.Interner.GetOrCreateFunctionType([integer], integer);
        var promised = model.Interner.GetOrCreateFunctionType([integer], integer, isPure: true);

        switch(name)
        {
            case "cast-to-a-pure-function-type":
                var held = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("h", plain));
                var cast = new CastExpression(HandBuiltModel.Node(37), held, promised, [], promised);
                return model.Analyze(new CastAnalyzer(), new LocalDeclarationStatement(new LocalVariable("k", promised), cast));
            case "overloads-differing-only-in-purity":
                var twice = fragments.Class("Twice");
                _ = DeclarationFragments.Method(twice, "A", integer, promised);
                _ = DeclarationFragments.Method(twice, "A", integer, plain);
                return fragments.Run(new DeclarationShapeAnalyzer());
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }
    }
}