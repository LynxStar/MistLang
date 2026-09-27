using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int day = 3;
        string dayName = day switch { 1 => "Monday", 2 => "Tuesday", 3 => "Wednesday", 4 => "Thursday", 5 => "Friday", var _ => "Weekend" };
        System.Console.WriteLine(System.FormattableString.Invariant($"Day {day} is {dayName}"));
        string command = "quit";
        string response = command switch { "start" => "Starting...", "stop" => "Stopping...", "quit" => "Goodbye!", var _ => "Unknown command" };
        System.Console.WriteLine($"Command '{command}' => {response}");
        bool isActive = true;
        string status = isActive switch { true => "Active", false => "Inactive" };
        System.Console.WriteLine($"isActive={isActive} => {status}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
