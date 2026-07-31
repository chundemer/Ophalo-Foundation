using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <inheritdoc cref="IAccountFeatureAccessResolver"/>
public sealed class AccountFeatureAccessResolver(
    IFeatureAccessPolicy featurePolicy,
    IAccountCapabilityPackageEnrollmentPersistence enrollmentPersistence)
    : IAccountFeatureAccessResolver
{
    public async Task<bool> IsEnabledAsync(
        Guid accountId,
        AccountFeatureAccessContext? context,
        string featureKey,
        CancellationToken cancellationToken)
    {
        if (context is null)
            return false;

        if (featurePolicy.IsEnabled(context.Plan, featureKey))
            return true;

        var enrollment = await enrollmentPersistence.GetByAccountAndFeatureKeyAsync(
            accountId, featureKey, cancellationToken);

        return enrollment is not null && enrollment.Status == CapabilityEnrollmentStatus.Enrolled;
    }
}
