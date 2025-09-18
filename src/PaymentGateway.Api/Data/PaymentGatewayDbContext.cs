using Microsoft.EntityFrameworkCore;
using PaymentGateway.Api.Data.Entities;

namespace PaymentGateway.Api.Data;

public class PaymentGatewayDbContext : DbContext
{
    public PaymentGatewayDbContext(DbContextOptions<PaymentGatewayDbContext> options)
        : base(options)
    {
    }

    public DbSet<PaymentRequest> PaymentRequests { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaymentRequest>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CardNumber)
                .IsRequired()
                .HasMaxLength(19);

            entity.Property(e => e.CardNumberLastFour)
                .IsRequired()
                .HasMaxLength(4);

            entity.Property(e => e.Currency)
                .IsRequired()
                .HasMaxLength(3);

            entity.Property(e => e.CVV)
                .IsRequired()
                .HasMaxLength(4);

            entity.Property(e => e.Status)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(100);

            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasFilter("IdempotencyKey IS NOT NULL");

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Status, e.NextRetryAt });
        });
    }
}