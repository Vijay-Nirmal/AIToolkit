using System.Text;

namespace AIToolkit.Tools.Document.Word.Tests;

[TestClass]
public class UploadOnDisposeMemoryStreamTests
{
    [TestMethod]
    [Timeout(5000)]
    public async Task SyncDisposeDoesNotBlockAndAsyncDisposeAwaitsExistingPersist()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistCount = 0;
        byte[]? persistedBytes = null;

        var stream = new UploadOnDisposeMemoryStream(
            async (content, cancellationToken) =>
            {
                Interlocked.Increment(ref persistCount);
                started.TrySetResult();

                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                persistedBytes = buffer.ToArray();

                await allowCompletion.Task.ConfigureAwait(false);
                completed.TrySetResult();
            },
            CancellationToken.None);

        var payload = Encoding.UTF8.GetBytes("uploaded through async persistence");
        await stream.WriteAsync(payload).ConfigureAwait(false);

        var syncDisposeTask = Task.Run(() => stream.Dispose());

        await started.Task.ConfigureAwait(false);
        var completedBeforeAllow = await Task.WhenAny(syncDisposeTask, Task.Delay(250)).ConfigureAwait(false);
        Assert.AreSame(syncDisposeTask, completedBeforeAllow, "Synchronous Dispose should not block on asynchronous persistence.");
        Assert.AreEqual(1, persistCount, "Persistence should start exactly once.");
        Assert.AreEqual("uploaded through async persistence", Encoding.UTF8.GetString(persistedBytes!));

        var asyncDisposeTask = stream.DisposeAsync().AsTask();
        Assert.IsFalse(asyncDisposeTask.IsCompleted, "DisposeAsync should await the in-flight persistence work.");

        allowCompletion.TrySetResult();

        await asyncDisposeTask.ConfigureAwait(false);
        await completed.Task.ConfigureAwait(false);
        Assert.AreEqual(1, persistCount, "DisposeAsync should reuse the existing persistence task rather than persisting twice.");
    }
}