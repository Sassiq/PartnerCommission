using Microsoft.EntityFrameworkCore;
using Partners.Domain;
using Partners.Domain.Entities;

namespace Partners.Api.Data;

public sealed class PartnersDbContext(DbContextOptions<PartnersDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).ValueGeneratedNever();
            e.Property(u => u.ExternalId).HasMaxLength(200).IsRequired();
            e.HasIndex(u => u.ExternalId).IsUnique();
            e.HasIndex(u => u.PartnerId);
            e.HasOne<User>().WithMany().HasForeignKey(u => u.PartnerId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
