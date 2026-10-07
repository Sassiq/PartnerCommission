using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts;
using PartnerCommission.Messaging.Outbox;
using Wallet.Domain;
using Wallet.Domain.Entities;

namespace Wallet.Api.Data;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<Accrual> Accruals => Set<Accrual>();
    public DbSet<UserWallet> Wallets => Set<UserWallet>();
    public DbSet<Payout> Payouts => Set<Payout>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingTables();

        modelBuilder.Entity<Accrual>(e =>
        {
            e.ToTable("accruals");
            e.HasKey(x => x.CommissionId);
            e.Property(x => x.CommissionId).ValueGeneratedNever();
            e.Property(x => x.UserExternalId).HasMaxLength(200).IsRequired();
            e.Property(x => x.EventExternalId).HasMaxLength(200).IsRequired();
            e.Property(x => x.Amount).HasPrecision(MoneyFormat.Precision, MoneyFormat.Scale);
            
            e.HasIndex(x => x.UserExternalId)
                .HasFilter($"status = {(int)AccrualStatus.Pending}")
                .HasDatabaseName("ix_accruals_pending_user");
            e.HasIndex(x => x.PayoutId);
        });

        modelBuilder.Entity<UserWallet>(e =>
        {
            e.ToTable("wallets");
            e.HasKey(x => x.UserExternalId);
            e.Property(x => x.UserExternalId).HasMaxLength(200);
            e.Property(x => x.Balance).HasPrecision(MoneyFormat.Precision, MoneyFormat.Scale);
        });

        modelBuilder.Entity<Payout>(e =>
        {
            e.ToTable("payouts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserExternalId).HasMaxLength(200).IsRequired();
            e.Property(x => x.Amount).HasPrecision(MoneyFormat.Precision, MoneyFormat.Scale);
            e.HasIndex(x => new { x.UserExternalId, x.PaidAt });
        });
    }
}
