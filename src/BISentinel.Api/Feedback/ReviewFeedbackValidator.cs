namespace BISentinel.Api.Feedback;

public static class ReviewFeedbackValidator
{
    public static Dictionary<string, string[]> Validate(SaveReviewFeedbackRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ReviewerId == Guid.Empty)
        {
            errors[nameof(request.ReviewerId)] = ["Reviewer ID must be a non-empty browser-generated identifier."];
        }

        if (request.ReviewFindingId == Guid.Empty)
        {
            errors[nameof(request.ReviewFindingId)] = ["A saved review finding ID is required."];
        }

        ValidateLength(request.Comment, 500, nameof(request.Comment), errors, allowNull: true);

        if (!string.IsNullOrWhiteSpace(request.Rating) &&
            request.Rating is not ("helpful" or "incorrect" or "not-applicable"))
        {
            errors[nameof(request.Rating)] = ["Rating must be helpful, incorrect, or not-applicable."];
        }

        if (string.IsNullOrWhiteSpace(request.Rating) && string.IsNullOrWhiteSpace(request.Comment))
        {
            errors[nameof(request.Rating)] = ["Provide a rating or a feedback note."];
        }

        return errors;
    }

    private static void ValidateLength(
        string? value,
        int maximumLength,
        string field,
        IDictionary<string, string[]> errors,
        bool allowNull = false)
    {
        if ((value is null && !allowNull) ||
            (value is not null && value.Length > maximumLength) ||
            (!allowNull && string.IsNullOrWhiteSpace(value)))
        {
            errors[field] = [$"{field} is required and must be no longer than {maximumLength} characters."];
        }
    }
}
