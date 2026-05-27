namespace Flowlio.Server.Auth;

/// <summary>Shared result helpers for permission-gated API endpoints.</summary>
public static class MemberAuthorization
{
    public static IResult Forbidden() =>
        Results.Problem(detail: "Na tuto akci nemáte oprávnění.", statusCode: StatusCodes.Status403Forbidden);
}
