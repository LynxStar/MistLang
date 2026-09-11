using System;

namespace Snippet;

interface Container<Element>
{
    Element Get(int index);
}
