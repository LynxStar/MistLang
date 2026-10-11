using Standard;

namespace Snippet;

public class Player
{
    private string name;
    private int health;

    public Player(string name, int health)
    {
        this.name = "";
        this.health = default(int);
        this.name = name;
        this.health = health;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Player: {name}, HP: {health}");
    }
}
