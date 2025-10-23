using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Renewtron.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<SearchLog> SearchLogs { get; set; }
    public DbSet<SearchResult> SearchResults { get; set; }
    public DbSet<Holder> Holders { get; set; }
    public DbSet<RenewalRequest> RenewalRequests { get; set; }
    public DbSet<SavedCreditCard> SavedCreditCards { get; set; }
    public DbSet<StripePayment> StripePayments { get; set; }


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
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.SearchLogId);

            entity.HasOne(e => e.SearchLog)
                .WithMany(s => s.Results)
                .HasForeignKey(e => e.SearchLogId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Holder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Type).HasMaxLength(100);
            entity.Property(e => e.Abn).HasMaxLength(20);
            entity.HasIndex(e => e.SearchResultId);

            entity.HasOne(e => e.SearchResult)
                .WithMany(s => s.Holders)
                .HasForeignKey(e => e.SearchResultId)
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

            entity.HasOne(e => e.StripePayment)
                .WithOne(sp => sp.RenewalRequest)
                .HasForeignKey<StripePayment>(sp => sp.RenewalRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StripePayment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PaymentIntentId).IsRequired();
            entity.Property(e => e.PaymentStatus).IsRequired();
            entity.HasIndex(e => e.RenewalRequestId).IsUnique();
            entity.HasIndex(e => e.PaymentIntentId);
            entity.HasIndex(e => e.CustomerCreditCardId);

            entity.HasOne(e => e.CustomerCreditCard)
                .WithMany()
                .HasForeignKey(e => e.CustomerCreditCardId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SavedCreditCard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CardholderName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CardNumberLast4).IsRequired().HasMaxLength(4);
            entity.Property(e => e.CardBrand).HasMaxLength(50);
            entity.Property(e => e.EncryptedCardNumber).IsRequired();
            entity.Property(e => e.EncryptedCvc).IsRequired();
            entity.HasIndex(e => e.IpAddress);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
