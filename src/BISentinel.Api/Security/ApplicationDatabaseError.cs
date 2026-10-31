using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Security;

public static partial class ApplicationDatabaseError
{
    private const int AzureSqlFirewallErrorNumber = 40615;

    [GeneratedRegex(@"Client with IP address '([^']+)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClientIpAddressPattern();

    public static string GetDetail(SqlException exception, string fallbackDetail)
    {
        if (exception.Number != AzureSqlFirewallErrorNumber)
        {
            return fallbackDetail;
        }

        var match = ClientIpAddressPattern().Match(exception.Message);
        var address = match.Success ? $" ({match.Groups[1].Value})" : string.Empty;
        return $"Azure SQL blocked the application host's client IP{address}. In the Azure portal, open the SQL server's Networking page and add the application host's outbound IP under Firewall rules. If the app is deployed, allow its outbound IP rather than your workstation IP. Save the rule and retry after it takes effect.";
    }
}
