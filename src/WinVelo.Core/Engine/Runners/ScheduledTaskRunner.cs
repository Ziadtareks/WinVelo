using System;
using WinVelo.Core.Interfaces;

namespace WinVelo.Core.Engine.Runners;

public sealed class ScheduledTaskRunner
{
    private readonly ITaskSchedulerService _taskService;

    public ScheduledTaskRunner(ITaskSchedulerService taskService)
    {
        _taskService = taskService;
    }

    public bool TaskExists(string path, string taskName) => _taskService.TaskExists(path, taskName);
    public bool IsTaskEnabled(string path, string taskName) => _taskService.IsTaskEnabled(path, taskName);

    public bool TryEnableTask(string path, string taskName, out string? errorMessage)
    {
        try
        {
            _taskService.EnableTask(path, taskName);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryDisableTask(string path, string taskName, out string? errorMessage)
    {
        try
        {
            _taskService.DisableTask(path, taskName);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
