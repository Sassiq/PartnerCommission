using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Partners.Api.Data;
using Partners.Api.Controllers;
using PartnerCommission.Contracts.Partners;

namespace Partners.Api.GrpcServices;

public sealed class PartnersInternalService(TreeQueries tree, PartnersDbContext db)
    : PartnersInternal.PartnersInternalBase
{
    public override async Task<GetAncestorsResponse> GetAncestors(GetAncestorsRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.UserExternalId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_external_id is required."));
        }

        if (request.MaxLevels is < 1 or > UsersController.MaxTreeDepth)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument, $"max_levels must be between 1 and {UsersController.MaxTreeDepth}."));
        }

        var ancestors = await tree.GetAncestorsAsync(request.UserExternalId, request.MaxLevels, context.CancellationToken);

        if (ancestors.Count == 0 &&
            !await db.Users.AnyAsync(u => u.ExternalId == request.UserExternalId, context.CancellationToken))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"User '{request.UserExternalId}' does not exist."));
        }

        var response = new GetAncestorsResponse();
        response.Ancestors.AddRange(ancestors.Select(a => new Ancestor { Level = a.Level, ExternalId = a.ExternalId }));
        return response;
    }
}
