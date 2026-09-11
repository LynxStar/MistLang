using System;

namespace Snippet;

class TopLevelService
{
    private int counter;

    public TopLevelService()
    {
        this.counter = 0;
    }

    public int GetCounter()
    {
        return counter;
    }

    private TopLevelService(Mist.Emission.CSharp.__DefaultInit marker)
    {
        this.counter = default(int);
    }

    public static TopLevelService __CreateDefault()
    {
        return new TopLevelService(default(Mist.Emission.CSharp.__DefaultInit));
    }
}
