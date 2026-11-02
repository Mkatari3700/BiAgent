using BISentinel.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BISentinel.Api.Tests;

public sealed class ScopedReviewAccessMiddlewareTests
{
    [Fact]
    public async Task ReadOnlyUser_CannotAccessReviewerQueue()
    {
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Read-only",
            null,
            false,
            false,
            false,
            true,
            false,
            DateTimeOffset.UtcNow);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/reviews/submissions";
        context.Request.Method = HttpMethods.Get;
        context.Items[ApplicationUserContext.ItemKey] = user;
        context.Items[ActiveWorkspaceScope.ItemKey] =
            new ActiveWorkspaceScope("Project", Guid.NewGuid());
        var nextCalled = false;
        var middleware = new ScopedReviewAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new StubOrganizationHierarchyStore(canReviewScope: false),
            new ApplicationAuthenticationSettings(user.TenantId, Guid.Empty),
            NullLogger<ScopedReviewAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task ScopedLead_CanAccessReviewerQueueWithinAssignedScope()
    {
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Project lead",
            null,
            false,
            false,
            false,
            true,
            false,
            DateTimeOffset.UtcNow)
        {
            IsProjectLead = true
        };
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/reviews/submissions";
        context.Request.Method = HttpMethods.Get;
        context.Items[ApplicationUserContext.ItemKey] = user;
        context.Items[ActiveWorkspaceScope.ItemKey] =
            new ActiveWorkspaceScope("Project", Guid.NewGuid());
        var nextCalled = false;
        var middleware = new ScopedReviewAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new StubOrganizationHierarchyStore(canReviewScope: true),
            new ApplicationAuthenticationSettings(user.TenantId, Guid.Empty),
            NullLogger<ScopedReviewAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task ScopedLead_CannotAccessReviewerQueueOutsideLeadScope()
    {
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Project lead",
            null,
            false,
            false,
            false,
            true,
            false,
            DateTimeOffset.UtcNow)
        {
            IsProjectLead = true
        };
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/reviews/submissions";
        context.Request.Method = HttpMethods.Get;
        context.Items[ApplicationUserContext.ItemKey] = user;
        context.Items[ActiveWorkspaceScope.ItemKey] =
            new ActiveWorkspaceScope("Project", Guid.NewGuid());
        var nextCalled = false;
        var middleware = new ScopedReviewAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new StubOrganizationHierarchyStore(canReviewScope: false),
            new ApplicationAuthenticationSettings(user.TenantId, Guid.Empty),
            NullLogger<ScopedReviewAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyUser_CanSubmitReviewWithinAssignedScope()
    {
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Read-only",
            null,
            false,
            false,
            false,
            true,
            false,
            DateTimeOffset.UtcNow);
        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/reviews/history/{Guid.NewGuid()}/submission";
        context.Request.Method = HttpMethods.Post;
        context.Items[ApplicationUserContext.ItemKey] = user;
        context.Items[ActiveWorkspaceScope.ItemKey] =
            new ActiveWorkspaceScope("Project", Guid.NewGuid());
        var nextCalled = false;
        var hierarchyStore = new StubOrganizationHierarchyStore(canReviewScope: false);
        var middleware = new ScopedReviewAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            hierarchyStore,
            new ApplicationAuthenticationSettings(user.TenantId, Guid.Empty),
            NullLogger<ScopedReviewAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, hierarchyStore.CanReviewScopeCallCount);
    }

    [Fact]
    public async Task ReadOnlyUser_CannotSubmitReviewWithoutActiveScope()
    {
        var user = new ApplicationUserSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Read-only",
            null,
            false,
            false,
            false,
            true,
            false,
            DateTimeOffset.UtcNow);
        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/reviews/history/{Guid.NewGuid()}/submission";
        context.Request.Method = HttpMethods.Post;
        context.Items[ApplicationUserContext.ItemKey] = user;
        var nextCalled = false;
        var middleware = new ScopedReviewAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new StubOrganizationHierarchyStore(canReviewScope: false),
            new ApplicationAuthenticationSettings(user.TenantId, Guid.Empty),
            NullLogger<ScopedReviewAccessMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private sealed class StubOrganizationHierarchyStore(bool canReviewScope) : IOrganizationHierarchyStore
    {
        public int CanReviewScopeCallCount { get; private set; }

        public Task<bool> IsScopeAccessibleAsync(
            Guid tenantId,
            Guid userId,
            string scopeType,
            Guid scopeId,
            bool isRootAdmin,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<bool> CanReviewScopeAsync(
            Guid tenantId,
            Guid userId,
            string scopeType,
            Guid scopeId,
            CancellationToken cancellationToken)
        {
            CanReviewScopeCallCount++;
            return Task.FromResult(canReviewScope);
        }

        public Task<OrganizationHierarchySnapshot> GetSnapshotAsync(
            Guid tenantId,
            Guid userId,
            Guid bootstrapAdminObjectId,
            bool isRootAdmin,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<(HierarchyMutationResult Result, Guid Id)> CreateOrganizationAsync(
            Guid tenantId,
            Guid actorUserId,
            string name,
            bool isRootAdmin,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<(HierarchyMutationResult Result, Guid Id)> CreateProjectAsync(
            Guid tenantId,
            Guid actorUserId,
            Guid organizationId,
            string name,
            bool isRootAdmin,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<(HierarchyMutationResult Result, Guid Id)> CreateTeamAsync(
            Guid tenantId,
            Guid actorUserId,
            Guid projectId,
            string name,
            bool isRootAdmin,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<HierarchyMutationResult> SetAssignmentAsync(
            Guid tenantId,
            Guid actorUserId,
            Guid targetUserId,
            string scopeType,
            Guid scopeId,
            string roleName,
            bool isAssigned,
            bool isRootAdmin,
            CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
