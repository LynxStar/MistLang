using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class ConstructorInitializers
{
    // Each application completes its assignment before the next value is evaluated, with values passed
    // as arguments so a ref local or parameter needs no closure capture.
    public static string Apply(NewExpression allocation, Func<Expression, string> render)
    {
        var constructed = new NewExpression(allocation.Node, allocation.Type, allocation.Arguments,
            allocation.Constructor, [], allocation.ParameterTypes)
        { ContextTypes = allocation.ContextTypes };
        var result = render(constructed);
        var product = TypeNameEmitter.Emit(allocation.Type);

        for(var index = 0; index < allocation.Initializer.Count; index++)
        {
            var entry = allocation.Initializer[index];
            var target = $"__constructed{index}";
            var value = $"__initialized{index}";
            var valueType = TypeNameEmitter.Emit(entry.TargetType ?? entry.Value.Type);
            var assignment = entry.MemberName is { } member ? $"{target}.{member} = {value}" : $"{target}.Add({value})";
            result = $"((System.Func<{product}, {valueType}, {product}>)(({target}, {value}) => {{ {assignment}; return {target}; }}))({result}, {render(entry.Value)})";
        }

        return result;
    }
}