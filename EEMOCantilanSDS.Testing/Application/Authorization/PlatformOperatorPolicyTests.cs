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
/// </summary>
public class PlatformOperatorPolicyTests
{
    [Fact]
    public void ADedicatedOperatorIsTheOperator_WhateverTenantOrRoleTheyHold()
    {
        // The intended mechanism: an operator belonging to no LGU and holding no municipal office. This is the case
        // the API policy and the activation handler refused.
        Assert.True(PlatformOperatorPolicy.IsOperator(isDedicatedOperator: true, role: null, isDefaultTenant: false));
        Assert.True(PlatformOperatorPolicy.IsOperator(isDedicatedOperator: true, role: "Admin", isDefaultTenant: false));
    }

    [Fact]
    public void TheDefaultTenantsSuperAdminIsStillAccepted()
    {
        // The documented fallback. Removing it would lock the office out of its own onboarding before a dedicated
        // account exists.
        Assert.True(PlatformOperatorPolicy.IsOperator(false, "SuperAdmin", isDefaultTenant: true));
        Assert.True(PlatformOperatorPolicy.IsOperator(false, "superadmin", isDefaultTenant: true));
    }

    [Fact]
    public void AnotherLgusHeadIsNeverTheOperator()
    {
        // The whole point of the rule. Onboarding, activation, backup and restore reach across every LGU in the shared
        // database, so a municipal Head must never hold them.
        Assert.False(PlatformOperatorPolicy.IsOperator(false, "SuperAdmin", isDefaultTenant: false));
    }

    [Fact]
    public void ALesserRoleInTheDefaultTenantIsNotTheOperator()
    {
        Assert.False(PlatformOperatorPolicy.IsOperator(false, "Admin", isDefaultTenant: true));
        Assert.False(PlatformOperatorPolicy.IsOperator(false, "Collector", isDefaultTenant: true));
        Assert.False(PlatformOperatorPolicy.IsOperator(false, null, isDefaultTenant: true));
    }
}
