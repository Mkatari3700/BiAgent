using System.Security.Cryptography;
using System.Text;

namespace BISentinel.Api.ReviewHistory;

public static class ReviewFindingIdentity
{
    public static byte[] CreateFingerprint(string projectId, string ruleId, string location) =>
        SHA256.HashData(Encoding.Unicode.GetBytes($"{projectId}\n{ruleId}\n{location}"));
}
