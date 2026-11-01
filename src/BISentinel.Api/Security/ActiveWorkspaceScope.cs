namespace BISentinel.Api.Security;

public sealed record ActiveWorkspaceScope(string ScopeType, Guid ScopeId)
{
    public const string ItemKey = "BI Sentinel active workspace scope";

    public static ActiveWorkspaceScope? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value)
            ? value as ActiveWorkspaceScope
            : null;
}
