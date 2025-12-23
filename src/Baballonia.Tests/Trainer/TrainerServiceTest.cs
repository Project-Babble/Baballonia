using System;
using System.Threading.Tasks;
using Baballonia.Desktop.Trainer;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Baballonia.Tests.Trainer;

[TestClass]
[TestSubject(typeof(TrainerService))]
public class TrainerServiceTest
{
    [TestMethod]
    public async Task Test()
    {
        var factory = LoggerFactory.Create(builder => builder.AddConsole().AddDebug());
        var log = factory.CreateLogger<TrainerService>();
        TrainerService service = new TrainerService(log);

        service.RunTraining("test.bin", "model.onnx");

        await service.WaitAsync();
    }
}
