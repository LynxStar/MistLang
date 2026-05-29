using MyApp;
using System;

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

        protected Helper()
        {
            this.target = "";
        }

        public static Helper __CreateDefault()
        {
            var inst = new Helper();
            return inst;
        }
    }
}
