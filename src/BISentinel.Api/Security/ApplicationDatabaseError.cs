using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;

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

    public static string GetDetail(
        SqlException exception,
        IWebHostEnvironment environment,
        string developmentDetail)
    {
        if (!environment.IsDevelopment())
        {
            return "A database operation failed. Verify database connectivity, required database migrations/scripts, and application database permissions.";
        }

        if (exception.Number == AzureSqlFirewallErrorNumber)
        {
            return GetDetail(exception, developmentDetail);
        }

        return $"{developmentDetail} Database error: {exception.Message}";
    }
}
