using System;

namespace Snippet;

public interface Container<TElement>
{
    TElement Get(int index);
}
