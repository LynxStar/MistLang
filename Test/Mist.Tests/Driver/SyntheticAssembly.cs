using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Mist.Tests.Driver;

/// <summary>
/// Writes a one-class assembly with <see cref="MetadataBuilder"/>, so a test has a real compiled
/// reference without running MSBuild or Roslyn: a public class with a public parameterless constructor
/// and <c>public const int Answer = 42</c>, and, when <c>uses</c> names a type of another assembly,
/// <c>static void Use(T)</c> and <c>static T Make()</c> over it.
/// </summary>
internal static class SyntheticAssembly
{
    private static readonly byte[] FrameworkPublicKeyToken = [0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a];

    public static string Write(
        string directory, string assemblyName, string ns, string typeName, (string Assembly, string Namespace, string Name)? uses = null)
    {
        var metadata = new MetadataBuilder();
        var ilStream = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(ilStream);

        _ = metadata.AddModule(
            0, metadata.GetOrAddString(assemblyName + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        _ = metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName), new Version(1, 0, 0, 0), default, default, default, AssemblyHashAlgorithm.Sha1);

        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0), default,
            metadata.GetOrAddBlob(FrameworkPublicKeyToken), default, default);
        var objectType = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));

        var parameterlessSignature = new BlobBuilder();
        new BlobEncoder(parameterlessSignature).MethodSignature(isInstanceMethod: true).Parameters(0, returnType => returnType.Void(), _ => { });
        var objectConstructor = metadata.AddMemberReference(
            objectType, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(parameterlessSignature));

        var answerSignature = new BlobBuilder();
        new BlobEncoder(answerSignature).Field().Type().Int32();
        var answer = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
            metadata.GetOrAddString("Answer"), metadata.GetOrAddBlob(answerSignature));
        _ = metadata.AddConstant(answer, 42);

        var constructor = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(parameterlessSignature),
            Body(bodies, il =>
            {
                il.LoadArgument(0);
                il.Call(objectConstructor);
                il.OpCode(ILOpCode.Ret);
            }),
            MetadataTokens.ParameterHandle(1));

        if(uses is { } used)
        {
            var usedAssembly = metadata.AddAssemblyReference(
                metadata.GetOrAddString(used.Assembly), new Version(1, 0, 0, 0), default, default, default, default);
            var usedType = metadata.AddTypeReference(usedAssembly, metadata.GetOrAddString(used.Namespace), metadata.GetOrAddString(used.Name));

            var useSignature = new BlobBuilder();
            new BlobEncoder(useSignature).MethodSignature().Parameters(
                1, returnType => returnType.Void(), parameters => parameters.AddParameter().Type().Type(usedType, isValueType: false));
            var parameter = metadata.AddParameter(ParameterAttributes.None, metadata.GetOrAddString("value"), 1);
            _ = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, metadata.GetOrAddString("Use"), metadata.GetOrAddBlob(useSignature),
                Body(bodies, il => il.OpCode(ILOpCode.Ret)), parameter);

            var makeSignature = new BlobBuilder();
            new BlobEncoder(makeSignature).MethodSignature().Parameters(
                0, returnType => returnType.Type().Type(usedType, isValueType: false), _ => { });
            _ = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, metadata.GetOrAddString("Make"), metadata.GetOrAddBlob(makeSignature),
                Body(bodies, il =>
                {
                    il.OpCode(ILOpCode.Ldnull);
                    il.OpCode(ILOpCode.Ret);
                }),
                MetadataTokens.ParameterHandle(2));
        }

        _ = metadata.AddTypeDefinition(
            default, default, metadata.GetOrAddString("<Module>"), default, answer, constructor);
        _ = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            metadata.GetOrAddString(ns), metadata.GetOrAddString(typeName), objectType, answer, constructor);

        var image = new BlobBuilder();
        _ = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll), new MetadataRootBuilder(metadata), ilStream)
            .Serialize(image);

        _ = Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assemblyName + ".dll");
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    private static int Body(MethodBodyStreamEncoder bodies, Action<InstructionEncoder> emit)
    {
        var il = new InstructionEncoder(new BlobBuilder());
        emit(il);
        return bodies.AddMethodBody(il);
    }
}