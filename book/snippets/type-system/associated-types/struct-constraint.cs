using Standard;
using System;

namespace AssociatedTypesDemo;

interface IIterator<TCursor>
    where TCursor : struct
{
    TCursor CreateCursor(int pos);
}
