namespace Partners.Api.Models;

public sealed record TreeResponse(
    string ExternalId,
    TreeDirection Direction,
    IReadOnlyList<TreeNodeResponse> Nodes);
