namespace Flowlio.Server;

/// <summary>Centralized distributed-cache key names.</summary>
public static class CacheKeys
{
    public static string Dashboard(Guid familyId) => $"dashboard:{familyId}";
}
