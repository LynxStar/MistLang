using System;

// Top-level statements (no namespace wrapping)
var helper = new MyApp.Helper("world");
Console.WriteLine(helper.Greet());

return 0;

// Type declarations in their namespace
namespace MyApp
{
    public class Helper
    {
        private string target;

        public Helper(string target)
        {
            this.target = target;
        }

        public string Greet()
        {
            return $"Hello, {this.target}!";
        }
    }
}
