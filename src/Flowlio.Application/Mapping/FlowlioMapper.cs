using Flowlio.Domain;
using Flowlio.Shared;
using Riok.Mapperly.Abstractions;

namespace Flowlio.Application.Mapping;

/// <summary>Source-generated mapping between domain entities and shared DTOs.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class FlowlioMapper
{
    [MapperIgnoreTarget(nameof(BankAccountDto.CurrentBalance))]
    [MapperIgnoreTarget(nameof(BankAccountDto.OwnerName))]
    [MapperIgnoreTarget(nameof(BankAccountDto.IsChildAccount))]
    [MapperIgnoreTarget(nameof(BankAccountDto.CardCount))]
    [MapperIgnoreTarget(nameof(BankAccountDto.DisponentCount))]
    public partial BankAccountDto ToDto(BankAccount entity);

    [MapperIgnoreTarget(nameof(BankCardDto.HolderName))]
    [MapperIgnoreTarget(nameof(BankCardDto.Version))]
    public partial BankCardDto ToDto(BankCard entity);

    public partial CategoryDto ToDto(Category entity);

    // CategoryName is flattened from entity.Category.Name by Mapperly (Include the navigation when querying).
    public partial CategorizationRuleDto ToDto(CategorizationRule entity);

    // CategoryName is flattened from entity.Category.Name by Mapperly.
    // Version is the xmin row-version, set by the endpoint via db.GetRowVersion(...).
    [MapperIgnoreTarget(nameof(TransactionDto.Version))]
    public partial TransactionDto ToDto(Transaction entity);

    public partial RecurringPaymentDto ToDto(RecurringPayment entity);

    public partial SubscriptionDto ToDto(Subscription entity);
}
