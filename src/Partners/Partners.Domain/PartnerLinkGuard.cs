namespace Partners.Domain;

public sealed class InvalidPartnerLinkException(string message) : Exception(message);

public static class PartnerLinkGuard
{
    /// <summary>
    /// Check if user can be the partner of the user (no loops or self-references)
    /// </summary>
    public static void EnsureCanLink(string userExternalId, string partnerExternalId, IEnumerable<string> partnerAncestorExternalIds)
    {
        ArgumentNullException.ThrowIfNull(partnerAncestorExternalIds);

        if (string.Equals(userExternalId, partnerExternalId, StringComparison.Ordinal))
        {
            throw new InvalidPartnerLinkException("A user cannot be their own partner.");
        }

        if (partnerAncestorExternalIds.Contains(userExternalId, StringComparer.Ordinal))
        {
            throw new InvalidPartnerLinkException(
                $"'{partnerExternalId}' is in the partner tree below '{userExternalId}'; linking them would create a cycle.");
        }
    }
}
