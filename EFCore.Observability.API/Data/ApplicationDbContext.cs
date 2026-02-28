using Microsoft.EntityFrameworkCore;

namespace EFCore.Observability.API.Data;

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



        modelBuilder.Entity<Bill>().HasData(
            new Bill { Id = 1, AccountNumber = "ACC001", Amount = 100.50m, DueDate = DateTime.Now.AddDays(30), Status = "Pending", CreatedAt = DateTime.Now },
            new Bill { Id = 2, AccountNumber = "ACC002", Amount = 250.00m, DueDate = DateTime.Now.AddDays(15), Status = "Pending", CreatedAt = DateTime.Now },
            new Bill { Id = 3, AccountNumber = "ACC003", Amount = 75.25m, DueDate = DateTime.Now.AddDays(45), Status = "Paid", CreatedAt = DateTime.Now }
            );

        modelBuilder.Entity<Payment>().HasData(
            new Payment { Id = 1, BillId = 1, Amount = 100.50m, PaymentDate = DateTime.Now, TransactionId = "TXN001" },
            new Payment { Id = 2, BillId = 3, Amount = 75.25m, PaymentDate = DateTime.Now, TransactionId = "TXN002" }
        );
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
