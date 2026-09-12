using Standard;

namespace Snippet;

internal interface Container<Element>
{
    Element Get(int index);
}
