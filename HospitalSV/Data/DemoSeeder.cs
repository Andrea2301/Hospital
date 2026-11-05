using HospitalSV.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalSV.Data;

/// <summary>
/// Fictional clinic used by the public sandbox. Every reset wipes the
/// business tables and recreates a small but realistic dataset, so the
/// dashboard, agenda and reports never look empty.
/// </summary>
public static class DemoSeeder
{
    private const string SeedAuthor = "DemoSeeder";

    private static readonly (string FullName, string Email, string Role)[] DemoUsers =
    {
        ("Dr. Camila Ospino (Demo Admin)", "admin@hospital.com",      "Admin"),
        ("Dra. Laura Gómez",               "doctor.laura@hospital.com", "Doctor"),
        ("Mariana Silva",                   "reception@hospital.com",   "Receptionist")
    };

    /// <summary>
    /// Seeds the sandbox only when the database has no clinical data, so a
    /// restart does not erase what visitors are testing. Returns true when
    /// the seed actually ran.
    /// </summary>
    public static async Task<bool> EnsureSeededAsync(AppDbContext context, CancellationToken ct = default)
    {
        var hasDoctors = await context.Doctors.IgnoreQueryFilters().AnyAsync(ct);
        if (hasDoctors)
            return false;

        await ResetAsync(context, ct);
        return true;
    }

    /// <summary>
    /// Deletes all clinical data and demo accounts, then recreates them.
    /// Must run inside a transaction when called from a request.
    /// </summary>
    public static async Task ResetAsync(AppDbContext context, CancellationToken ct = default)
    {
        context.ChangeTracker.Clear();

        // Reports first: their FKs to patients/doctors are Restrict.
        await context.MedicalReports.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        await context.MedicalAppointments.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        await context.Patients.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        await context.Doctors.IgnoreQueryFilters().ExecuteDeleteAsync(ct);

        await ResetUsersAsync(context, ct);

        var doctors = await SeedDoctorsAsync(context, ct);
        var patients = await SeedPatientsAsync(context, ct);
        var appointments = await SeedAppointmentsAsync(context, doctors, patients, ct);
        await SeedReportsAsync(context, appointments, ct);
    }

    private static async Task ResetUsersAsync(AppDbContext context, CancellationToken ct)
    {
        var keep = DemoUsers.Select(u => u.Email).ToArray();

        // Remove accounts created by visitors so the sandbox starts clean.
        await context.Users
            .Where(u => !keep.Contains(u.Email))
            .ExecuteDeleteAsync(ct);

        var existing = await context.Users
            .Where(u => keep.Contains(u.Email))
            .ToListAsync(ct);

        foreach (var seed in DemoUsers)
        {
            var user = existing.FirstOrDefault(u => u.Email == seed.Email);
            if (user is null)
            {
                context.Users.Add(new AppUser
                {
                    FullName     = seed.FullName,
                    Email        = seed.Email,
                    // The password is random and never exposed: demo entry
                    // always goes through POST /api/auth/demo.
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N") + "Aa1!"),
                    Role         = seed.Role,
                    IsActive     = true,
                    CreatedBy    = SeedAuthor
                });
                continue;
            }

            // A visitor may have deactivated or demoted the demo accounts.
            user.FullName = seed.FullName;
            user.Role     = seed.Role;
            user.IsActive = true;
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task<List<Doctor>> SeedDoctorsAsync(AppDbContext context, CancellationToken ct)
    {
        var doctors = new List<Doctor>
        {
            new() { Name = "Laura Gómez",             Document = "DOC-101", Specialty = "Cardiología",                 Phone = "+57 300 123 4567", Email = "doctor.laura@hospital.com" },
            new() { Name = "Andrés Felipe Rojas",     Document = "DOC-102", Specialty = "Neurología",                  Phone = "+57 301 234 5678", Email = "andres.rojas@hospital.com" },
            new() { Name = "Valeria Restrepo",        Document = "DOC-103", Specialty = "Pediatría",                   Phone = "+57 310 345 6789", Email = "valeria.restrepo@hospital.com" },
            new() { Name = "Mateo Gutiérrez",         Document = "DOC-104", Specialty = "Ortopedia y Traumatología",   Phone = "+57 312 456 7890", Email = "mateo.gutierrez@hospital.com" },
            new() { Name = "Sofía Castellanos",       Document = "DOC-105", Specialty = "Medicina Interna",            Phone = "+57 315 567 8901", Email = "sofia.castellanos@hospital.com" }
        };

        foreach (var doctor in doctors)
            doctor.CreatedBy = SeedAuthor;

        context.Doctors.AddRange(doctors);
        await context.SaveChangesAsync(ct);
        return doctors;
    }

    private static async Task<List<Patient>> SeedPatientsAsync(AppDbContext context, CancellationToken ct)
    {
        var patients = new List<Patient>
        {
            new() { FullName = "Camila Andrea Ospino",  Document = "1000538798", Phone = "+57 320 987 6543", Email = "camila.ospino@example.com" },
            new() { FullName = "Juan Pablo Morales",    Document = "71234567",   Phone = "+57 311 876 5432", Email = "juan.morales@example.com" },
            new() { FullName = "María Fernanda López",  Document = "1020304050", Phone = "+57 314 765 4321", Email = "maria.lopez@example.com" },
            new() { FullName = "Gabriel Antonio Torres",Document = "43567890",   Phone = "+57 316 654 3210", Email = "gabriel.torres@example.com" },
            new() { FullName = "Lucía Martínez Ríos",   Document = "1152345678", Phone = "+57 318 543 2109", Email = "lucia.martinez@example.com" },
            new() { FullName = "Daniel Esteban Vargas", Document = "1098765432", Phone = "+57 319 432 1098", Email = "daniel.vargas@example.com" },
            new() { FullName = "Paula Andrea Cifuentes",Document = "1012345678", Phone = "+57 313 321 0987", Email = "paula.cifuentes@example.com" },
            new() { FullName = "Ricardo José Molina",   Document = "79123456",   Phone = "+57 317 210 9876", Email = "ricardo.molina@example.com" }
        };

        foreach (var patient in patients)
            patient.CreatedBy = SeedAuthor;

        context.Patients.AddRange(patients);
        await context.SaveChangesAsync(ct);
        return patients;
    }

    private static async Task<List<MedicalAppointment>> SeedAppointmentsAsync(
        AppDbContext context,
        IReadOnlyList<Doctor> doctors,
        IReadOnlyList<Patient> patients,
        CancellationToken ct)
    {
        // Indices reference the doctor/patient lists above.
        var plan = new (int Doctor, int Patient, int DayOffset, int Hour, int Minute, AppointmentStatus Status)[]
        {
            (0, 0, -6,  9,  0, AppointmentStatus.Attended),
            (1, 1, -5, 10,  0, AppointmentStatus.Attended),
            (2, 2, -4, 11,  0, AppointmentStatus.Attended),
            (3, 3, -3,  9, 30, AppointmentStatus.Attended),
            (4, 4, -2, 15,  0, AppointmentStatus.Attended),
            (0, 5, -1,  8, 30, AppointmentStatus.Attended),
            (1, 6,  0,  9,  0, AppointmentStatus.Pending),
            (2, 7,  0, 11, 30, AppointmentStatus.Pending),
            (0, 0,  1,  9,  0, AppointmentStatus.Pending),
            (1, 3,  1, 10, 30, AppointmentStatus.Pending),
            (2, 4,  1, 14,  0, AppointmentStatus.Pending),
            (3, 5,  2,  8, 30, AppointmentStatus.Pending),
            (4, 6,  2, 10,  0, AppointmentStatus.Pending),
            (0, 2,  3,  9, 30, AppointmentStatus.Pending),
            (1, 7,  3, 15, 30, AppointmentStatus.Pending),
            (3, 1, -2, 11,  0, AppointmentStatus.Cancelled),
            (4, 2,  4, 12,  0, AppointmentStatus.Cancelled)
        };

        var today = DateTime.UtcNow.Date;
        var now   = DateTime.UtcNow;
        var appointments = new List<MedicalAppointment>(plan.Length);

        foreach (var slot in plan)
        {
            var date = today.AddDays(slot.DayOffset)
                .AddHours(slot.Hour)
                .AddMinutes(slot.Minute);

            var appointment = new MedicalAppointment
            {
                Date      = date,
                Status    = slot.Status,
                PatientId = patients[slot.Patient].Id,
                DoctorId  = doctors[slot.Doctor].Id,
                CreatedAt = date < now ? date.AddHours(-6) : now.AddDays(-1),
                CreatedBy = SeedAuthor
            };

            context.MedicalAppointments.Add(appointment);
            appointments.Add(appointment);
        }

        await context.SaveChangesAsync(ct);
        return appointments;
    }

    private static async Task SeedReportsAsync(
        AppDbContext context,
        IReadOnlyList<MedicalAppointment> appointments,
        CancellationToken ct)
    {
        // One report per attended encounter (indices 0-5 above).
        var reports = new[]
        {
            new MedicalReport
            {
                AppointmentId        = appointments[0].Id,
                PatientId            = appointments[0].PatientId,
                DoctorId             = appointments[0].DoctorId,
                ReasonForVisit       = "Chest tightness and palpitations after exercise.",
                PhysicalExamination  = "Alert, oriented, no distress. Regular rhythm, no murmurs. Lungs clear.",
                BloodPressure        = "128/82 mmHg",
                HeartRate            = 78,
                Temperature          = 36.7m,
                WeightKg             = 68.5m,
                HeightCm             = 165m,
                Diagnosis            = "Stable angina — suspected",
                Treatment            = "Resting ECG ordered. Start ASA 100 mg daily. Avoid strenuous exercise until reviewed. Cardiology follow-up in 2 weeks.",
                Notes                = "Patient reports two episodes in the last month, both relieved by rest.",
                CreatedAt            = appointments[0].Date.AddHours(1),
                CreatedBy            = "Dra. Laura Gómez"
            },
            new MedicalReport
            {
                AppointmentId        = appointments[1].Id,
                PatientId            = appointments[1].PatientId,
                DoctorId             = appointments[1].DoctorId,
                ReasonForVisit       = "Recurring migraines with photophobia and nausea.",
                PhysicalExamination  = "Neurological exam normal. No focal deficits. Mild neck stiffness.",
                BloodPressure        = "118/76 mmHg",
                HeartRate            = 72,
                Temperature          = 36.5m,
                WeightKg             = 80.2m,
                HeightCm             = 178m,
                Diagnosis            = "Migraine without aura",
                Treatment            = "Sumatriptan 50 mg at onset, maximum 2 doses per day. Trigger diary. Neurology follow-up in 4 weeks.",
                Notes                = "Sleep deprivation identified as the main trigger.",
                CreatedAt            = appointments[1].Date.AddHours(1),
                CreatedBy            = "Dr. Andrés Felipe Rojas"
            },
            new MedicalReport
            {
                AppointmentId        = appointments[2].Id,
                PatientId            = appointments[2].PatientId,
                DoctorId             = appointments[2].DoctorId,
                ReasonForVisit       = "Fever and sore throat for three days.",
                PhysicalExamination  = "Pharyngeal erythema without exudate. Tympanic membranes normal. No lymphadenopathy.",
                BloodPressure        = "100/65 mmHg",
                HeartRate            = 96,
                Temperature          = 38.2m,
                WeightKg             = 24.8m,
                HeightCm             = 128m,
                Diagnosis            = "Acute pharyngitis, viral",
                Treatment            = "Hydration and rest. Acetaminophen 15 mg/kg every 6 hours as needed. Review if fever persists beyond 48 hours.",
                Notes                = "Mother instructed on warning signs. Rapid strep test negative.",
                CreatedAt            = appointments[2].Date.AddHours(1),
                CreatedBy            = "Dra. Valeria Restrepo"
            }
        };

        context.MedicalReports.AddRange(reports);
        await context.SaveChangesAsync(ct);
    }
}
