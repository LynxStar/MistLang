namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a sequence with a separator between its elements -- the inverse of a split.
/// <para>
/// Only the separator is text. Each element is emitted through a callback rather than
/// returned as a string, because emitting one is a sequence of writer operations and so
/// cannot be composed with <see cref="string.Join(string, IEnumerable{string})"/> the way
/// a parameter list can.
/// </para>
/// </summary>
internal static class EmitList
{
    /// <summary>
    /// Emits every item in order, writing <paramref name="separator"/> between consecutive
    /// items only -- never before the first and never after the last. An empty sequence
    /// emits nothing at all, not even the separator.
    /// </summary>
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