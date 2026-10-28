using Microsoft.Data.SqlClient;

namespace BISentinel.Api.ReviewHistory;

public sealed class ReviewHistoryCleanupService(
    IReviewHistoryStore historyStore,
    ILogger<ReviewHistoryCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                var deleted = await historyStore.DeleteExpiredAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Removed {ExpiredReviewCount} expired review runs.", deleted);
                }
            }
            catch (SqlException exception)
            {
                logger.LogError(exception, "Scheduled review-history cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
