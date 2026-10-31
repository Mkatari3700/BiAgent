using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Security;

public static class ApplicationDatabaseError
{
    private const int AzureSqlFirewallErrorNumber = 40615;

    public static string GetDetail(SqlException exception, string fallbackDetail)
    {
        if (exception.Number != AzureSqlFirewallErrorNumber)
        {
            return fallbackDetail;
        }

        return "The application database is blocked by its network firewall. Ask your BI Sentinel administrator to verify the app host's database connectivity.";
    }
}
