using Microsoft.Extensions.Configuration;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Infrastructure.Cursors;
using OpHalo.Keep.Infrastructure.Persistence;

namespace OpHalo.Api.Keep;

public static class KeepServiceCollectionExtensions
{
    public static IServiceCollection AddKeepServices(this IServiceCollection services)
    {
        services.AddScoped<IKeepIntakePersistence, KeepIntakePersistence>();
        services.AddScoped<IKeepIntakeSetupPersistence, KeepIntakeSetupPersistence>();
        services.AddScoped<IKeepSetupPersistence, EfKeepSetupPersistence>();
        services.AddScoped<IKeepProductOpsPersistence, EfKeepProductOpsPersistence>();
        services.AddScoped<IKeepSetupDeferralPersistence, EfKeepSetupDeferralPersistence>();
        services.AddScoped<KeepSetupService>();
        services.AddScoped<KeepOnboardingService>();
        services.AddScoped<KeepBusinessSetupService>();
        services.AddScoped<IKeepBusinessRequestPersistence, KeepBusinessRequestPersistence>();
        services.AddScoped<CreateBusinessRequestService>();
        services.AddScoped<LookupKeepRequestByPhoneService>();
        services.AddScoped<KeepIntakeSetupService>();
        services.AddScoped<IKeepRequestListPersistence, KeepRequestListPersistence>();
        services.AddScoped<IKeepRequestDetailPersistence, EfKeepRequestDetailPersistence>();
        services.AddScoped<IKeepRequestOperatePersistence, EfKeepRequestOperatePersistence>();
        services.AddScoped<IKeepSmsHandoffPersistence, EfKeepSmsHandoffPersistence>();
        services.AddScoped<IKeepIntakeSmsHandoffPersistence, EfKeepIntakeSmsHandoffPersistence>();
        services.AddScoped<KeepTokenService>();
        services.AddScoped<CreateKeepPublicIntakeService>();
        services.AddScoped<GetKeepRequestListService>();
        services.AddScoped<GetAvailableKeepRequestsService>();
        // Cursor signing key read lazily from IConfiguration so that WebApplicationFactory
        // overrides in ConfigureAppConfiguration are visible at scope-creation time.
        services.AddScoped<IKeepRequestListCursorProtector>(sp =>
        {
            var keyBase64 = sp.GetRequiredService<IConfiguration>()["Keep:RequestListCursorSigningKey"];
            if (string.IsNullOrWhiteSpace(keyBase64))
                throw new InvalidOperationException(
                    "Keep:RequestListCursorSigningKey is required. " +
                    "Supply it via user secrets, environment variable, or appsettings.");
            return new HmacKeepRequestListCursorProtector(Convert.FromBase64String(keyBase64));
        });
        services.AddScoped<GetKeepRequestDetailService>();
        services.AddScoped<GetKeepCustomerPageService>();
        services.AddScoped<ChangeKeepRequestStatusService>();
        services.AddScoped<ClassifyKeepRequestService>();
        services.AddScoped<AddBusinessUpdateService>();
        services.AddScoped<AddInternalNoteService>();
        services.AddScoped<AcknowledgeAttentionService>();
        services.AddScoped<LogExternalContactService>();
        services.AddScoped<ManageResponsibleService>();
        services.AddScoped<ManageWatcherService>();
        services.AddScoped<SelfWatchService>();
        services.AddScoped<MuteService>();
        services.AddScoped<MarkFeedbackReviewedService>();
        services.AddScoped<ManageRequestTimingService>();
        services.AddScoped<ClearShareIntentService>();
        services.AddScoped<CreateSmsHandoffService>();
        services.AddScoped<CreateIntakeSmsHandoffService>();
        services.AddScoped<UpdateServiceLocationService>();
        services.AddScoped<SetBusinessPriorityService>();
        services.AddScoped<GetParticipantCandidatesService>();
        services.AddScoped<KeepRequestParticipationService>();
        services.AddScoped<KeepPublicCustomerAccessGuard>();
        services.AddScoped<AddCustomerMessageService>();
        services.AddScoped<SubmitFeedbackService>();
        services.AddScoped<IKeepCustomerWritePersistence, EfKeepCustomerWritePersistence>();
        services.AddScoped<GetBadgeCountService>();
        services.AddScoped<IKeepBadgePersistence, EfKeepBadgePersistence>();
        services.AddSingleton<KeepPushCandidateService>();
        services.AddScoped<IKeepPushNotifier, KeepPushNotifier>();

        return services;
    }
}
