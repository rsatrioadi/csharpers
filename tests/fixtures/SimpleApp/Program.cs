namespace SimpleApp;

public static class Program
{
    public static void Main()
    {
        var dog = new Dog("Rex", "fetch");
        var sound = dog.Speak();
        var hello = dog.Greet("Alice");
        System.Console.WriteLine(sound);
        System.Console.WriteLine(hello);
    }
}
