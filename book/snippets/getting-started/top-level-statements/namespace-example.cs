using MyApp;
using System;

System.Console.OutputEncoding = System.Text.Encoding.UTF8;
var helper = new Helper("world");
System.Console.WriteLine(helper.Greet());
return 0;

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
