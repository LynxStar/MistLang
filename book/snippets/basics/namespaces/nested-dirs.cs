using System;

namespace Snippet;

public class Player
{
    private string name;

    private int health;

    public Player(string name, int health)
    {
        this.name = name;
        this.health = health;
    }

    public string Describe()
    {
        return $"Player: {name}, HP: {health}";
    }

    protected Player()
    {
        this.name = "";
        this.health = 0;
    }

    public static Player __CreateDefault()
    {
        var inst = new Player();
        return inst;
    }
}
