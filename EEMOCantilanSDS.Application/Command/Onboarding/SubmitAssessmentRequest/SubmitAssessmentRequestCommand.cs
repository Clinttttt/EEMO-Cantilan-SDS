using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SubmitAssessmentRequest
{
    /// <summary>
    /// Public (anonymous) submission of an LGU assessment request — Stage 1 of onboarding. Creates an inert,
    /// pre-LGU review record; it writes no live LGU data and cannot affect any active municipality.
    /// </summary>
    public record SubmitAssessmentRequestCommand(
        string Municipality,
        string Province,
        string RequestingOffice,
        string FocalPerson,
        string Position,
        string OfficialEmail,
        string ContactNumber,
        string FacilitiesManaged,
        string? ApproxVendors,
        string? AuthorizationStatus,
        bool Acknowledged,
        string? Notes) : IRequest<Result<AssessmentRequestDto>>;
}
