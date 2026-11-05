# HospitalSV

A full-stack clinical scheduling system for small hospitals and clinics: staff and
patient records, conflict-free appointment booking, medical reports with PDF
export, transactional email, and a public sandbox that anyone can explore without
registering.

> **Live demo:** _coming soon_ — the sandbox will be deployed on AWS Free Tier.

**Frontend repository:** 

---

## What it does

| Area | Capability |
| --- | --- |
| Appointments | Booking with **doctor double-booking detection**, reschedule, cancel, mark as attended, soft delete |
| Doctors | Roster with specialty, document, contact details and account linking |
| Patients | Admission records, contact details, soft delete, search and pagination |
| Medical reports | Reason for visit, vitals (BP, heart rate, temperature, weight, height), physical exam, diagnosis, treatment, notes, finalization, **PDF export** |
| Email | Confirmation, cancellation and rescheduling emails with an `.ics` calendar attachment; manual resend from the UI; SMTP failures never block the operation |
| Staff & roles | Admin-managed accounts, role changes, enable/disable, password rules |
| Dashboard | Live counts, today's agenda and recent activity tailored to the signed-in role |
| Public sandbox | One-click role login, fictional data, scheduled reset, visible demo banner |

### Roles

| Role | Access |
| --- | --- |
| **Admin** | Everything: staff accounts and roles, doctors, patients, appointments, reports |
| **Doctor** | Own agenda, patients, medical reports |
| **Receptionist** | Patients, bookings, cancellations |

The API enforces the same rules with `[Authorize(Roles = ...)]`, so hiding a button
is never the only protection.

---

## Architecture

```
┌──────────────────────────┐        HTTPS/JSON        ┌──────────────────────────┐
│  Angular 22 SPA          │ ───────────────────────► │  ASP.NET Core 8 Web API  │
│  standalone + signals    │ ◄─────────────────────── │  JWT · EF Core 8         │
│  S3 + CloudFront         │                          │  ECS Express (1 task)    │
└──────────────────────────┘                          └────────────┬─────────────┘
                                                                   │ Npgsql / SSL
                                                                   ▼
                                                      ┌──────────────────────────┐
                                                      │  PostgreSQL (Aiven)      │
                                                      └──────────────────────────┘
                                                                   │
                                                                   ▼
                                                      ┌──────────────────────────┐
                                                      │  SMTP (Gmail, port 587)  │
                                                      └──────────────────────────┘
```

The backend is stateless apart from the in-memory demo reset cooldown, which is why
the deployment intentionally runs **one task**. Migrations and seed data run at
startup.

### Backend

- **ASP.NET Core 8** Web API with controllers, async/await and DTOs
- **Entity Framework Core 8** + **PostgreSQL** (Npgsql), migrations applied on boot
- **JWT** authentication and **BCrypt** password hashing
- Soft delete + audit fields (`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `DeletedAt`)
- **Rate limiting**: 120 req/min per IP globally, 5 req/min on auth, 30 req/min on sandbox entry
- Security headers, correlation IDs, global exception handling with SQL error masking
- Swagger UI available **only in Development**
- `GET /health` liveness probe for load balancers

### Frontend

- **Angular 22** standalone components and signals
- Custom design system — no Bootstrap, no component library
- Responsive layout, accessible focus states, reduced-motion support
- Production build budgeted and tree-shaken (~116 kB transferred for the initial bundle)

---

## The public sandbox

The sandbox is a single shared instance, not multi-tenant. When `Demo:Enabled=true`:

1. On first boot the database is seeded with a **fictional clinic**: 5 doctors,
   8 patients, 17 appointments and 3 medical reports.
2. The login page shows three role cards (Administrator, Doctor, Receptionist).
   Clicking one logs in without a password.
3. Registration is blocked and email delivery is disabled.
4. Data is reset **every 24 hours**, and visitors can trigger a manual reset with a
   **10-minute cooldown**. Resetting signs the visitor out.
5. A sticky banner makes it obvious that the data is fictional, and the API sends
   `X-Robots-Tag: noindex, nofollow`.

No passwords are shipped in the frontend bundle. The API selects the demo account
server-side and issues a normal JWT with a `demo=true` claim.

---

## Tech stack

| Layer | Technology |
| --- | --- |
| Frontend | Angular 22, TypeScript 6, RxJS, custom CSS design system |
| Backend | .NET 8, ASP.NET Core Web API, EF Core 8 |
| Database | PostgreSQL 16 (Aiven) |
| Auth | JWT Bearer + BCrypt |
| PDF | PdfSharpCore |
| Email | SMTP + MIME with `.ics` attachment |
| Container | Docker, multi-stage build, non-root `app` user |
| Hosting | AWS ECS Express Mode + ECR, S3 + CloudFront |

---

## Local setup

### Prerequisites

- .NET 8 SDK
- Node.js 20+ and npm
- PostgreSQL 14+ (local, Docker, or a free Aiven instance)

### 1. Database

Create a database and keep the connection string. For a quick local instance:

```bash
docker run --name hospitalsv-db -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=hospitalsv -p 5432:5432 -d postgres:16
```

### 2. Backend

```bash
git clone https://github.com/Andrea2301/Hospital
cd hospitalsv-api

dotnet user-secrets init --project HospitalSV
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=hospitalsv;Username=postgres;Password=postgres" \
  --project HospitalSV
dotnet user-secrets set "Jwt:Secret" "<64+ random characters>" --project HospitalSV

dotnet run --project HospitalSV --urls http://localhost:5000
```

- API: `http://localhost:5000/api`
- Swagger: `http://localhost:5000/swagger`
- Health: `http://localhost:5000/health`

Migrations and the initial admin account are created automatically.

### 3. Frontend

```bash
git clone 
cd hospitalsv-frontend
npm install
npm start
```

Open `http://localhost:4200`. The dev server reads `src/environments/environment.ts`,
which points at `http://localhost:5000/api`.

### 4. Sign in

| Account | Email | Password |
| --- | --- | --- |
| Admin | `admin@hospital.com` | from `Seed:AdminPassword` |
| Doctor | `doctor.laura@hospital.com` | from `Seed:DoctorPassword` |
| Receptionist | `reception@hospital.com` | from `Seed:ReceptionistPassword` |

The demo passwords in `appsettings.json` are placeholders on purpose. Set real ones
through user secrets or environment variables.

---

## Configuration

All settings can be supplied as environment variables. In .NET, `__` maps to `:`.

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt__Secret` | Signing key (64+ random characters) |
| `Jwt__Issuer` / `Jwt__Audience` / `Jwt__ExpiresInMinutes` | Token settings |
| `Cors__AllowedOrigins__0`, `__1`, … | Allowed frontend origins (no wildcards) |
| `Demo__Enabled` | Turns the public sandbox on/off |
| `Demo__ResetIntervalHours` | Automatic reset interval (default `24`) |
| `Demo__ManualResetCooldownMinutes` | Cooldown between manual resets (default `10`) |
| `Email__Enabled` | Enables SMTP delivery |
| `Email__Host` / `Port` / `EnableSsl` | SMTP endpoint |
| `Email__Username` / `Password` / `FromEmail` / `FromName` | Sender credentials |
| `Seed__AdminPassword` / `DoctorPassword` / `ReceptionistPassword` | Initial accounts (non-demo only) |

`appsettings.json` contains **placeholders only**. Real secrets belong in user
secrets locally and in the platform's environment variables when deployed.

See [`.env.example`](.env.example) for a complete template.

---

## API overview

| Method | Route | Roles |
| --- | --- | --- |
| `POST` | `/api/auth/login` | public (rate limited) |
| `POST` | `/api/auth/register` | public; disabled in demo |
| `POST` | `/api/auth/demo` | public; demo only |
| `GET` | `/api/doctors` | authenticated |
| `POST` `PUT` `DELETE` | `/api/doctors/{id}` | Admin |
| `GET` | `/api/patients` | authenticated |
| `POST` `PUT` | `/api/patients` | Admin, Receptionist |
| `DELETE` | `/api/patients/{id}` | Admin |
| `GET` | `/api/medicalappointments` | authenticated |
| `POST` `PUT` `DELETE` | `/api/medicalappointments` | Admin, Receptionist |
| `PATCH` | `/api/medicalappointments/{id}/cancel` | all staff |
| `PATCH` | `/api/medicalappointments/{id}/attend` | Admin, Doctor |
| `POST` | `/api/medicalappointments/{id}/send-confirmation` | all staff |
| `GET` | `/api/medicalreports` | Admin, Doctor |
| `POST` `PUT` | `/api/medicalreports` | Admin, Doctor |
| `GET` | `/api/medicalreports/{id}/pdf` | Admin, Doctor |
| `GET` | `/api/users` | Admin |
| `PATCH` | `/api/users/{id}/toggle-status` | Admin |
| `GET` | `/api/demo/status` | public |
| `POST` | `/api/demo/reset` | authenticated; demo only |

Errors use a consistent envelope with `status`, `message`, `correlationId` and
`timestamp`. Rate-limited requests return `429` with a JSON body.

---

## Security notes

- Secret scanning: `appsettings.json` is placeholder-only; environment-specific
  files are gitignored.
- Passwords are hashed with BCrypt and never returned by the API.
- JWT validation includes issuer, audience, lifetime and signing key.
- CORS is an explicit allowlist configured per environment.
- Swagger and stack traces are not exposed in Production.
- Rate limiting is partitioned by forwarded client IP behind the load balancer.
- Soft-deleted rows are filtered out by EF global query filters.
- The sandbox disables registration and email so it cannot send mail to real
  addresses or be used to create persistent accounts.

If you fork this project, rotate every credential before deploying and never reuse
the development database.

---

## License

Portfolio project. Not intended for real clinical use without an independent
security, privacy and regulatory review.
