using EEMOCantilanSDS.Application.Behaviors;
using FluentValidation;
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
        /// <summary>
        /// Application's own registrations: validators, the MediatR pipeline, and the two settlement services.
        /// </summary>
        /// <remarks>
        /// Takes no configuration on purpose. It used to accept an <c>IConfiguration</c> it never read, which the MediatR
        /// lambda then shadowed with a parameter of the same name — so the file appeared to configure MediatR from app
        /// settings when it does nothing of the kind.
        /// </remarks>
        public static IServiceCollection AddApplicationService(this IServiceCollection services)
        {
            services.AddValidatorsFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
            services.AddMediatR(mediatr =>
            {
                mediatr.RegisterServicesFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
                mediatr.AddOpenBehavior(typeof(ValidationBehavior<,>));
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
