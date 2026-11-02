using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class ClrCatchEmitter
{
    public static void Emit(InvocationExpression call, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var function = (IFunctionType)TypeNameEmitter.Closed(DelegateShape.Of(call.Arguments[0].Type)!);
        var completion = TaskResult.Of(call.Type) is null ? null : TaskResult.Of(function.ReturnType);
        var result = ResultTypes.Of(completion is null ? call.Type : TaskResult.Of(call.Type))!;
        var inputs = ClrCatchArguments.Prepare(call);
        var prepared = BodyEmitter.TemporaryName();
        var inputTypes = new List<IMistType> { function };
        var arguments = new List<string>();
        foreach(var input in inputs.Skip(1))
        {
            var name = $"{prepared}.Item{inputTypes.Count + 1}";
            if(input.Spread)
            {
                inputTypes.Add(input.Type);
                arguments.AddRange(Enumerable.Range(0, input.Width).Select(i => $"{name}.Item{i + 1}"));
            }
            else
            {
                inputTypes.Add(function.ParameterTypes[arguments.Count]);
                arguments.Add(name);
            }
        }

        var preparedType = inputTypes.Count == 1 ? TypeNameEmitter.Emit(function) : $"({string.Join(", ", inputTypes.Select(TypeNameEmitter.Emit))})";
        var signature = $"{preparedType}, {TypeNameEmitter.Emit(call.Type)}";
        var operation = inputTypes.Count == 1 ? prepared : $"{prepared}.Item1";
        var invocation = $"{operation}({string.Join(", ", arguments)})";
        var returnsVoid = OptionalUnion.IsVoid(completion ?? function.ReturnType);
        var resultName = TypeNameEmitter.Emit(result.Type);

        // Passing prepared values into the adapter leaves their evaluation outside its try, including for async adapters.
        writer.Append($"((System.Func<{signature}>)({(completion is null ? "" : "async ")}({prepared}) =>");
        writer.AppendLine();
        Adapter(writer, invocation, resultName, returnsVoid, completion is not null);

        writer.Append(inputs.Count == 1 ? "))(" : "))((");
        for(var i = 0; i < inputs.Count; i++)
        {
            if(i > 0)
            {
                writer.Append(", ");
            }

            BodyEmitter.EmitCoerced(inputs[i].Value, inputTypes[i], writer, diagnostics);
        }

        writer.Append(inputs.Count == 1 ? ")" : "))");
    }

    // Taking the intrinsic as a value closes its pack to the delegate's parameters, so the same boundary
    // is written over named parameters rather than a prepared tuple, and the delegate itself defers
    // evaluation of every input until it is invoked.
    public static string Reference(FunctionReferenceExpression reference)
    {
        var delegateType = (IFunctionType)TypeNameEmitter.Closed(reference.Type);
        var operationType = (IFunctionType)TypeNameEmitter.Closed(delegateType.ParameterTypes[0]);
        var completion = TaskResult.Of(delegateType.ReturnType) is null ? null : TaskResult.Of(operationType.ReturnType);
        var result = ResultTypes.Of(completion is null ? delegateType.ReturnType : TaskResult.Of(delegateType.ReturnType)!)!;
        var resultName = TypeNameEmitter.Emit(result.Type);
        var returnsVoid = OptionalUnion.IsVoid(completion ?? operationType.ReturnType);

        var operation = BodyEmitter.TemporaryName();
        var arguments = Enumerable.Range(0, delegateType.ParameterTypes.Count - 1).Select(_ => BodyEmitter.TemporaryName()).ToList();
        var invocation = $"{operation}({string.Join(", ", arguments)})";
        var parameters = string.Join(", ", new[] { operation }.Concat(arguments));

        var writer = new CSharpWriter();
        writer.Append($"(({TypeNameEmitter.Emit(reference.Type)})({(completion is null ? "" : "async ")}({parameters}) =>");
        writer.AppendLine();
        Adapter(writer, invocation, resultName, returnsVoid, completion is not null);
        writer.Append("))");

        return writer.ToString();
    }

    private static void Adapter(CSharpWriter writer, string invocation, string resultName, bool returnsVoid, bool awaits)
    {
        using(writer.Block())
        {
            writer.WriteLine("try");
            using(writer.Block())
            {
                var value = awaits ? $"await {invocation}" : invocation;
                if(returnsVoid)
                {
                    writer.WriteLine($"{value};");
                    writer.WriteLine($"return {resultName}.Success(Standard.Void.Instance);");
                }
                else
                {
                    writer.WriteLine($"return {resultName}.Success({value});");
                }
            }

            var error = BodyEmitter.TemporaryName();
            writer.WriteLine($"catch (System.Exception {error})");
            using(writer.Block())
            {
                writer.WriteLine($"return {resultName}.Failure({error});");
            }
        }
    }
}