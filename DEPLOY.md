# Deploying HospitalSV API on AWS

This is the shortest path that matches the stack used for the portfolio: a
container image on **ECS Express Mode**, PostgreSQL on **Aiven**, and the
Angular build on **S3 + CloudFront**.

The API is stateless except for the in-memory demo reset cooldown, so keep the
ECS service at **one task**. Migrations and the sandbox seed run on startup.

---

## 0. Rotate secrets before the first push

The original development repository contained real credentials in its history.
Do **not** reuse that history. Before deploying:

1. Rotate the Aiven database password.
2. Generate a new JWT secret (64+ random characters).
3. Confirm `HospitalSV/appsettings.Development.json` is not committed.
4. Create a fresh repository (or a clean orphan branch) and push only that.

Useful generators:

```powershell
# JWT secret
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))

# Database password, if you are recreating the Aiven user
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
```

---

## 1. Database: Aiven PostgreSQL (free plan)

1. Create a PostgreSQL service in Aiven.
2. Copy the **Service URI** and add `SslMode=Require;Trust Server Certificate=true;`.
3. Keep the service running while the API boots: migrations and seed run at
   startup.
4. Do not use an IP allowlist for Fargate: tasks get dynamic public IPs. Require
   SSL and use a strong password instead.

Connection string shape:

```
Host=HOST;Port=PORT;Database=defaultdb;Username=avnadmin;Password=PASSWORD;SslMode=Require;Trust Server Certificate=true;
```

---

## 2. API: ECR + ECS Express Mode

### 2.1 Build and push the image

```bash
aws ecr create-repository --repository-name hospitalsv-api --region us-east-1

aws ecr get-login-password --region us-east-1 \
  | docker login --username AWS --password-stdin ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com

docker build -t hospitalsv-api .
docker tag hospitalsv-api:latest ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/hospitalsv-api:latest
docker push ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/hospitalsv-api:latest
```

The `Dockerfile` publishes to `/app`, listens on **8080**, and runs as the
non-root `app` user.

### 2.2 Create the ECS Express service

In the ECS console choose **Express Mode** and point it at the ECR image.

| Setting | Value |
| --- | --- |
| Container port | `8080` |
| Health check path | `/health` |
| Desired count | `1` |
| Min / max tasks | `1` / `1` |
| CPU / memory | `0.5 vCPU` / `1 GB` is enough for the sandbox |
| Networking | Public subnets of the default VPC (avoids the ~$32/month NAT Gateway) |

### 2.3 Environment variables

Set these in the ECS service (never in the image):

| Variable | Example / notes |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://+:8080` |
| `ConnectionStrings__DefaultConnection` | Aiven connection string |
| `Jwt__Secret` | fresh 64+ character secret |
| `Jwt__Issuer` | `Hospital` |
| `Jwt__Audience` | `Hospital` |
| `Jwt__ExpiresInMinutes` | `60` |
| `Cors__AllowedOrigins__0` | `https://your-cloudfront-domain` |
| `Demo__Enabled` | `true` for the public sandbox, `false` otherwise |
| `Demo__ResetIntervalHours` | `24` |
| `Demo__ManualResetCooldownMinutes` | `10` |
| `Email__Enabled` | `false` in the sandbox |

For a real (non-demo) deployment also set `Seed__AdminPassword`,
`Seed__DoctorPassword`, `Seed__ReceptionistPassword`, and the `Email__*` values.

> In .NET configuration, `__` maps to `:`. Arrays use an index:
> `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, …

### 2.4 After the first deploy

```bash
curl https://YOUR-API-URL/health
curl https://YOUR-API-URL/api/demo/status
```

`/swagger` is intentionally disabled in Production.

---

## 3. Frontend: S3 + CloudFront

1. Edit `src/environments/environment.prod.ts` and set the real API URL
   (it must include `/api`).
2. Build:

```bash
npm ci
npm run build
```

3. Create a private S3 bucket and upload the browser output:

```bash
aws s3 sync dist/hospital-frontend/browser s3://YOUR-BUCKET --delete
```

4. Create a CloudFront distribution with the bucket as origin
   (Origin Access Control enabled).
5. Add a **custom error response** so Angular routing works on refresh:

| HTTP error | Response page | Response code |
| --- | --- | --- |
| `403` | `/index.html` | `200` |
| `404` | `/index.html` | `200` |

6. Add the CloudFront URL to `Cors__AllowedOrigins__0` and redeploy/restart the
   API task. Without this the browser silently blocks every request.

### Cache behavior

- `index.html`: no cache (`CachingDisabled`) or a very short TTL.
- `*.js`, `*.css`, fonts: long cache (`CachingOptimized`); filenames are hashed.

---

## 4. Smoke test checklist

- [ ] `GET /health` returns `200`.
- [ ] Landing page loads and shows **Try live demo** only when `Demo__Enabled=true`.
- [ ] One click enters the dashboard; the sandbox banner is visible.
- [ ] Appointments, patients, doctors and reports contain seeded data.
- [ ] Creating/editing a patient or appointment works and survives a refresh.
- [ ] Reset demo data logs the visitor out and restores the seed.
- [ ] Register route redirects to login in demo mode.
- [ ] Refresh on `/dashboard` and `/reports` does not 403/404.
- [ ] Browser console has no CORS errors.

---

## 5. Costs and teardown

The $100 free credits cover a small demo for a while, but ECS Express includes
an ALB and Fargate tasks. Check **AWS Budgets** with an alert at $1–5.

When the credits run out, the same image runs on **Lightsail Containers**
(~$7/month) with the same environment variables; only the public URL changes.

To stop spending quickly:

1. Delete the ECS Express service.
2. Delete the ECR repository (images are billed by GB-month).
3. Empty and delete the S3 bucket, disable/delete the CloudFront distribution.
4. Pause or delete the Aiven service.

---

## 6. Common failures

| Symptom | Cause | Fix |
| --- | --- | --- |
| `503` on demo entry | Seed accounts missing | Let the API finish booting; check logs; reset demo data |
| CORS error in browser | CloudFront URL missing from `Cors__AllowedOrigins__0` | Add it and restart the task |
| `404` on `/health` | Health check path configured as `/` | Use `/health` |
| Refresh on a route returns 403/404 | CloudFront SPA fallback missing | Add the two custom error responses |
| Startup crash | Missing `Jwt__Secret` or connection string | Check ECS environment variables |
| Migrations collide | More than one task starts at once | Keep desired count = 1 |
| Everyone rate-limited at once | Proxy headers not forwarded | `UseForwardedHeaders` is enabled; ensure the ALB sends `X-Forwarded-For` |
