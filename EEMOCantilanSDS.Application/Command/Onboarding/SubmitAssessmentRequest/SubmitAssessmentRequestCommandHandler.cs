using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SubmitAssessmentRequest
{
    public class SubmitAssessmentRequestCommandHandler(IAppDbContext context)
        : IRequestHandler<SubmitAssessmentRequestCommand, Result<AssessmentRequestDto>>
    {
        public async Task<Result<AssessmentRequestDto>> Handle(SubmitAssessmentRequestCommand request, CancellationToken ct)
        {
            var activeMunicipality = await context.Municipalities
                .AsNoTracking()
                .AnyAsync(m => m.Status == MunicipalityStatus.Active
                    && m.Name.Trim().ToUpper() == OnboardingPipelineGuard.Normalize(request.Municipality)
                    && m.Province.Trim().ToUpper() == OnboardingPipelineGuard.Normalize(request.Province), ct);
            if (activeMunicipality)
                return Result<AssessmentRequestDto>.Failure(
                    $"{request.Municipality.Trim()} is already active in StallTrack.", ResultStatus.Conflict);

            var existing = await OnboardingPipelineGuard.FindOtherActiveAsync(
                context, request.Municipality, request.Province, null, ct);
            if (existing is not null)
                return Result<AssessmentRequestDto>.Failure(
                    OnboardingPipelineGuard.DuplicateMessage(existing), ResultStatus.Conflict);

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
            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (OnboardingPipelineGuard.IsActivePipelineConstraintViolation(ex))
            {
                // The pre-check gives the useful stage-specific message in the normal path. The partial unique
                // index closes the race when two public submissions arrive together.
                return Result<AssessmentRequestDto>.Failure(
                    $"{request.Municipality.Trim()} already has an active onboarding request.", ResultStatus.Conflict);
            }

            return Result<AssessmentRequestDto>.Success(entity.ToDto());
        }
    }
}
