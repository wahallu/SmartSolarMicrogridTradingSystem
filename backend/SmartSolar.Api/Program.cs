/* =============================================================================
 * File        : Program.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Application Bootstrap
 * Description : Configures the API services, security, middleware and routes.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-23
 * =============================================================================
 */

using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SmartSolar.Api.Common;
using SmartSolar.Api.Configuration;
using SmartSolar.Api.Data;
using SmartSolar.Api.Middleware;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Implementations;
using SmartSolar.Api.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// 1. Configuration binding - a mis-spelled key now fails here rather than deep
//    inside a service at request time.
// -----------------------------------------------------------------------------
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection(MongoDbSettings.SectionName));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<BusinessRuleSettings>(builder.Configuration.GetSection(BusinessRuleSettings.SectionName));
builder.Services.Configure<SeedDataSettings>(builder.Configuration.GetSection(SeedDataSettings.SectionName));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

// The signing secret must be long enough for HMAC-SHA256; refuse to start otherwise.
if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 32)
{
    throw new InvalidOperationException(
        "JwtSettings:Secret is missing or too short. Provide at least 32 characters in appsettings.json.");
}

// -----------------------------------------------------------------------------
// 2. Framework services
// -----------------------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // camelCase matches the contract and what both clients deserialise.
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // Enums are exchanged as their names ("Approved"), never as integers.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Model binding failures are returned in the same envelope as every other error,
// so the clients need exactly one error handling path.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToList());

        var response = ApiResponse<object>.Fail(
            "One or more validation errors occurred.",
            ErrorCodes.ValidationFailed,
            errors);

        return new BadRequestObjectResult(response);
    };
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();

// In short: Swagger is both the live API docs and the report's reference, so a
// bearer scheme is added to let a marker call the secured endpoints.
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Smart Solar Microgrid Trading System API",
        Version = "v1",
        Description = "FAT service for the SE4040 Enterprise Application Development assignment. "
                    + "All business logic lives here; the web and Android clients are UI only."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT returned by POST /api/v1/auth/login."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// -----------------------------------------------------------------------------
// 3. Authentication and authorisation
// -----------------------------------------------------------------------------
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Tokens are validated on every request; ClockSkew is zeroed so an expired
    // token is rejected immediately rather than up to five minutes later.
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero
    };

    // even a rejected token gets the same standard JSON envelope as
// everything else.
    options.Events = new JwtBearerEvents
    {
        OnChallenge = async context =>
        {
            context.HandleResponse();
            if (context.Response.HasStarted)
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Fail("Authentication is required to access this resource.", "UNAUTHENTICATED"));
        },
        OnForbidden = async context =>
        {
            if (context.Response.HasStarted)
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Fail("Your role does not permit this operation.", ErrorCodes.Forbidden));
        }
    };
});

builder.Services.AddAuthorization();

// -----------------------------------------------------------------------------
// 4. CORS - the React development server and the Android emulator both call in
//    from a different origin than the API itself.
// -----------------------------------------------------------------------------
const string CorsPolicy = "SmartSolarClients";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        // AllowAnyOrigin is acceptable here because the API is stateless and
        // authenticated by bearer token rather than by cookie.
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// -----------------------------------------------------------------------------
// 5. Application services - the FAT service layer
// -----------------------------------------------------------------------------
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProsumerService, ProsumerService>();
builder.Services.AddScoped<IStationService, StationService>();
builder.Services.AddScoped<ISlotService, SlotService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddScoped<BookingTimeDataNormalizer>();

var app = builder.Build();

// -----------------------------------------------------------------------------
// 6. HTTP pipeline. Order matters: exception handling must be outermost so it
//    can catch anything thrown further in.
// -----------------------------------------------------------------------------
app.UseExceptionHandlingMiddleware();

// The verb override MUST run before routing, otherwise routing has already rejected
// the POST with 405 by the time the header is read.
//
// Note the explicit UseRouting() below. WebApplication normally inserts UseRouting()
// automatically at the very START of the pipeline when endpoints are mapped, which
// would place it ahead of this middleware and defeat it. Calling UseRouting()
// explicitly suppresses that automatic insertion and pins routing to this position.
app.UseMethodOverride();
app.UseRouting();

// Swagger is left enabled outside Development as well, because the marker needs
// a way to exercise the API on the IIS deployment.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Smart Solar Microgrid API v1");
    options.DocumentTitle = "Smart Solar Microgrid Trading System - API";
});

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// A friendly root so hitting the site in a browser confirms the deployment.
app.MapGet("/", () => Results.Ok(new
{
    service = "Smart Solar Microgrid Trading System - Central Web API",
    version = "1.0.0",
    documentation = "/swagger",
    health = "/api/v1/health"
})).AllowAnonymous();

// -----------------------------------------------------------------------------
// 7. Start-up work: verify indexes, repair legacy booking timestamps, then seed.
// -----------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var context = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
        await context.EnsureIndexesAsync();

        // Older builds stored an entered 08:00 Sri Lankan slot as 08:00Z instead
        // of the real instant 02:30Z. Repair before seeding so any reservations
        // newly derived from existing slots copy already-corrected instants.
        var timeNormalizer = scope.ServiceProvider.GetRequiredService<BookingTimeDataNormalizer>();
        await timeNormalizer.NormalizeAsync();

        var seedSettings = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<SeedDataSettings>>().Value;

        if (seedSettings.Enabled)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
            await seeder.SeedAsync();
        }
        else
        {
            logger.LogInformation("Seeding is disabled by configuration.");
        }
    }
    catch (Exception ex)
    {
        // The API must still start when MongoDB is unreachable so that /health
        // can report the problem instead of the process simply dying.
        logger.LogError(ex, "Start-up initialisation failed. The API will run but the database may be unavailable.");
    }
}

app.Run();
