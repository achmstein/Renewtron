using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Renewtron.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<SearchLog> SearchLogs { get; set; }
    public DbSet<SearchResult> SearchResults { get; set; }
    public DbSet<RenewalRequest> RenewalRequests { get; set; }
    public DbSet<StripePayment> StripePayments { get; set; }
    public DbSet<Lead> Leads { get; set; }
    public DbSet<OntraportSale> OntraportSales { get; set; }
    public DbSet<RenewalOrder> RenewalOrders { get; set; }
    public DbSet<BulkRenewalUpload> BulkRenewalUploads { get; set; }
    public DbSet<OutboundMessage> OutboundMessages { get; set; }
    public DbSet<FunnelEvent> FunnelEvents { get; set; }
    public DbSet<OntraportSyncOutbox> OntraportSyncOutbox { get; set; }
    public DbSet<AsicKeyNotification> AsicKeyNotifications { get; set; }
    public DbSet<AsicKeyNotificationPdf> AsicKeyNotificationPdfs { get; set; }
    public DbSet<AsicKeyRequest> AsicKeyRequests { get; set; }
    public DbSet<RegisteredBusinessName> RegisteredBusinessNames { get; set; }
    public DbSet<BusinessNameImport> BusinessNameImports { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<ClientEmail> ClientEmails { get; set; }
    public DbSet<AbnHolder> AbnHolders { get; set; }
    public DbSet<ClientAbn> ClientAbns { get; set; }
    public DbSet<ClientBusinessName> ClientBusinessNames { get; set; }
    public DbSet<AsicPortalLink> AsicPortalLinks { get; set; }


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
            entity.Property(e => e.VerificationError).HasMaxLength(500);
            entity.Ignore(e => e.IsVerified);
        });

        modelBuilder.Entity<SearchResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BusinessName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AccountNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.RegistrationDate).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.SearchLogId);
            entity.Property(e => e.IsAvailable).HasDefaultValue(true);

            entity.HasOne(e => e.SearchLog)
                .WithMany(s => s.Results)
                .HasForeignKey(e => e.SearchLogId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RegisteredBusinessName>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(11).IsUnicode(false);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.RegistrationDate).IsRequired().HasMaxLength(10).IsUnicode(false);
            entity.HasIndex(e => new { e.Abn, e.ImportId }).IncludeProperties(e => new { e.Name, e.RegistrationDate });
            // Old imports are cleared in batches by ImportId.
            entity.HasIndex(e => e.ImportId);
        });

        modelBuilder.Entity<BusinessNameImport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceUrl).IsRequired().HasMaxLength(500);
            entity.Property(e => e.SourceModified).HasMaxLength(64);
            entity.Property(e => e.Error).HasMaxLength(1000);
        });

        modelBuilder.Entity<RenewalRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SearchResultId).IsUnique();
            entity.HasIndex(e => e.InitiatedAt);

            // Match the stored snapshot's decimal(18,2) so EF doesn't flag PendingModelChanges.
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BpayAmount).HasPrecision(18, 2);
            entity.Property(e => e.AirwallexTransferId).HasMaxLength(64);
            entity.Property(e => e.AirwallexTransferStatus).HasMaxLength(32);
            entity.Property(e => e.BpayPaymentError).HasMaxLength(1000);

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
            entity.Property(e => e.Source).HasMaxLength(64);
            entity.Property(e => e.OntraportContactId).HasMaxLength(20);
            entity.Property(e => e.VisitorId).HasMaxLength(64);

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
            entity.Property(e => e.Address).HasMaxLength(200);
            entity.Property(e => e.Suburb).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(50);
            entity.Property(e => e.Postcode).HasMaxLength(10);
            entity.Property(e => e.TfnEncrypted).HasMaxLength(200);
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

        modelBuilder.Entity<BulkRenewalUpload>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BusinessName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(20);
            entity.Property(e => e.OwnerName).HasMaxLength(200);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.SourceFile).HasMaxLength(260);

            entity.HasIndex(e => e.Abn);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RenewalDueDate);

            entity.HasOne(e => e.RenewalRequest)
                .WithMany()
                .HasForeignKey(e => e.RenewalRequestId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OutboundMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Template).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Subject).HasMaxLength(300);
            entity.Property(e => e.Recipient).IsRequired().HasMaxLength(300);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

            entity.HasIndex(e => e.LeadId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.BatchId);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.Lead)
                .WithMany()
                .HasForeignKey(e => e.LeadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OntraportSyncOutbox>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OntraportContactId).IsRequired().HasMaxLength(20);
            entity.Property(e => e.LastError).HasMaxLength(1000);

            entity.HasIndex(e => e.SentAt);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<AsicKeyRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OntraportContactId).HasMaxLength(20);
            entity.Property(e => e.GivenNames).IsRequired().HasMaxLength(140);
            entity.Property(e => e.FamilyName).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(20);
            entity.Property(e => e.BusinessName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.OtherBusinessNames).HasMaxLength(4000);
            entity.Property(e => e.Question).HasMaxLength(4000);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(20);
            entity.Property(e => e.AsicReferenceNumber).HasMaxLength(50);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

            entity.HasOne(e => e.OntraportSale)
                .WithMany()
                .HasForeignKey(e => e.OntraportSaleId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.AsicKeyNotification)
                .WithMany()
                .HasForeignKey(e => e.AsicKeyNotificationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.BusinessName);
            entity.HasIndex(e => e.Abn);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.OntraportSaleId);
        });

        modelBuilder.Entity<AsicKeyNotification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MessageId).IsRequired().HasMaxLength(300);
            entity.Property(e => e.Subject).HasMaxLength(300);
            entity.Property(e => e.From).HasMaxLength(300);
            entity.Property(e => e.BusinessName).HasMaxLength(200);
            entity.Property(e => e.Abn).HasMaxLength(20);
            entity.Property(e => e.DownloadUrl).HasMaxLength(2000);
            entity.Property(e => e.AsicKey).HasMaxLength(50);
            entity.Property(e => e.OntraportContactIds).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.PdfTextExcerpt).HasMaxLength(2000);

            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ReceivedAt);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.AsicKey);
        });

        modelBuilder.Entity<AsicKeyNotificationPdf>(entity =>
        {
            entity.HasKey(e => e.NotificationId);
            entity.HasOne<AsicKeyNotification>()
                .WithOne()
                .HasForeignKey<AsicKeyNotificationPdf>(e => e.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.MobileNumber).HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(200);
            entity.Property(e => e.Suburb).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(50);
            entity.Property(e => e.Postcode).HasMaxLength(10);
            entity.Property(e => e.TfnEncrypted).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.HasIndex(e => e.FullName);
            entity.HasIndex(e => e.PossibleDuplicateOfId);
            entity.HasIndex(e => e.LastSeenAt);
            entity.HasOne(e => e.PossibleDuplicateOf).WithMany()
                .HasForeignKey(e => e.PossibleDuplicateOfId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ClientEmail>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.OntraportContactId).HasMaxLength(20);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(20).HasConversion<string>();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.OntraportContactId);
            entity.HasOne(e => e.Client).WithMany(c => c.Emails)
                .HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AbnHolder>(entity =>
        {
            entity.HasKey(e => e.Abn);
            entity.Property(e => e.Abn).HasMaxLength(11).IsUnicode(false);
            entity.HasIndex(e => e.RegisterCheckedAt);
        });

        modelBuilder.Entity<ClientAbn>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(11).IsUnicode(false);
            entity.HasIndex(e => new { e.ClientId, e.Abn }).IsUnique();
            entity.HasIndex(e => e.Abn);
            // One reminder contact per ABN.
            entity.HasIndex(e => e.Abn, "IX_ClientAbns_Abn_ReminderContact").IsUnique().HasFilter("[IsReminderContact] = 1");
            entity.HasOne(e => e.Client).WithMany(c => c.Abns)
                .HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Holder).WithMany(h => h.Contacts)
                .HasForeignKey(e => e.Abn).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ClientBusinessName>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(11).IsUnicode(false);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AsicAccountNumber).HasMaxLength(50);
            entity.Property(e => e.DueDateSource).HasMaxLength(20).HasConversion<string>();
            entity.Property(e => e.AsicKey).HasMaxLength(50);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(20).HasConversion<string>();
            // The register's key: a name is held by one ABN.
            entity.HasIndex(e => new { e.Abn, e.Name }).IsUnique();
            entity.HasIndex(e => e.RenewalDueDate);
            entity.HasOne(e => e.Holder).WithMany(h => h.BusinessNames)
                .HasForeignKey(e => e.Abn).OnDelete(DeleteBehavior.NoAction);
        });

        // Links from the older records. NoAction: SQL Server refuses two cascade paths into one
        // table, and the linker repoints these itself when clients are merged.
        modelBuilder.Entity<Lead>().HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<Lead>().HasIndex(e => e.ClientId);
        modelBuilder.Entity<OntraportSale>().HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<OntraportSale>().HasOne<ClientBusinessName>().WithMany().HasForeignKey(e => e.ClientBusinessNameId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<OntraportSale>().HasIndex(e => e.ClientId);
        modelBuilder.Entity<OntraportSale>().Property(e => e.HandledNote).HasMaxLength(500);
        modelBuilder.Entity<RenewalRequest>().Property(e => e.HandledNote).HasMaxLength(500);

        modelBuilder.Entity<RenewalOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Abn).HasMaxLength(20);
            entity.Property(e => e.BusinessName).HasMaxLength(200);
            entity.Property(e => e.NameKey).HasMaxLength(200);
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.AmountPaid).HasPrecision(18, 2);
            entity.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Stage).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Reason).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(e => new { e.Source, e.SourceId }).IsUnique();
            entity.HasIndex(e => e.Stage);
            entity.HasIndex(e => new { e.Abn, e.NameKey });
            entity.HasIndex(e => e.ClientId);
            entity.HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<RenewalRequest>().HasOne<RenewalOrder>().WithMany().HasForeignKey(e => e.RenewalOrderId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<RenewalRequest>().HasIndex(e => e.RenewalOrderId);
        modelBuilder.Entity<OntraportSale>().HasIndex(e => e.ClientBusinessNameId);
        modelBuilder.Entity<RenewalRequest>().HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<RenewalRequest>().HasOne<ClientBusinessName>().WithMany().HasForeignKey(e => e.ClientBusinessNameId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<RenewalRequest>().HasIndex(e => e.ClientId);
        modelBuilder.Entity<RenewalRequest>().HasIndex(e => e.ClientBusinessNameId);
        modelBuilder.Entity<BulkRenewalUpload>().HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<BulkRenewalUpload>().HasOne<ClientBusinessName>().WithMany().HasForeignKey(e => e.ClientBusinessNameId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<BulkRenewalUpload>().HasIndex(e => e.ClientId);
        modelBuilder.Entity<AsicKeyRequest>().HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<AsicKeyRequest>().HasIndex(e => e.ClientId);
        modelBuilder.Entity<AsicKeyNotification>().HasOne<ClientBusinessName>().WithMany().HasForeignKey(e => e.ClientBusinessNameId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<AsicKeyNotification>().HasIndex(e => e.ClientBusinessNameId);
        modelBuilder.Entity<AsicPortalLink>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BusinessName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Abn).IsRequired().HasMaxLength(11).IsUnicode(false);
            entity.Property(e => e.AsicKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DateOfBirth).IsRequired().HasMaxLength(10);
            entity.Property(e => e.DateOfBirthSource).IsRequired().HasMaxLength(10);
            entity.Property(e => e.OntraportContactId).HasMaxLength(20);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20).HasConversion<string>();
            entity.Property(e => e.DeclareAs).IsRequired().HasMaxLength(20).HasConversion<string>();
            entity.Property(e => e.LinkOutcome).HasMaxLength(20);
            entity.Property(e => e.EmailOutcome).HasMaxLength(20);
            entity.Property(e => e.EmailReference).HasMaxLength(50);
            entity.Property(e => e.RepsOutcome).HasMaxLength(20);
            entity.Property(e => e.RepsReferences).HasMaxLength(500);
            entity.Property(e => e.HolderType).HasMaxLength(50);
            entity.Property(e => e.RepsRemoved).HasMaxLength(2000);
            entity.Property(e => e.HolderOutcome).HasMaxLength(20);
            entity.Property(e => e.HolderReference).HasMaxLength(50);
            entity.Property(e => e.FailedAtStep).HasMaxLength(40);
            entity.Property(e => e.Error).HasMaxLength(2000);
            entity.Property(e => e.Screenshot).HasMaxLength(200);
            entity.Property(e => e.OntraportResult).HasMaxLength(200);
            entity.Property(e => e.RequestedBy).HasMaxLength(200);
            entity.HasIndex(e => e.ClientBusinessNameId);
            entity.HasIndex(e => e.ClientId);
            entity.HasIndex(e => e.Status);
            entity.HasOne<ClientBusinessName>().WithMany().HasForeignKey(e => e.ClientBusinessNameId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<OutboundMessage>().HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<OutboundMessage>().HasIndex(e => e.ClientId);

        modelBuilder.Entity<FunnelEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VisitorId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.SessionId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Step).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Abn).HasMaxLength(20);
            entity.Property(e => e.Source).HasMaxLength(64);
            entity.Property(e => e.Detail).HasMaxLength(500);
            entity.Property(e => e.Path).HasMaxLength(300);
            entity.Property(e => e.Referrer).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);

            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Step);
            entity.HasIndex(e => e.VisitorId);
            entity.HasIndex(e => new { e.Step, e.CreatedAt });

            entity.HasOne(e => e.Lead)
                .WithMany()
                .HasForeignKey(e => e.LeadId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
