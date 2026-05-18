using Namespaces.Models;
using Namespaces.Models.Game;
using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var player = new Player("Alice", 100);
        Console.WriteLine(player.Describe());
        var state = new GameState(1, true);
        Console.WriteLine(state.Summary());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
