using Standard;

namespace Snippet;

internal class TopLevelService
{
    private int counter;

    public TopLevelService()
    {
        this.counter = default(int);
        this.counter = 0;
    }

    public int GetCounter()
    {
        return counter;
    }
}
