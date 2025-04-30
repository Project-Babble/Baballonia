using System;

namespace Baballonia.Contracts;

// Simple interface to allow for easy mocking of the DispatcherService from the Core project
// allowing us to invoke actions on the UI thread from the Core project.
public interface IDispatcherService
{
    public void Run(Action action);
}
