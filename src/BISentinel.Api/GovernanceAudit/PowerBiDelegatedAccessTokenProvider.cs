using System.Security.Claims;
using Microsoft.Identity.Web;

namespace BISentinel.Api.GovernanceAudit;

public interface IPowerBiDelegatedAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(ClaimsPrincipal user);
}

public sealed class PowerBiDelegatedAccessTokenProvider(
    ITokenAcquisition tokenAcquisition) : IPowerBiDelegatedAccessTokenProvider
{
    public static IReadOnlyList<string> RequiredScopes { get; } = Array.AsReadOnly(
    [
        "https://analysis.windows.net/powerbi/api/Workspace.Read.All",
        "https://analysis.windows.net/powerbi/api/Report.Read.All",
        "https://analysis.windows.net/powerbi/api/Dataset.Read.All"
    ]);

    public async Task<string> GetAccessTokenAsync(ClaimsPrincipal user)
    {
        try
        {
            return await tokenAcquisition.GetAccessTokenForUserAsync(RequiredScopes, user: user);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            throw new PowerBiDelegatedConsentRequiredException(exception);
        }
    }
}

public sealed class PowerBiDelegatedConsentRequiredException(Exception innerException)
    : Exception("Power BI delegated consent is required.", innerException)
{
}
