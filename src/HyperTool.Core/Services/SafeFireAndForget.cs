using System.Collections.Concurrent;

namespace HyperTool.Services;

public static class SafeFireAndForget
{
    private static readonly ConcurrentDictionary<string, byte> RunningTasks = new(StringComparer.Ordinal);

    public static void Run(Task task, Action<Exception>? onError = null, string operation = "background")
    {
        ArgumentNullException.ThrowIfNull(task);

        var operationId = $"{operation}:{Guid.NewGuid():N}";
        RunningTasks.TryAdd(operationId, 0);

        _ = task.ContinueWith(
            completedTask =>
            {
                RunningTasks.TryRemove(operationId, out _);

                if (completedTask.IsCanceled)
                {
                    return;
                }

                if (completedTask.Exception is { } aggregate)
                {
                    var flattened = aggregate.Flatten();
                    foreach (var inner in flattened.InnerExceptions)
                    {
                        onError?.Invoke(inner);
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static int RunningCount => RunningTasks.Count;
}
