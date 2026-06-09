using Microsoft.Extensions.DependencyInjection;
using QualityLock.Application.Interfaces;
using QualityLock.Application.Services;
using QualityLock.Infrastructure.Database;
using QualityLock.Infrastructure.Repositories;

namespace QualityLock.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(_ => new MySqlConnectionFactory(connectionString));

        services.AddScoped<IOperatorRepository, OperatorRepository>();
        services.AddScoped<ISystemUserRepository, SystemUserRepository>();
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IAdminOverrideRepository, AdminOverrideRepository>();
        services.AddScoped<IHeartbeatRepository, HeartbeatRepository>();

        services.AddScoped<IBadgeValidationService, BadgeValidationService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IAdminOverrideService, AdminOverrideService>();
        services.AddScoped<IHeartbeatService, HeartbeatService>();
        services.AddScoped<IStationBootstrapService, StationBootstrapService>();
        services.AddScoped<IStationRegistrationService, StationRegistrationService>();
        services.AddScoped<IAdminAuthService, AdminAuthService>();

        return services;
    }
}
