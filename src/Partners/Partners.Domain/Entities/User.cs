namespace Partners.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";
    public Guid? PartnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static User Create(string externalId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        return new User
        {
            Id = Guid.CreateVersion7(),
            ExternalId = externalId,
            CreatedAt = createdAt
        };
    }
}
