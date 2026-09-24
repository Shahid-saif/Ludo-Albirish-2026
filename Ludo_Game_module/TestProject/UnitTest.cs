using HelloWorld;

namespace TestProject;

public class Tests
{
    [Test]
    public void TestHelloWorld()
    {
        var module = new MyModule(null, null);
        var helloAnon = module.Hello("Anon");
        Assert.That(helloAnon, Is.Not.Null);
        Assert.That(helloAnon, Is.EqualTo("Hello, Anon!"));
    }
}
