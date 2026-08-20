using Microsoft.Extensions.Configuration;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Application.PriceBook;
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
        services.AddScoped<IKeepAccountTimeZoneLookup, EfKeepAccountTimeZoneLookup>();
        services.AddScoped<IKeepSmsHandoffPersistence, EfKeepSmsHandoffPersistence>();
        services.AddScoped<IKeepIntakeSmsHandoffPersistence, EfKeepIntakeSmsHandoffPersistence>();
        services.AddScoped<IKeepCallHandoffPersistence, EfKeepCallHandoffPersistence>();
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
        services.AddScoped<GetKeepRequestRelatedWorkService>();
        services.AddScoped<GetKeepCustomerPageService>();
        services.AddScoped<ChangeKeepRequestStatusService>();
        services.AddScoped<ClassifyKeepRequestService>();
        services.AddScoped<AddBusinessUpdateService>();
        services.AddScoped<AddInternalNoteService>();
        services.AddScoped<AcknowledgeAttentionService>();
        services.AddScoped<LogExternalContactService>();
        services.AddScoped<PrepareUpdateNotificationService>();
        services.AddScoped<ConfirmUpdateNotificationService>();
        services.AddScoped<ManageResponsibleService>();
        services.AddScoped<ManageWatcherService>();
        services.AddScoped<SelfWatchService>();
        services.AddScoped<MuteService>();
        services.AddScoped<MarkFeedbackReviewedService>();
        services.AddScoped<ManageRequestTimingService>();
        services.AddScoped<ClearShareIntentService>();
        services.AddScoped<CreateSmsHandoffService>();
        services.AddScoped<CreateIntakeSmsHandoffService>();
        services.AddScoped<CreateCallHandoffService>();
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

        // Price Book, Quotes & Materials — catalog items (Session 2a.1/2a.2)
        services.AddScoped<ICatalogItemPersistence, EfCatalogItemPersistence>();
        services.AddScoped<CatalogItemLifecycleService>();
        services.AddScoped<CatalogItemApiService>();

        // Price Book, Quotes & Materials — direct-entry publish (Session 2d.2, ADR-470). The
        // separate 2d.1c per-entity adapters (IPriceBookAccountStatePersistence,
        // IPriceBookVersionPersistence, IManualPriceOverridePersistence) are not registered —
        // this is the only path that writes those tables, and it owns their whole transaction.
        services.AddScoped<IPriceBookPublishPersistence, EfPriceBookPublishPersistence>();
        services.AddScoped<PriceBookPublishService>();

        // Price Book, Quotes & Materials — atomic Save & activate (build-log/112, Session 2e.2).
        // The sole item-creation path exposed in Session 2e; shares the ADR-470 account lock and
        // VersionNumber sequence with the publish path above.
        services.AddScoped<ICatalogItemCreateAndActivatePersistence, EfCatalogItemCreateAndActivatePersistence>();
        services.AddScoped<CatalogItemCreateAndActivateService>();

        // Price Book, Quotes & Materials — catalog categories (Session 2b.1/2b.3)
        services.AddScoped<ICatalogCategoryPersistence, EfCatalogCategoryPersistence>();
        services.AddScoped<CatalogCategoryLifecycleService>();
        services.AddScoped<CatalogCategoryApiService>();

        // Price Book, Quotes & Materials — bounded catalog reads (Session 2e.3, build-log/113).
        // Reuses IKeepRequestListCursorProtector (registered above) — its HMAC signing is generic
        // string signing with no KeepRequest-specific payload knowledge.
        services.AddScoped<ICatalogReadPersistence, EfCatalogReadPersistence>();
        services.AddScoped<CatalogReadApiService>();

        // Price Book, Quotes & Materials — offering/assembly office management (Session 3.2a.1).
        services.AddScoped<IOfferingAssemblyPersistence, EfOfferingAssemblyPersistence>();
        services.AddScoped<OfferingAssemblyLifecycleService>();
        services.AddScoped<OfferingAssemblyApiService>();

        // Price Book, Quotes & Materials — offering/assembly bounded reads (Session 3.2a.2).
        // Reuses IKeepRequestListCursorProtector (registered above).
        services.AddScoped<OfferingAssemblyReadApiService>();

        // Price Book, Quotes & Materials — proposed-scope create/edit/submit API (Session 3.3b).
        // IProposedScopePersistence/IProposedScopeSubmissionPersistence/SubmitProposedScopeService
        // were introduced in 3.3a but had no caller until this batch wired them up.
        services.AddScoped<IProposedScopePersistence, EfProposedScopePersistence>();
        services.AddScoped<IProposedScopeSubmissionPersistence, EfProposedScopeSubmissionPersistence>();
        services.AddScoped<SubmitProposedScopeService>();
        services.AddScoped<CreateProposedScopeService>();
        services.AddScoped<EditProposedScopeService>();
        services.AddScoped<ProposedScopeApiService>();

        // Price Book, Quotes & Materials — proposed-scope read API (Session 3.4a).
        services.AddScoped<ProposedScopeReadApiService>();

        // Price Book, Quotes & Materials — field-safe catalog read API (Session 3.4b). Reuses
        // ICatalogReadPersistence/IKeepRequestListCursorProtector (registered above).
        services.AddScoped<FieldCatalogReadApiService>();

        // Price Book, Quotes & Materials — field-safe assembly read API (Session 3.4c). Reuses
        // IOfferingAssemblyPersistence/IKeepRequestListCursorProtector (registered above).
        services.AddScoped<FieldOfferingAssemblyReadApiService>();

        // Price Book, Quotes & Materials — polymorphic field-scope search (build-log/121, ADR-486).
        // Reuses ICatalogReadPersistence/IOfferingAssemblyPersistence/IKeepRequestListCursorProtector
        // (registered above).
        services.AddScoped<FieldScopeSearchApiService>();

        // Price Book, Quotes & Materials — server-authoritative field-select (Session 3.4d). Reuses
        // EditProposedScopeService/ICatalogReadPersistence (registered above).
        services.AddScoped<FieldProposedScopeSelectionApiService>();

        // Price Book, Quotes & Materials — atomic expand-assembly (Session 3.4e). Reuses
        // EditProposedScopeService/IOfferingAssemblyPersistence (registered above).
        services.AddScoped<IOfferingAssemblyExpansionPersistence, EfOfferingAssemblyExpansionPersistence>();
        services.AddScoped<FieldExpandAssemblyApiService>();

        // Price Book, Quotes & Materials — Quick scope action configuration + field read (Session
        // 3, build-log/119). IQuickScopeActionPersistence was added in Session 2 with no consumer
        // until this batch. Reuses ICatalogReadPersistence/IOfferingAssemblyPersistence (registered
        // above) to resolve each configured slot's target display name and current eligibility.
        services.AddScoped<IQuickScopeActionPersistence, EfQuickScopeActionPersistence>();
        services.AddScoped<QuickScopeActionConfigApiService>();
        services.AddScoped<QuickScopeActionFieldReadApiService>();

        // Price Book, Quotes & Materials — Paired Nudges Owner/Admin rule configuration (Session 2,
        // build-log/123). IScopeNudgeRulePersistence was added in Session 1 with no consumer until
        // this batch. Reuses ICatalogReadPersistence/IOfferingAssemblyPersistence (registered above)
        // for existence checks and display/eligibility resolution.
        services.AddScoped<IScopeNudgeRulePersistence, EfScopeNudgeRulePersistence>();
        services.AddScoped<ScopeNudgeRuleConfigApiService>();

        // Price Book, Quotes & Materials — Paired Nudges field nudge-read (Session 3,
        // build-log/123). Reuses IProposedScopePersistence/IScopeNudgeRulePersistence/
        // ICatalogReadPersistence/IOfferingAssemblyPersistence (all registered above).
        services.AddScoped<ScopeNudgeFieldReadApiService>();

        // Direct Actual Work — draft create/edit/discard (ADR-487, build-log/129, Batch 3).
        // IActualWorkPersistence was added in Batch 2 with no consumer until this batch. Reuses
        // ICatalogReadPersistence (registered above) to resolve a field-selected catalog item's
        // current Price Book snapshot; IActiveResponsibleCheck is the new reusable row-authorization
        // primitive shared by every draft mutation.
        services.AddScoped<IActualWorkPersistence, EfActualWorkPersistence>();
        services.AddScoped<IActiveResponsibleCheck, ActiveResponsibleCheck>();
        services.AddScoped<ActualWorkDraftApiService>();

        return services;
    }
}
