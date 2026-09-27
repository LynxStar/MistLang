using Standard;

namespace Snippet;

public class GameState
{
    private int level;
    private bool active;

    public GameState(int level, bool active)
    {
        this.level = level;
        this.active = active;
    }

    public string Summary()
    {
        return System.FormattableString.Invariant($"Level: {level}, Active: {active}");
    }
}
