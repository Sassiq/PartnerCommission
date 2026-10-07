using Grpc.Core;
using Ancestor = Commission.Domain.Ancestor;
using PartnerCommission.Contracts.Partners;
using PartnerCommission.Messaging.Consuming;

namespace Commission.Api.Partners;

public interface IAncestorsProvider
{
    /// <exception cref="PermanentMessageException">The user does not exist.</exception>
    /// <exception cref="TransientMessageException">Partners is unavailable.</exception>
    Task<IReadOnlyList<Ancestor>> GetAncestorsAsync(string userExternalId, int maxLevels, CancellationToken ct);
}

public sealed class GrpcAncestorsProvider(PartnersInternal.PartnersInternalClient client) : IAncestorsProvider
{
    public async Task<IReadOnlyList<Ancestor>> GetAncestorsAsync(string userExternalId, int maxLevels, CancellationToken ct)
    {
        try
        {
            var response = await client.GetAncestorsAsync(
                new GetAncestorsRequest { UserExternalId = userExternalId, MaxLevels = maxLevels },
                cancellationToken: ct);

            return response.Ancestors.Select(a => new Ancestor(a.Level, a.ExternalId)).ToList();
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.InvalidArgument)
        {
            throw new PermanentMessageException($"Partners rejected the request for '{userExternalId}': {ex.Status.Detail}", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TransientMessageException($"Partners is unavailable: {ex.Message}", ex);
        }
    }
}
