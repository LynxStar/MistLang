using System;

namespace Snippet;

internal interface Container<Element>
{
    Element Get(int index);
}
