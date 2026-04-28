namespace SimpleApp;

public class Dog : Animal, IGreeter
{
    private readonly string _trick;
    private readonly IGreeter? _buddy;

    public Dog(string name, string trick, IGreeter? buddy = null) : base(name)
    {
        _trick = trick;
        _buddy = buddy;
    }

    public override string Speak()
    {
        return Logger.Format(Name, "Woof!");
    }

    public string Greet(string other)
    {
        return Logger.Format(Name, $"hi {other}, watch me {_trick}");
    }
}
