using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetPaymentSettings;

/// <summary>Returns the caller's LGU online-payment account status (never the secret).</summary>
public record GetMunicipalityPaymentSettingsQuery : IRequest<Result<PaymentSettingsDto>>;
