using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
namespace EEMOCantilanSDS.Testing;
public sealed class FineAssessmentDomainTests
{
    [Fact] public void UncappedAmountRequiresGoverningEvidence() {
        var tenant=Guid.NewGuid(); var actor=Guid.NewGuid(); var now=DateTime.UtcNow;
        var penalty=PenaltyDefinition.Create(tenant,"APPROVED",new(2026,1,1),"Approved",null,GovernedServiceBasis.DirectApprovedAmount,null,null,true,"head");
        var policy=FineAssessmentPolicy.Create(tenant,penalty.Id,FineAccountKind.Kanmanggay,null,"Actual authority",FinePenaltyDateBasis.AssessmentDate,false,true,actor,now);
        Assert.Throws<ArgumentException>(()=>FineAssessment.Create(tenant,FineAccountKind.Kanmanggay,Guid.NewGuid(),policy,penalty,"Occupant","4","KM",
            new(2026,10,1),new(2026,10,10),"Case 1","Incident",100m,Guid.NewGuid(),new string('A',64),actor,now,"Evidence"));
    }
    [Fact] public void PolicyCannotPretendSpaceIsFacilityAndRequiresReference() {
        Assert.Throws<ArgumentException>(()=>FineAssessmentPolicy.Create(Guid.NewGuid(),Guid.NewGuid(),FineAccountKind.Kanmanggay,FacilityCode.ICE,"Ref",
            FinePenaltyDateBasis.AssessmentDate,false,false,Guid.NewGuid(),DateTime.UtcNow));
        Assert.Throws<ArgumentException>(()=>FineAssessmentPolicy.Create(Guid.NewGuid(),Guid.NewGuid(),FineAccountKind.Kanmanggay,null,"",
            FinePenaltyDateBasis.AssessmentDate,false,false,Guid.NewGuid(),DateTime.UtcNow));
    }
}
