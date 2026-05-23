using Standard;
using System;

namespace AssociatedTypesDemo;

interface Container<TElement>
{
    TElement Get(int index);
}
