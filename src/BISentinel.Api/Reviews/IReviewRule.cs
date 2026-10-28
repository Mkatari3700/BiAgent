namespace BISentinel.Api.Reviews;

public interface IReviewRule
{
    string Key { get; }

    bool RequiresSemanticModel => true;

    ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options);
}
