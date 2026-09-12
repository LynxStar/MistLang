using Namespaces.Models;
using Namespaces.Models.Game;
using Standard;

namespace Namespaces;

public class Program
{
    public static void Main(string[] args)
    {
        Namespaces.Models.Player player = new Namespaces.Models.Player("Alice", 100);
        System.Console.WriteLine(player.Describe());
        Namespaces.Models.Game.GameState state = new Namespaces.Models.Game.GameState(1, true);
        System.Console.WriteLine(state.Summary());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
