using System;

namespace Snippet;

interface Container<TElement>
{
    TElement Get(int index);
}
