namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// The justification every trim suppression in the backend cites. The reflection it covers runs over
/// types from a MetadataLoadContext, which reads referenced assemblies as metadata; trimming the
/// compiler's own binary cannot remove anything from them, so the trimmer's warning does not apply.
/// </summary>
internal static class MetadataOnlyReflection
{
    public const string Justification =
        "Reflects over MetadataLoadContext types read from referenced assemblies; trimming the compiler cannot affect them.";
}