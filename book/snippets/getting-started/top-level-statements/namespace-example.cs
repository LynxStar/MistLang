using Standard;

using MyApp;

MyApp.Helper helper = new MyApp.Helper("world");
System.Console.WriteLine(helper.Greet());
return 0;

namespace MyApp
{
    public class Helper
    {
        private string target;

        public Helper(string target)
        {
            this.target = "";
            this.target = target;
        }

        public string Greet()
        {
            return $"Hello, {this.target}!";
        }
    }
}
