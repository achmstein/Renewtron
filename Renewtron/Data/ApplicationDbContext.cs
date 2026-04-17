using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Renewtron.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<SearchLog> SearchLogs { get; set; }
    public DbSet<SearchResult> SearchResults { get; set; }
    public DbSet<RenewalRequest> RenewalRequests { get; set; }
    public DbSet<AirwallexPayment> AirwallexPayments { get; set; }
    public DbSet<Lead> Leads { get; set; }
    public DbSet<OntraportSale> OntraportSales { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>().ToTable("Users");
        modelBuilder.Entity<IdentityRole>().ToTable("Roles");
        modelBuilder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
        modelBuilder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");

        modelBuilder.Entity<SearchLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(20);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.HasIndex(e => e.SearchedAt);
            entity.HasIndex(e => e.Abn);
        });

        modelBuilder.Entity<SearchResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BusinessName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AccountNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.RegistrationDate).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.SearchLogId);

            entity.HasOne(e => e.SearchLog)
                .WithMany(s => s.Results)
                .HasForeignKey(e => e.SearchLogId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RenewalRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SearchResultId).IsUnique();
            entity.HasIndex(e => e.InitiatedAt);

            entity.HasOne(e => e.SearchResult)
                .WithOne(s => s.RenewalRequest)
                .HasForeignKey<RenewalRequest>(e => e.SearchResultId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.AirwallexPayment)
                .WithOne(sp => sp.RenewalRequest)
                .HasForeignKey<AirwallexPayment>(sp => sp.RenewalRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AirwallexPayment>(entity =>
        {
            entity.ToTable("StripePayments"); // Keep existing table name for backward compatibility
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PaymentIntentId).IsRequired();
            entity.Property(e => e.PaymentStatus).IsRequired();
            entity.HasIndex(e => e.RenewalRequestId).IsUnique();
            entity.HasIndex(e => e.PaymentIntentId);
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(20);
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.MobileNumber).IsRequired().HasMaxLength(20);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.OutcomeMessage).HasMaxLength(500);

            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Abn);
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.Outcome);

            entity.HasOne(e => e.SearchLog)
                .WithMany()
                .HasForeignKey(e => e.SearchLogId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.RenewalRequests)
                .WithOne(r => r.Lead)
                .HasForeignKey(r => r.LeadId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OntraportSale>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OntraportContactId).IsRequired().HasMaxLength(20);
            entity.Property(e => e.OntraportTransactionId).HasMaxLength(20);
            entity.Property(e => e.ContactName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.MobileNumber).HasMaxLength(20);
            entity.Property(e => e.DateOfBirth).HasMaxLength(20);
            entity.Property(e => e.BusinessName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(20);
            entity.Property(e => e.BusinessNameOwner).HasMaxLength(200);
            entity.Property(e => e.AmountPaid).HasPrecision(18, 2);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

            entity.HasIndex(e => e.OntraportContactId);
            entity.HasIndex(e => e.Abn);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RenewalDueDate);

            entity.HasOne(e => e.RenewalRequest)
                .WithMany()
                .HasForeignKey(e => e.RenewalRequestId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
