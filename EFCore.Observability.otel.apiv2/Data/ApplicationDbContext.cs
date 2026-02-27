using Microsoft.EntityFrameworkCore;

namespace EFCore.Observability.otel.apiv2.Data;


public class PrimaryDbContext : DbContext
{
    public PrimaryDbContext(DbContextOptions<PrimaryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Bill> Bills { get; set; }
    public DbSet<Payment> Payments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Bill>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.DueDate).IsRequired();
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.PaymentDate).HasDefaultValueSql("GETDATE()");
            entity.HasOne(e => e.Bill)
                  .WithMany()
                  .HasForeignKey(e => e.BillId);
        });
    }
}





public class ReplicaDbContext : DbContext
{
    public ReplicaDbContext(DbContextOptions<ReplicaDbContext> options)
        : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public DbSet<Bill> Bills { get; set; }
    public DbSet<Payment> Payments { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Bill>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("Bills");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("Payments");
        });
    }
    public override void Dispose()
    {
        base.Dispose();
    }

}

public class Bill
{
    public int Id { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Payment
{
    public int Id { get; set; }
    public int BillId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public Bill? Bill { get; set; }
}
