using BISentinel.Api.Feedback;

namespace BISentinel.Api.Tests;

public sealed class ReviewSubmissionTests
{
    [Fact]
    public void SubmitValidator_AcceptsUniqueFindingsAndOptionalJustifications()
    {
        var request = new SubmitReviewRequest(
        [
            new SubmitReviewFindingRequest(Guid.NewGuid(), "Business owner is documented."),
            new SubmitReviewFindingRequest(Guid.NewGuid(), null)
        ]);

        Assert.Empty(ReviewSubmissionValidator.Validate(request));
    }

    [Fact]
    public void SubmitValidator_RejectsEmptyOrDuplicateFindingIds()
    {
        var findingId = Guid.NewGuid();
        var request = new SubmitReviewRequest(
        [
            new SubmitReviewFindingRequest(Guid.Empty, null),
            new SubmitReviewFindingRequest(findingId, null),
            new SubmitReviewFindingRequest(findingId, null)
        ]);

        Assert.Contains(nameof(request.Findings), ReviewSubmissionValidator.Validate(request).Keys);
    }

    [Fact]
    public void ReviewerValidator_RequiresValidUniqueDecisionForEveryFinding()
    {
        var request = new CompleteReviewSubmissionRequest(
        [
            new ReviewSubmissionFindingRequest(Guid.NewGuid(), "Accepted", null),
            new ReviewSubmissionFindingRequest(Guid.NewGuid(), "NeedsChanges", "Document the owner.")
        ]);

        Assert.Empty(ReviewSubmissionValidator.Validate(request));
    }

    [Fact]
    public void ReviewerValidator_RejectsUnsupportedDisposition()
    {
        var request = new CompleteReviewSubmissionRequest(
        [
            new ReviewSubmissionFindingRequest(Guid.NewGuid(), "Maybe", null)
        ]);

        Assert.Contains(nameof(request.Findings), ReviewSubmissionValidator.Validate(request).Keys);
    }
}
