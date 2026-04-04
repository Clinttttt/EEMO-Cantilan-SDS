using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection service, IConfiguration configuration)
        {
            service.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
            });
            service.AddScoped<IAppDbContext, AppDbContext>();
            service.AddScoped<IUnitOfWork, UnitOfWork>();
            service.AddScoped<ITokenService, TokenService>();
            
            // Repositories
            service.AddScoped<IAuthRepository, AuthRepository>();
            service.AddScoped<ISetupRepository, SetupRepository>();
            service.AddScoped<ICollectorRepository, CollectorRepository>();
            service.AddScoped<IStallRepository, StallRepository>();
            service.AddScoped<IFacilityRepository, FacilityRepository>();
            service.AddScoped<IPaymentRepository, PaymentRepository>();
            
            return service;

        }
    }
}
