using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tweakora.Core.Operations;

public enum OperationItemStatus
{
    Succeeded,
    Skipped,
    Failed
}

public sealed class OperationItemResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required OperationItemStatus Status { get; init; }
    public string? Reason { get; init; }
}

public sealed class OperationExecutionReport
{
    public required string Title { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required List<OperationItemResult> Results { get; init; }
    public int SucceededCount => Results.Count(r => r.Status == OperationItemStatus.Succeeded);
    public int SkippedCount => Results.Count(r => r.Status == OperationItemStatus.Skipped);
    public int FailedCount => Results.Count(r => r.Status == OperationItemStatus.Failed);
    public string Summary => $"{SucceededCount} succeeded, {SkippedCount} skipped, {FailedCount} failed";
}

public interface IOperationRunner
{
    event Action<string>? OnLog;
    event Action<string, int>? OnProgress;
    Task<OperationExecutionReport> RunAsync<T>(
        string operationTitle,
        IReadOnlyList<T> items,
        Func<T, CancellationToken, Task<OperationItemResult>> executeItem,
        CancellationToken cancellationToken = default);
}

public sealed class OperationRunner : IOperationRunner
{
    public event Action<string>? OnLog;
    public event Action<string, int>? OnProgress;

    public async Task<OperationExecutionReport> RunAsync<T>(
        string operationTitle,
        IReadOnlyList<T> items,
        Func<T, CancellationToken, Task<OperationItemResult>> executeItem,
        CancellationToken cancellationToken = default)
    {
        DateTime start = DateTime.UtcNow;
        List<OperationItemResult> results = new();

        OnLog?.Invoke($"[START] {operationTitle} ({items.Count} items)");
        OnProgress?.Invoke("Starting...", 0);

        for (int i = 0; i < items.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                for (int j = i; j < items.Count; j++)
                {
                    results.Add(new OperationItemResult
                    {
                        Id = $"item-{j}",
                        Name = items[j]?.ToString() ?? $"Item {j}",
                        Status = OperationItemStatus.Skipped,
                        Reason = "Operation cancelled by user."
                    });
                }
                OnLog?.Invoke("[CANCEL] Operation cancelled by user.");
                break;
            }

            T item = items[i];
            int percent = (int)(((double)(i + 1) / items.Count) * 100);

            try
            {
                var itemResult = await executeItem(item, cancellationToken);
                results.Add(itemResult);

                string statusTag = itemResult.Status switch
                {
                    OperationItemStatus.Succeeded => "PASS",
                    OperationItemStatus.Skipped => "SKIP",
                    OperationItemStatus.Failed => "FAIL",
                    _ => "INFO"
                };

                string msg = $"[{statusTag}] {itemResult.Name}" + (!string.IsNullOrWhiteSpace(itemResult.Reason) ? $": {itemResult.Reason}" : "");
                OnLog?.Invoke(msg);
            }
            catch (Exception ex)
            {
                var errResult = new OperationItemResult
                {
                    Id = $"item-{i}",
                    Name = item?.ToString() ?? $"Item {i}",
                    Status = OperationItemStatus.Failed,
                    Reason = ex.Message
                };
                results.Add(errResult);
                OnLog?.Invoke($"[FAIL] {errResult.Name}: {ex.Message}");
            }

            OnProgress?.Invoke($"Processed {i + 1}/{items.Count}", percent);
        }

        DateTime end = DateTime.UtcNow;
        var report = new OperationExecutionReport
        {
            Title = operationTitle,
            StartTime = start,
            EndTime = end,
            Results = results
        };

        OnProgress?.Invoke(report.Summary, 100);
        OnLog?.Invoke($"[DONE] {operationTitle}: {report.Summary}");

        return report;
    }
}
