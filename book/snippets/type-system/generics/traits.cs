using System;

namespace Snippet;

interface Comparable<T>
{
    int CompareTo(T other);
}
interface Converter<TIn, TOut>
{
    TOut Convert(TIn input);
}
