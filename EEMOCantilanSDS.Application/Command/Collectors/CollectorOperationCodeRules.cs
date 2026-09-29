using System.Linq.Expressions;
using EEMOCantilanSDS.Domain.Constants;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Collectors;

/// <summary>
/// The one validation rule for operation codes submitted with a collector account: exact supported permission codes
/// from the fixed catalog, each at most once. Revenue classifications and excluded identities (Weight &amp; Measure,
/// standalone Fish/Meat vendor fee) are not operation codes.
/// </summary>
internal static class CollectorOperationCodeRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Expression<Func<T, IEnumerable<string>?>> codes)
    {
        validator.RuleForEach(codes!)
            .Must(CollectorOperationCodes.IsSupported)
            .WithMessage("One or more operation codes are not supported.");
        validator.RuleFor(codes)
            .Must(list => list is null || list.Distinct(StringComparer.Ordinal).Count() == list.Count())
            .WithMessage("An operation was selected more than once.");
    }
}
