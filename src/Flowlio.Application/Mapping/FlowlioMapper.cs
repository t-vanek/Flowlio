using Flowlio.Domain;
using Flowlio.Shared;
using Riok.Mapperly.Abstractions;

namespace Flowlio.Application.Mapping;

/// <summary>Source-generated mapping between domain entities and shared DTOs.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class FlowlioMapper
{
    [MapperIgnoreTarget(nameof(BankAccountDto.CurrentBalance))]
    public partial BankAccountDto ToDto(BankAccount entity);

    public partial CategoryDto ToDto(Category entity);

    // CategoryName is flattened from entity.Category.Name by Mapperly.
    public partial TransactionDto ToDto(Transaction entity);

    public partial RecurringPaymentDto ToDto(RecurringPayment entity);

    public partial SubscriptionDto ToDto(Subscription entity);
}
