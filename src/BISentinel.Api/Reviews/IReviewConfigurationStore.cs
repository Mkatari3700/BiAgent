namespace BISentinel.Api.Reviews;

public interface IReviewConfigurationStore
{
    Task<ReviewConfigurationSnapshot> GetAsync(CancellationToken cancellationToken);

    Task<ReviewConfigurationSnapshot?> UpdateAsync(
        ReviewConfiguration configuration,
        int expectedVersion,
        string changedBy,
        CancellationToken cancellationToken);
}
