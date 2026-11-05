using HospitalSV.Models;
using HospitalSV.Services;
using Microsoft.EntityFrameworkCore;

namespace HospitalSV.Data;

public class AppDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUserService;

    public DbSet<Patient>            Patients            { get; set; }
    public DbSet<Doctor>             Doctors             { get; set; }
    public DbSet<MedicalAppointment> MedicalAppointments { get; set; }
    public DbSet<MedicalReport>      MedicalReports      { get; set; }
    public DbSet<AppUser>            Users               { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserService? currentUserService = null)
        : base(options)
    {
        _currentUserService = currentUserService;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global Query Filters for Soft Deletion
        modelBuilder.Entity<Doctor>().HasQueryFilter(d => !d.IsDeleted);
        modelBuilder.Entity<Patient>().HasQueryFilter(p => !p.IsDeleted);
        modelBuilder.Entity<MedicalAppointment>().HasQueryFilter(a => !a.IsDeleted);
        modelBuilder.Entity<MedicalReport>().HasQueryFilter(r => !r.IsDeleted);

        // Unique index on Document (only for active records)
        modelBuilder.Entity<Doctor>()
            .HasIndex(d => d.Document)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        modelBuilder.Entity<Patient>()
            .HasIndex(p => p.Document)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // Unique index on Email for Users
        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // 1-to-1: One medical report per appointment
        modelBuilder.Entity<MedicalReport>()
            .HasIndex(r => r.AppointmentId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // Store enum as string in DB for readability
        modelBuilder.Entity<MedicalAppointment>()
            .Property(a => a.Status)
            .HasConversion<string>();

        // Cascade relationships
        modelBuilder.Entity<MedicalAppointment>()
            .HasOne(a => a.Patient)
            .WithMany(p => p.MedicalAppointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MedicalAppointment>()
            .HasOne(a => a.Doctor)
            .WithMany(d => d.MedicalAppointments)
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MedicalReport>()
            .HasOne(r => r.Appointment)
            .WithOne(a => a.MedicalReport)
            .HasForeignKey<MedicalReport>(r => r.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MedicalReport>()
            .HasOne(r => r.Patient)
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MedicalReport>()
            .HasOne(r => r.Doctor)
            .WithMany()
            .HasForeignKey(r => r.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Intercepts entity save operations to automatically set Audit and Soft Delete data.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndSoftDelete();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndSoftDelete();
        return base.SaveChanges();
    }

    private void ApplyAuditAndSoftDelete()
    {
        var currentUser = _currentUserService?.UserEmail ?? "System";
        var now = DateTime.UtcNow;

        // 1. Soft Delete interception
        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = now;
            }
        }

        // 2. Audit Trail interception
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                // Only set CreatedAt if it hasn't been explicitly assigned,
                // so the seeder can persist historical timestamps correctly.
                if (entry.Entity.CreatedAt == default)
                    entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy ??= currentUser;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = currentUser;
            }
        }
    }
}