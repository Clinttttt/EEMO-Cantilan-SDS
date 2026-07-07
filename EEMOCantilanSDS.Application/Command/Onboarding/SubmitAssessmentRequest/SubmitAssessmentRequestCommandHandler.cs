using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SubmitAssessmentRequest
{
    public class SubmitAssessmentRequestCommandHandler(IAppDbContext context)
        : IRequestHandler<SubmitAssessmentRequestCommand, Result<AssessmentRequestDto>>
    {
        public async Task<Result<AssessmentRequestDto>> Handle(SubmitAssessmentRequestCommand request, CancellationToken ct)
        {
            var entity = AssessmentRequest.Create(
                request.Municipality,
                request.Province,
                request.RequestingOffice,
                request.FocalPerson,
                request.Position,
                request.OfficialEmail,
                request.ContactNumber,
                request.FacilitiesManaged,
                request.ApproxVendors,
                request.AuthorizationStatus,
                request.Acknowledged,
                request.Notes);

            context.AssessmentRequests.Add(entity);
            await context.SaveChangesAsync(ct);

            return Result<AssessmentRequestDto>.Success(entity.ToDto());
        }
    }
}
