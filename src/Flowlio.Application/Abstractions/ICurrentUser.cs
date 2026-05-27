namespace Flowlio.Application.Abstractions;

/// <summary>Ambient information about the authenticated user making the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
}
