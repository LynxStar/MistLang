using System.Text;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class InterpolationEmitter
{
    public static string Emit(InterpolatedStringExpression interpolated, Func<Expression, string> render)
    {
        var holes = interpolated.Parts.OfType<InterpolatedStringHole>().ToDictionary(hole => hole, hole => Hole(hole, render));
        var provided = holes.Values.Any(hole => InvariantCulture.Provides(hole.Type));
        var text = new StringBuilder("$\"");

        foreach(var part in interpolated.Parts)
        {
            switch(part)
            {
                case InterpolatedStringText literal:
                    _ = text.Append(literal.Text);
                    break;

                case InterpolatedStringHole hole:
                    var (value, type) = holes[hole];
                    _ = text.Append('{').Append(InvariantCulture.Hole(value, type, provided)).Append('}');
                    break;
            }
        }

        _ = text.Append('"');

        return provided ? InvariantCulture.Provided(text.ToString()) : text.ToString();
    }

    private static (string Value, IMistType Type) Hole(InterpolatedStringHole hole, Func<Expression, string> render)
    {
        var type = TypeNameEmitter.Closed(hole.Expression.Type);
        var value = render(hole.Expression);

        // An optional's absence lowers to null, which interpolation prints
        // as the empty string; Mist prints it as the unit value's name.
        return OptionalUnion.InnerType(type) is { } inner
            ? ($"(object?){value} ?? \"Void\"", inner)
            : (value, type);
    }
}