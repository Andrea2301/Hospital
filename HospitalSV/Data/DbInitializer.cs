using HospitalSV.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HospitalSV.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext context, IConfiguration config, CancellationToken ct = default)
    {
        // ─── Read seed passwords from configuration (never hardcode secrets) ─────
        var adminPwd = config["Seed:AdminPassword"]
            ?? throw new InvalidOperationException("Seed:AdminPassword is not configured. Add it to appsettings.Development.json or an environment variable.");
        var doctorPwd = config["Seed:DoctorPassword"]
            ?? throw new InvalidOperationException("Seed:DoctorPassword is not configured.");
        var receptionistPwd = config["Seed:ReceptionistPassword"]
            ?? throw new InvalidOperationException("Seed:ReceptionistPassword is not configured.");

        // ─── 1. Seed Users (Admin, Doctor, Receptionist) ─────────────────────────
        var seedUsers = new (string FullName, string Email, string Password, string Role)[]
        {
            ("Dr. Carlos Mendoza (Admin)", "admin@hospital.com",      adminPwd,         "Admin"),
            ("Dra. Laura Gómez",           "doctor.laura@hospital.com", doctorPwd,       "Doctor"),
            ("Mariana Silva",               "reception@hospital.com",   receptionistPwd, "Receptionist")
        };

        foreach (var u in seedUsers)
        {
            var exists = await context.Users.AnyAsync(x => x.Email == u.Email, ct);
            if (!exists)
            {
                context.Users.Add(new AppUser
                {
                    FullName     = u.FullName,
                    Email        = u.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(u.Password),
                    Role         = u.Role,
                    IsActive     = true,
                    CreatedBy    = "SystemSeeder"
                });
            }
        }
        await context.SaveChangesAsync(ct);

        // ─── 2. Seed Doctors ────────────────────────────────────────────────────
        var seedDoctors = new (string Name, string Document, string Specialty, string Phone, string Email)[]
        {
            ("Laura Gómez", "DOC-101", "Cardiología", "+57 300 123 4567", "doctor.laura@hospital.com"),
            ("Andrés Felipe Rojas", "DOC-102", "Neurología", "+57 301 234 5678", "andres.rojas@hospital.com"),
            ("Valeria Restrepo", "DOC-103", "Pediatría", "+57 310 345 6789", "valeria.restrepo@hospital.com"),
            ("Mateo Gutiérrez", "DOC-104", "Ortopedia y Traumatología", "+57 312 456 7890", "mateo.gutierrez@hospital.com"),
            ("Sofía Castellanos", "DOC-105", "Medicina Interna", "+57 315 567 8901", "sofia.castellanos@hospital.com")
        };

        foreach (var d in seedDoctors)
        {
            var exists = await context.Doctors.AnyAsync(x => x.Document == d.Document, ct);
            if (!exists)
            {
                context.Doctors.Add(new Doctor
                {
                    Name      = d.Name,
                    Document  = d.Document,
                    Specialty = d.Specialty,
                    Phone     = d.Phone,
                    Email     = d.Email,
                    CreatedBy = "SystemSeeder"
                });
            }
        }
        await context.SaveChangesAsync(ct);

        // ─── 3. Seed Patients ───────────────────────────────────────────────────
        var seedPatients = new (string FullName, string Document, string Phone, string Email)[]
        {
            ("Camila Andrea Ospino", "1000538798", "+57 320 987 6543", "camila.ospino@hospital.com"),
            ("Juan Pablo Morales", "71234567", "+57 311 876 5432", "juan.morales@hospital.com"),
            ("María Fernanda López", "1020304050", "+57 314 765 4321", "maria.lopez@hospital.com"),
            ("Gabriel Antonio Torres", "43567890", "+57 316 654 3210", "gabriel.torres@hospital.com"),
            ("Lucía Martínez Ríos", "1152345678", "+57 318 543 2109", "lucia.martinez@hospital.com")
        };

        foreach (var p in seedPatients)
        {
            var exists = await context.Patients.AnyAsync(x => x.Document == p.Document, ct);
            if (!exists)
            {
                context.Patients.Add(new Patient
                {
                    FullName  = p.FullName,
                    Document  = p.Document,
                    Phone     = p.Phone,
                    Email     = p.Email,
                    CreatedBy = "SystemSeeder"
                });
            }
        }
        await context.SaveChangesAsync(ct);

        // ─── 4. Seed Medical Appointments ───────────────────────────────────────
        // Load doctors and patients ordered by Id to get consistent indices
        var docs = await context.Doctors.OrderBy(d => d.Id).Take(5).ToListAsync(ct);
        var pats = await context.Patients.OrderBy(p => p.Id).Take(5).ToListAsync(ct);

        if (docs.Count >= 2 && pats.Count >= 2)
        {
            var now = DateTime.UtcNow;

            // Each appointment is identified by the unique combination of PatientId + DoctorId + Date.
            // This makes the check truly idempotent regardless of how many rows exist in the table.
            var seedAppointments = new[]
            {
                new
                {
                    PatientId = pats[0].Id,
                    DoctorId  = docs[0].Id,
                    Date      = now.AddDays(-1).Date.AddHours(9),
                    Status    = AppointmentStatus.Attended,
                    CreatedAt = now.AddDays(-3)
                },
                new
                {
                    PatientId = pats[1].Id,
                    DoctorId  = docs[1].Id,
                    Date      = now.AddDays(-1).Date.AddHours(10), // Bug fix: yesterday, not today — status Attended
                    Status    = AppointmentStatus.Attended,
                    CreatedAt = now.AddDays(-2)
                },
                new
                {
                    PatientId = pats[pats.Count > 2 ? 2 : 0].Id,
                    DoctorId  = docs[docs.Count > 2 ? 2 : 0].Id,
                    Date      = now.AddDays(1).Date.AddHours(10).AddMinutes(30),
                    Status    = AppointmentStatus.Pending,
                    CreatedAt = now.AddDays(-1)
                },
                new
                {
                    PatientId = pats[pats.Count > 3 ? 3 : 1].Id,
                    DoctorId  = docs[docs.Count > 3 ? 3 : 1].Id,
                    Date      = now.AddDays(1).Date.AddHours(14),
                    Status    = AppointmentStatus.Pending,
                    CreatedAt = now
                },
                new
                {
                    PatientId = pats[pats.Count > 4 ? 4 : 0].Id,
                    DoctorId  = docs[docs.Count > 4 ? 4 : 0].Id,
                    Date      = now.AddDays(3).Date.AddHours(11),
                    Status    = AppointmentStatus.Pending,
                    CreatedAt = now
                }
            };

            foreach (var a in seedAppointments)
            {
                // Idempotency check: skip if the exact same slot already exists
                var exists = await context.MedicalAppointments
                    .AnyAsync(x => x.PatientId == a.PatientId
                               && x.DoctorId  == a.DoctorId
                               && x.Date      == a.Date, ct);

                if (!exists)
                {
                    context.MedicalAppointments.Add(new MedicalAppointment
                    {
                        PatientId = a.PatientId,
                        DoctorId  = a.DoctorId,
                        Date      = a.Date,
                        Status    = a.Status,
                        CreatedAt = a.CreatedAt,   // preserved by the audit fix in AppDbContext
                        CreatedBy = "SystemSeeder"
                    });
                }
            }

            await context.SaveChangesAsync(ct);
        }
    }
}
