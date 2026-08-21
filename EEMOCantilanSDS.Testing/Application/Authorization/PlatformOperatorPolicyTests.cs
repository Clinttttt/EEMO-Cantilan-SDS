using EEMOCantilanSDS.Application.Common.Authorization;

namespace EEMOCantilanSDS.Testing.Application.Authorization;

/// <summary>
/// Who the platform operator is.
///
/// <para>
/// The rule was stated in three places — the API's policy, the Application guard, and an inlined copy in the activation
/// handler — and they disagreed. A dedicated operator account was accepted by the guard and refused by the other two,
/// so it could approve an LGU's onboarding and then fail to activate it. These tests pin the single rule down so a
/// fourth opinion cannot appear quietly.
/// </para>
///
/// <para>
/// The rule also used to accept the DEFAULT municipality's Head, from when that municipality was the only one on the
/// platform. That made one municipality's Head the operator over all of them, carrying a restore of the whole shared
/// database and the approval of other municipalities' onboarding. A dedicated operator account now exists, which is
/// the condition the clause was written to be deleted on, so nothing but the flag qualifies.
/// </para>
/// </summary>
public class PlatformOperatorPolicyTests
{
    [Fact]
    public void ADedicatedOperatorIsTheOperator()
    {
        // The one mechanism: an account belonging to no LGU and holding no municipal office.
        Assert.True(PlatformOperatorPolicy.IsOperator(isDedicatedOperator: true));
    }

    [Fact]
    public void NobodyWithoutTheFlagIsTheOperator()
    {
        // Including the default municipality's Head, which is the clause this platform retired. Onboarding,
        // activation, backup and restore reach across every LGU in the shared database, so no municipal officer
        // holds them — the office asked for exactly this.
        Assert.False(PlatformOperatorPolicy.IsOperator(isDedicatedOperator: false));
    }

    [Fact]
    public void TheRuleReadsNothingButTheFlag()
    {
        // Stated as a property rather than a case list: the decision cannot depend on a role or a municipality,
        // because it takes neither. A future clause about either would have to change this signature, which is the
        // point — the last one was added quietly and outlived its reason.
        Assert.Equal(1, typeof(PlatformOperatorPolicy)
            .GetMethod(nameof(PlatformOperatorPolicy.IsOperator))!
            .GetParameters().Length);
    }
}
