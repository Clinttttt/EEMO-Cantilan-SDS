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

            services.AddScoped<Common.Revenue.CollectionComposerWorkflow>();
            services.AddScoped<Common.Revenue.EcfCollectionWorkflow>();
            services.AddScoped<Common.Revenue.WcfCollectionWorkflow>();
            services.AddScoped<Common.Revenue.SettlementCutoverWorkflow>();
            services.AddScoped<Common.Revenue.WcfActivationWorkflow>();
            services.AddScoped<Common.Revenue.WcfMobileCollectionWorkflow>();
            services.AddScoped<Common.Revenue.AccountableFormCustodyWorkflow>();
            services.AddScoped<Common.Revenue.CollectorOperationAssignmentWorkflow>();
            services.AddScoped<Common.Revenue.GovernedServiceWorkflow>();
            services.AddScoped<Common.Revenue.PenaltyDefinitionWorkflow>();
            services.AddScoped<Common.Revenue.ObligationWorkflow>();
            services.AddScoped<Common.Revenue.VehicleClassWorkflow>();
            services.AddScoped<Common.Revenue.RemittanceWorkflow>();
            services.AddScoped<Common.Interface.Persistence.ICollectorCollectionFacts>(
                sp => sp.GetRequiredService<Common.Revenue.RemittanceWorkflow>());
            services.AddScoped<Common.Revenue.CollectionsReportWorkflow>();
            services.AddScoped<Common.Revenue.TransportationCollectionAuthority>();
            services.AddScoped<Common.Revenue.GovernedCanonicalAuthority>();
            services.AddScoped<Common.Revenue.NpmDailyCanonicalPoster>();
            services.AddScoped<Common.Revenue.FeeScheduleCollectionWorkflow>();
            services.AddScoped<Common.Slaughterhouse.ApprovedSlaughterAnimalWorkflow>();

            return services;
        }
    }
}
