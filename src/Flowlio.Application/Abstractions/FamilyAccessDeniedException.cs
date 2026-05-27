namespace Flowlio.Application.Abstractions;

/// <summary>
/// Thrown while resolving the current family when the authenticated user's membership has been
/// deactivated. Surfaced to API callers as HTTP 403.
/// </summary>
public sealed class FamilyAccessDeniedException(string message) : Exception(message);
