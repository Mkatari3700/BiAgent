using BISentinel.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BISentinel.Api.Tests;

public sealed class WorkspaceScopeMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_RequiresScopeForUnscopedReviewDataForNonAdmin()
    {
        var context = CreateContext("/api/reviews/history");
        context.Items[ApplicationUserContext.ItemKey] = CreateUser(isAdmin: false);
        var nextInvoked = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextInvoked);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AllowsUnscopedReviewDataForRootAdmin()
    {
        var context = CreateContext("/api/reviews/history");
        context.Items[ApplicationUserContext.ItemKey] = CreateUser(isAdmin: true);
        var nextInvoked = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextInvoked);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AllowsAccessRequestsWithoutASelectedScope()
    {
        var context = CreateContext("/api/access-requests/mine");
        context.Items[ApplicationUserContext.ItemKey] = CreateUser(isAdmin: false);
        var nextInvoked = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextInvoked);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static WorkspaceScopeMiddleware CreateMiddleware(RequestDelegate next) =>
        new(
            next,
            null!,
            new ApplicationAuthenticationSettings(Guid.NewGuid(), Guid.NewGuid()),
            NullLogger<WorkspaceScopeMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    private static ApplicationUserSession CreateUser(bool isAdmin) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Test user",
            "test@example.com",
            isAdmin,
            false,
            false,
            true,
            false,
            DateTimeOffset.UtcNow);
}
