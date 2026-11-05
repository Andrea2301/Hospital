using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using HospitalSV.Data;
using HospitalSV.Middleware;
using HospitalSV.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Harden Kestrel Server (Prevent Server header leakage)
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

// Behind a load balancer (ALB, CloudFront, reverse proxy) the TCP peer is the
// proxy. Trust X-Forwarded-For/Proto so rate limiting keys on the real client
// IP and HTTPS detection keeps working.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Database Connection Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

//  JWT Authentication 
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret  = jwtSection["Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSection["Issuer"],
            ValidAudience            = jwtSection["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.Zero // No tolerance window — tokens expire exactly when set
        };
    });

builder.Services.AddAuthorization();

//  Rate Limiting (OWASP Brute-Force & Flooding Defense) 
builder.Services.AddRateLimiter(options =>
{
    // Global policy: 120 requests per minute per IP.
    // CORS preflights (OPTIONS) are not limited: they are browser bookkeeping,
    // not user actions, and limiting them breaks every API call on the SPA.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (HttpMethods.IsOptions(context.Request.Method))
            return RateLimitPartition.GetNoLimiter<string>("preflight");

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0
            }
        );
    });

    // Strict policy for auth endpoints: 5 requests per minute (brute-force protection)
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit          = 5;
        opt.Window               = TimeSpan.FromMinutes(1);
        opt.QueueLimit           = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Sandbox entry is password-less but still rate limited to avoid abuse.
    options.AddFixedWindowLimiter("demo", opt =>
    {
        opt.PermitLimit          = 30;
        opt.Window               = TimeSpan.FromMinutes(1);
        opt.QueueLimit           = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"status\":429,\"message\":\"Too many requests. Please wait before trying again.\"}",
            cancellationToken: token
        );
    };
});

//  CORS Configuration
// Origins come from Cors:AllowedOrigins so the same image can serve local
// development, the sandbox and any future production domain.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))
            .WithExposedHeaders("X-Total-Count", "X-Page", "X-Page-Size", "X-Correlation-ID")
    );
});

//  Services & Validation Response Envelope 
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPdfReportService, PdfReportService>();
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

//  Public Demo Sandbox 
// When Demo:Enabled is true the API seeds a fictional clinic, exposes a
// password-less login, disables registration/email and resets on a schedule.
var demoOptions = builder.Configuration
    .GetSection(DemoOptions.SectionName)
    .Get<DemoOptions>() ?? new DemoOptions();

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
builder.Services.AddSingleton<DemoState>();
builder.Services.AddScoped<DemoResetService>();
if (demoOptions.Enabled)
    builder.Services.AddHostedService<DemoResetHostedService>();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Standardized enterprise 400 Bad Request error format
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(err => err.ErrorMessage).ToArray()
                );

            var correlationId = context.HttpContext.Items.TryGetValue("X-Correlation-ID", out var cid)
                ? cid?.ToString()
                : context.HttpContext.TraceIdentifier;

            var response = new
            {
                status = StatusCodes.Status400BadRequest,
                title = "Validation Failed",
                message = "One or more validation errors occurred.",
                errors = errors,
                correlationId = correlationId,
                traceId = context.HttpContext.TraceIdentifier,
                timestamp = DateTime.UtcNow
            };

            return new BadRequestObjectResult(response);
        };
    });

// Swagger / OpenAPI 
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Hospital  - Enterprise API",
        Version     = "v1.1",
        Description = "Enterprise RESTful API for Hospital. Includes role-based access control, security headers, rate limiting, and auditable data transactions.",
        Contact = new OpenApiContact
        {
            Name = "Hospital  Engineering",
            Email = "support@hospital.com"
        }
    });

    // Include XML comments for Swagger documentation
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    // JWT Security Scheme
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.ApiKey,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Enter 'Bearer' [space] and then your valid JWT token.\nExample: \"Bearer eyJhbGci...\""
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

//  Build the application
var app = builder.Build();


//  Custom Enterprise Middleware Pipeline 
app.UseForwardedHeaders();        // Resolve client IP/proto when behind ALB or a proxy
app.UseCorrelationId();           // Attach X-Correlation-ID to all requests/responses
app.UseSecurityHeaders();         // Apply OWASP security headers (no-sniff, frame-options, etc.)
app.UseGlobalExceptionHandling(); // Centralized exception safety & SQL error masking

//  Swagger Documentation (development only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Hospital API v1.1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "Hospital  - API Documentation";
    });

    app.MapGet("/", () => Results.Redirect("/swagger"));
}
else
{
    // Keep the public surface minimal in production.
    app.MapGet("/", () => Results.Ok(new
    {
        service = "HospitalSV API",
        status  = "ok",
        health  = "/health"
    }));
}

app.UseHttpsRedirection();
app.UseCors("AllowAngular");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Lightweight liveness probe for load balancers (ALB, ECS, etc.)
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    utc    = DateTime.UtcNow
}));

//  Apply Migrations & Seed Initial Database Data 
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var config  = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger  = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await context.Database.MigrateAsync(app.Lifetime.ApplicationStopping);

        if (demoOptions.Enabled)
        {
            logger.LogWarning(
                "Demo sandbox is ENABLED: registration and email delivery are disabled, " +
                "and clinical data will be reset every {Hours} hour(s).",
                Math.Max(1, demoOptions.ResetIntervalHours));

            var seeded = await DemoSeeder.EnsureSeededAsync(context, app.Lifetime.ApplicationStopping);
            logger.LogInformation(seeded
                ? "Demo sandbox seeded with fictional clinic data."
                : "Demo sandbox already contains data; keeping it until the next scheduled reset.");
        }
        else
        {
            await DbInitializer.SeedAsync(context, config, app.Lifetime.ApplicationStopping);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Could not apply database migrations or seed data on startup. Verify database connectivity.");
    }
}

app.Run();