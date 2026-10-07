using Microsoft.EntityFrameworkCore;

namespace Partners.Api.Data;

public sealed class AncestorRow
{
    public int Level { get; set; }
    public string ExternalId { get; set; } = "";
}

public sealed class DescendantRow
{
    public int Level { get; set; }
    public string ExternalId { get; set; } = "";
    public string PartnerExternalId { get; set; } = "";
}

public sealed class TreeQueries(PartnersDbContext db)
{
    //Protection if we get in cycle in tree
    public const int TreeDepthLimit = 10_000;

    public Task<List<AncestorRow>> GetAncestorsAsync(string externalId, int maxLevels, CancellationToken ct) =>
        db.Database.SqlQuery<AncestorRow>($"""
            WITH RECURSIVE chain(id, level) AS (
                SELECT u.partner_id, 1
                FROM users u
                WHERE u.external_id = {externalId} AND u.partner_id IS NOT NULL
                UNION ALL
                SELECT p.partner_id, c.level + 1
                FROM chain c
                JOIN users p ON p.id = c.id
                WHERE p.partner_id IS NOT NULL AND c.level < {maxLevels}
            )
            SELECT c.level AS level, a.external_id AS external_id
            FROM chain c
            JOIN users a ON a.id = c.id
            ORDER BY c.level
            """).ToListAsync(ct);

    public Task<List<DescendantRow>> GetDescendantsAsync(string externalId, int maxDepth, CancellationToken ct) =>
        db.Database.SqlQuery<DescendantRow>($"""
            WITH RECURSIVE tree(id, level) AS (
                SELECT c.id, 1
                FROM users c
                JOIN users r ON c.partner_id = r.id
                WHERE r.external_id = {externalId}
                UNION ALL
                SELECT c.id, t.level + 1
                FROM tree t
                JOIN users c ON c.partner_id = t.id
                WHERE t.level < {maxDepth}
            )
            SELECT t.level AS level, u.external_id AS external_id, r.external_id AS partner_external_id
            FROM tree t
            JOIN users u ON u.id = t.id
            JOIN users r ON r.id = u.partner_id
            ORDER BY t.level, u.external_id
            """).ToListAsync(ct);
}
