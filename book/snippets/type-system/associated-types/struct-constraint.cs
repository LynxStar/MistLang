using System;

namespace Snippet;

interface IIterator<TCursor>
    where TCursor : struct
{
    TCursor CreateCursor(int pos);
}
