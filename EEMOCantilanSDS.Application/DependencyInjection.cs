using EEMOCantilanSDS.Application.Behaviors;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationService(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddValidatorsFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
            services.AddMediatR(configuration =>
            {
                configuration.RegisterServicesFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
                configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            });
  
            services.AddAutoMapper(cfg =>
            {
                cfg.AddProfile<AutomapperProfile>();
            });

            services.AddScoped<
                Common.Interface.Services.IOnlinePaymentSettlementService,
                Common.Payments.OnlinePaymentSettlementService>();

            services.AddScoped<
                Common.Payments.INpmMonthSettlementService,
                Common.Payments.NpmMonthSettlementService>();

            return services;
        }
    }
}
