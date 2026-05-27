using System.ComponentModel.DataAnnotations;

namespace Flowlio.Server.Validation;

/// <summary>
/// Re-validates request DTOs server-side: the client's validation is advisory and bypassable, so the
/// same DataAnnotations are enforced here. Any argument declared in the Flowlio.Shared contracts is
/// checked; a failure short-circuits with an RFC 7807 validation problem (400) before the handler runs.
/// </summary>
public sealed class ValidationEndpointFilter : IEndpointFilter
{
    private const string ContractsNamespace = "Flowlio.Shared";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is null || argument.GetType().Namespace != ContractsNamespace)
                continue;

            var results = new List<ValidationResult>();
            if (Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
                continue;

            var errors = results
                .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [string.Empty])
                    .Select(member => (Member: member, Message: r.ErrorMessage ?? "Neplatná hodnota.")))
                .GroupBy(x => x.Member, x => x.Message)
                .ToDictionary(g => g.Key, g => g.ToArray());

            return Results.ValidationProblem(errors);
        }

        return await next(context);
    }
}
