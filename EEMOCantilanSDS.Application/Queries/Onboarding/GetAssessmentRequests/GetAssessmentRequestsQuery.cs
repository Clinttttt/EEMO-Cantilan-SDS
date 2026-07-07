using System.Collections.Generic;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Onboarding.GetAssessmentRequests
{
    /// <summary>Operator (platform-operator only) list of all assessment requests, newest first.</summary>
    public record GetAssessmentRequestsQuery() : IRequest<Result<IReadOnlyList<AssessmentRequestDto>>>;
}
