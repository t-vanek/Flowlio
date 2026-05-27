namespace Flowlio.Application.Statements;

/// <summary>
/// Published after a statement import completes. Routed through RabbitMQ so the completion can be
/// processed asynchronously (cache invalidation, live SignalR notification to the family).
/// </summary>
public sealed record StatementImported
{
    public required Guid FamilyId { get; init; }
    public required Guid BankAccountId { get; init; }
    public required Guid ImportBatchId { get; init; }
    public required int ImportedCount { get; init; }
    public required int DuplicateCount { get; init; }
}
