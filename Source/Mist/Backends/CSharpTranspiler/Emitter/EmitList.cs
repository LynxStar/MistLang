namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Each element is emitted through a callback rather than returned as a string, because emitting
/// one is a sequence of writer operations and so cannot be composed with
/// <see cref="string.Join(string, IEnumerable{string})"/>.
/// </summary>
internal static class EmitList
{
    public static void Separated<T>(
        IEnumerable<T> items,
        string separator,
        CSharpWriter writer,
        Action<T> emitItem)
    {
        var first = true;
        foreach(var item in items)
        {
            if(!first)
            {
                writer.Append(separator);
            }

            first = false;
            emitItem(item);
        }
    }
}