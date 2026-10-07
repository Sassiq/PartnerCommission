using Commission.Domain;
using Commission.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts;
using PartnerCommission.Messaging.Outbox;

namespace Commission.Api.Data;

public sealed class CommissionDbContext(DbContextOptions<CommissionDbContext> options) : DbContext(options)
{
    public DbSet<ProfitEvent> ProfitEvents => Set<ProfitEvent>();
    public DbSet<CommissionRecord> Commissions => Set<CommissionRecord>();
    public DbSet<CommissionSettings> Settings => Set<CommissionSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingTables();

        modelBuilder.Entity<ProfitEvent>(e =>
        {
            e.ToTable("profit_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ExternalId).HasMaxLength(200).IsRequired();
            e.Property(x => x.UserExternalId).HasMaxLength(200).IsRequired();
            e.Property(x => x.Profit).HasPrecision(MoneyFormat.Precision, MoneyFormat.Scale);

            e.HasIndex(x => x.ExternalId).IsUnique();
            e.HasIndex(x => new { x.UserExternalId, x.OccurredAt });
        });

        modelBuilder.Entity<CommissionRecord>(e =>
        {
            e.ToTable("commissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.PartnerExternalId).HasMaxLength(200).IsRequired();
            e.Property(x => x.Amount).HasPrecision(MoneyFormat.Precision, MoneyFormat.Scale);
            e.HasOne<ProfitEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.EventId, x.PartnerExternalId }).IsUnique();
        });

        modelBuilder.Entity<CommissionSettings>(e =>
        {
            // Only one row in db
            e.ToTable("commission_settings", t => t.HasCheckConstraint("ck_commission_settings_singleton", $"id = {CommissionSettings.SingletonId}"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasData(new CommissionSettings
            {
                Id = CommissionSettings.SingletonId,
                ActiveSchema = SchemaType.Linear
            });
        });
    }
}
