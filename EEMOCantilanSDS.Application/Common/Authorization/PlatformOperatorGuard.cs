using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Authorization
{
    /// <summary>
    /// Determines whether the current caller is the <b>platform operator</b> — an account carrying the
    /// <c>IsPlatformOperator</c> flag. Onboarding, activation and the whole-database tools are system-owner
    /// actions, so no municipality's Head can perform them, including the default municipality's.
    /// </summary>
    public static class PlatformOperatorGuard
    {
        /// <summary>
        /// True only for a dedicated platform/console operator account (the <c>IsPlatformOperator</c> flag).
        ///
        /// <para>
        /// Kept as its own method, distinct from <see cref="IsCurrentAsync"/>, because callers ask two different
        /// questions of it. Some ask "may this caller act at all", and some ask "does this caller see every
        /// municipality" — the two-factor recovery tool is open to a Head for their own office's staff, and only an
        /// operator reaches across offices.
        /// </para>
        /// </summary>
        public static async Task<bool> IsDedicatedOperatorAsync(IAppDbContext context, ICurrentUserService currentUser, CancellationToken ct)
        {
            if (currentUser.UserId is not Guid userId) return false;

            var isOperator = await context.AdminUsers
                .IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .Select(u => (bool?)u.IsPlatformOperator)
                .FirstOrDefaultAsync(ct);

            return isOperator == true;
        }

        /// <summary>
        /// Whether the caller may act as the platform operator.
        ///
        /// <para>
        /// The decision itself lives in <see cref="PlatformOperatorPolicy"/>, so the API's authorization policy and
        /// this guard cannot drift apart. They differ only in where the fact comes from: a claim there, the database
        /// here. This used to also read the caller's municipality, to accept the default one's Head; that fallback
        /// is gone, so the municipality no longer bears on the question and is no longer read.
        /// </para>
        /// </summary>
        public static async Task<bool> IsCurrentAsync(IAppDbContext context, ICurrentUserService currentUser, CancellationToken ct)
            => PlatformOperatorPolicy.IsOperator(await IsDedicatedOperatorAsync(context, currentUser, ct));
    }
}
