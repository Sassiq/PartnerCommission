using PartnerCommission.Contracts;

namespace Commission.Domain.Schemes;

public interface ICommissionScheme
{
    SchemaType Type { get; }

    decimal Multiplier(int level);
}
