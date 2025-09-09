using Baballonia;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Baballonia.Tests;

[TestClass]
[TestSubject(typeof(Utils))]
public class UtilsTest
{

    [TestMethod]
    public void TestFindSemVersionSuccess()
    {
        var version = Utils.FindVersionInString("1.2.3.4");
        Assert.IsNotNull(version);

        version = Utils.FindVersionInString("v1.2.3.4");
        Assert.IsNotNull(version);

        version = Utils.FindVersionInString("1.2.3.4rc");
        Assert.IsNotNull(version);

        version = Utils.FindVersionInString("v1.2.3.4rc");
        Assert.IsNotNull(version);

        version = Utils.FindVersionInString("random text v1.2.3.4rc more text");
        Assert.IsNotNull(version);
    }

    [TestMethod]
    public void TestFindSemVersionFail()
    {
        var version = Utils.FindVersionInString("1.2.3");
        Assert.IsNull(version);

        version = Utils.FindVersionInString("v1.2.3");
        Assert.IsNull(version);

        version = Utils.FindVersionInString("v1.2.3rc");
        Assert.IsNull(version);

        version = Utils.FindVersionInString("some random text");
        Assert.IsNull(version);

        version = Utils.FindVersionInString(null);
        Assert.IsNull(version);

        version = Utils.FindVersionInString("");
        Assert.IsNull(version);
    }
}
