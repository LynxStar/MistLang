using System;

namespace Namespaces.Models.Game;

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
        return $"Level: {level}, Active: {active}";
    }

    protected GameState()
    {
        this.level = 0;
        this.active = false;
    }

    public static GameState __CreateDefault()
    {
        var inst = new GameState();
        return inst;
    }
}
