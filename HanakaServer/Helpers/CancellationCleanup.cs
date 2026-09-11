using Microsoft.EntityFrameworkCore.Storage;

namespace HanakaServer.Helpers;

public static class CancellationCleanup
{
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PostCommitTimeout = TimeSpan.FromSeconds(15);

    public static CancellationTokenSource CreatePostCommitTokenSource() =>
        new(PostCommitTimeout);

    /// <summary>
    /// Rollback must not reuse RequestAborted: that token is already cancelled
    /// and would skip cleanup. A short server-owned timeout keeps cleanup bounded.
    /// </summary>
    public static async Task TryRollbackAsync(
        IDbContextTransaction? transaction,
        ILogger? logger,
        string operation)
    {
        if (transaction == null)
        {
            return;
        }

        using var cleanup = new CancellationTokenSource(RollbackTimeout);
        try
        {
            await transaction.RollbackAsync(cleanup.Token);
        }
        catch (Exception rollbackException)
        {
            logger?.LogWarning(
                rollbackException,
                "Transaction rollback cleanup failed or timed out for {Operation}.",
                operation);
        }
    }
}
