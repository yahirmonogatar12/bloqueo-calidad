using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using QualityLock.Api.Auth;
using QualityLock.Api.Middleware;
using QualityLock.Application.Configuration;
using QualityLock.Infrastructure.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Permite correr como Servicio de Windows (no-op si se ejecuta desde consola).
// Tambien fija el content root al directorio del ejecutable, de modo que la config y
// los logs se resuelven junto al .exe y no en System32 cuando lo arranca el SCM.
builder.Host.UseWindowsService(options => options.ServiceName = "QualityLockApi");

// Ruta de logs ABSOLUTA junto al ejecutable: como servicio de Windows el CWD es
// System32, asi que una ruta relativa escribiria (o fallaria) en el lugar equivocado.
var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "qualitylock-api-.log");

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var connectionString = builder.Configuration.GetConnectionString("MySQL");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Connection string 'MySQL' is not configured. Set ConnectionStrings__MySQL " +
        "as an environment variable or in appsettings.Development.json (see appsettings.json.example).");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHealthChecks();

// Persistir las claves de DataProtection en una carpeta fija junto al ejecutable.
// Como servicio (LocalSystem) el perfil de usuario por defecto no es fiable; esto evita
// que la inicializacion de DataProtection falle al arrancar.
var keysDir = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "keys"));
keysDir.Create();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(keysDir)
    .SetApplicationName("QualityLock");

// ── Auth / JWT ──────────────────────────────────────────────
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<ClientAuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<ITokenService, TokenService>();

// AdminAccessOptions lives in the Application layer (sin dependencia de Microsoft.Extensions.Options),
// así que se registra como instancia concreta resuelta de la sección "Admin".
var adminAccess = builder.Configuration.GetSection("Admin").Get<AdminAccessOptions>() ?? new AdminAccessOptions();
builder.Services.AddSingleton(adminAccess);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or too short (>= 32 bytes required). " +
        "Set Jwt__SigningKey as an environment variable or in appsettings.Development.json.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// Secure by default: every endpoint requires a valid token unless it opts out
// with [AllowAnonymous] (auth/token, health).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();

// Serilog ANTES del ExceptionMiddleware: asi registra el codigo HTTP final (ej. 404)
// que produce el handler, en vez de ver la excepcion cruda como 500.
app.UseSerilogRequestLogging(opts =>
{
    opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        if (correlationId is not null)
            diagnosticContext.Set("CorrelationId", correlationId);
    };
});

app.UseMiddleware<ExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }
