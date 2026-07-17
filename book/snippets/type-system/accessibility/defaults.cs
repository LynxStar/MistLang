using System;
using System.Text.RegularExpressions;

namespace Snippet;

internal class TopLevelService
{
    int counter;

    public TopLevelService()
    {
        this.counter = 0;
    }

    public int GetCounter()
    {
        return counter;
    }
}
