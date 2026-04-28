namespace SimpleApp;

public abstract class Animal
{
    protected string Name;

    protected Animal(string name)
    {
        Name = name;
    }

    public abstract string Speak();
}
