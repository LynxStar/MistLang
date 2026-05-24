using System;

namespace Standard;

interface IEnumerable<TElement, TEnumerator>
    where TEnumerator : System.Collections.Generic.IEnumerator<TElement>
{
    TEnumerator GetEnumerator();
}
