using System.Security.Claims;
using BISentinel.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BISentinel.Api.Tests;

public sealed class ApplicationUserAccessMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsApplicationAdminRoleFromStoredAssignment()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var bootstrapId = Guid.NewGuid();
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            tenantId,
            objectId,
            "BI Admin",
            "admin@example.com",
            true,
            true,
            false,
            DateTimeOffset.UtcNow);
        var store = new StubApplicationUserStore(user);
        var context = CreateAuthenticatedContext(tenantId, objectId);
        var nextInvoked = false;
        var middleware = new ApplicationUserAccessMiddleware(
            _ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            },
            store,
            new ApplicationAuthenticationSettings(tenantId, bootstrapId),
            NullLogger<ApplicationUserAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(nextInvoked);
        Assert.True(context.User.IsInRole("ApplicationAdmin"));
        Assert.True(context.User.IsInRole("ApplicationUser"));
        Assert.Same(user, ApplicationUserContext.Get(context));
    }

    [Fact]
    public async Task InvokeAsync_DoesNotGrantAdminToBasicUser()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            tenantId,
            objectId,
            "Review User",
            "reviewer@example.com",
            false,
            true,
            false,
            DateTimeOffset.UtcNow);
        var store = new StubApplicationUserStore(user);
        var context = CreateAuthenticatedContext(tenantId, objectId);
        var middleware = new ApplicationUserAccessMiddleware(
            _ => Task.CompletedTask,
            store,
            new ApplicationAuthenticationSettings(tenantId, Guid.NewGuid()),
            NullLogger<ApplicationUserAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(context.User.IsInRole("ApplicationUser"));
        Assert.False(context.User.IsInRole("ApplicationAdmin"));
    }

    [Fact]
    public async Task InvokeAsync_LoadsStoredAdminRoleForGovernanceRequests()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            tenantId,
            objectId,
            "BI Admin",
            "admin@example.com",
            true,
            true,
            false,
            DateTimeOffset.UtcNow);
        var context = CreateAuthenticatedContext(tenantId, objectId, "/api/governance-audit/runs");
        var middleware = new ApplicationUserAccessMiddleware(
            _ => Task.CompletedTask,
            new StubApplicationUserStore(user),
            new ApplicationAuthenticationSettings(tenantId, Guid.NewGuid()),
            NullLogger<ApplicationUserAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(context.User.IsInRole("ApplicationAdmin"));
        Assert.Same(user, ApplicationUserContext.Get(context));
    }

    [Fact]
    public async Task InvokeAsync_DoesNotGrantGovernanceAdminRoleToBasicUser()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            tenantId,
            objectId,
            "Review User",
            "reviewer@example.com",
            false,
            true,
            false,
            DateTimeOffset.UtcNow);
        var context = CreateAuthenticatedContext(tenantId, objectId, "/api/governance-audit/runs");
        var middleware = new ApplicationUserAccessMiddleware(
            _ => Task.CompletedTask,
            new StubApplicationUserStore(user),
            new ApplicationAuthenticationSettings(tenantId, Guid.NewGuid()),
            NullLogger<ApplicationUserAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(context.User.IsInRole("ApplicationUser"));
        Assert.False(context.User.IsInRole("ApplicationAdmin"));
    }

    private static DefaultHttpContext CreateAuthenticatedContext(
        Guid tenantId,
        Guid objectId,
        string path = "/api/admin/application-users")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tid", tenantId.ToString()),
            new Claim("oid", objectId.ToString()),
            new Claim(ClaimTypes.Name, "Signed-in user")
        ],
        "test"));
        return context;
    }

    private sealed class StubApplicationUserStore(ApplicationUserSession user) : IApplicationUserStore
    {
        public Task<ApplicationUserSession> EnsureUserAsync(
            Guid tenantId,
            Guid objectId,
            string? displayName,
            string? emailAddress,
            Guid bootstrapAdminObjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(user);

        public Task<IReadOnlyList<ApplicationUserSummary>> GetUsersAsync(
            Guid tenantId,
            Guid bootstrapAdminObjectId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ApplicationUserRoleChangeResult> SetAdminAsync(
            Guid tenantId,
            Guid targetUserId,
            Guid actorUserId,
            bool isAdmin,
            Guid bootstrapAdminObjectId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
