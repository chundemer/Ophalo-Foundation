export type AccountRole = "owner" | "admin" | "operator" | "viewer" | "unknown";

export interface MeResponse {
  accountUserId: string;
  accountId: string;
  isAuthenticated: boolean;
  isVerified: boolean;
  accountRole: AccountRole;
  businessName: string | null;
}

export interface OnboardingChecklist {
  profileAndContactSaved: boolean;
  timezoneSaved: boolean;
  policySaved: boolean;
  intakeLinkActive: boolean;
  operatorInvited: boolean;
  mobileDeviceRegistered: boolean;
  firstRequestCreated: boolean;
  quickCaptureExerciseDone: boolean;
  trackerReviewDone: boolean;
  spamClassificationExplained: boolean;
}

export interface KeepSetupPolicyResult {
  firstResponseTargetMinutes: number;
  standardResponseTargetMinutes: number;
  priorityResponseTargetMinutes: number;
  statusCheckThresholdDays: number;
}

export interface KeepSetupResult {
  businessName: string;
  timeZone: string;
  customerFacingPhone: string | null;
  customerFacingEmail: string | null;
  logoUrl: string | null;
  websiteUrl: string | null;
  responsePolicy: KeepSetupPolicyResult;
}

export interface SeatUsage {
  occupiedSeats: number;
  maxSeats: number;
  atLimit: boolean;
  limitApplies: boolean;
}

export interface MemberItem {
  accountUserId: string;
  email: string;
  role: string;
  status: string;
  isCurrentUser: boolean;
  isPrimaryOwner: boolean;
  activatedAtUtc: string | null;
  inviteExpiresAtUtc: string | null;
}

export interface ListMembersResponse {
  members: MemberItem[];
  seatUsage: SeatUsage;
}

export interface KeepBusinessSetupResult {
  businessInfoComplete: boolean;
  addFirstRequestComplete: boolean;
  reviewCustomerPageComplete: boolean;
  createIntakePageComplete: boolean;
  shareIntakePageComplete: boolean;
  buildTeamComplete: boolean;
  useMobileComplete: boolean;
  deferredSteps: number[];
  intendedTeamSize: number | null;
}

export interface IntakeStatusResult {
  hasActiveLink: boolean;
  publicSlug: string | null;
  createdAtUtc: string | null;
}

export interface IntakeEnsureResult {
  created: boolean;
  rawToken: string | null;
  publicSlug: string | null;
}

export interface IntakeReplaceResult {
  rawToken: string;
  publicSlug: string;
  staleLinksWarning: boolean;
}

export interface IntakeRenameLinkResult {
  publicSlug: string;
}

export interface CreateIntakeSmsHandoffResult {
  handoffUrl: string;
  customerPhone: string;
  messageBody: string;
  expiresAtUtc: string;
}

export interface PhoneLookupCustomer {
  name: string;
  phone: string;
  email: string | null;
}

export interface PhoneLookupActiveRequest {
  requestId: string;
  referenceCode: string;
  status: string;
  description: string;
  lastActivityAtUtc: string | null;
}

export interface PhoneLookupPrefill {
  name: string;
  email: string | null;
}

export interface PhoneLookupResult {
  customer: PhoneLookupCustomer | null;
  prefill: PhoneLookupPrefill | null;
  activeRequests: PhoneLookupActiveRequest[];
  hasMoreActiveRequests: boolean;
}

export interface CreateRequestBody {
  customerName: string;
  customerPhone: string;
  customerEmail?: string;
  description: string;
  source: string;
  serviceAddressLine1?: string;
  serviceAddressLine2?: string;
  serviceCity?: string;
  serviceState?: string;
  serviceZip?: string;
}

export interface AvailableActionsMetadata {
  canChangeStatus: boolean;
  canSendBusinessUpdate: boolean;
  canAddInternalNote: boolean;
  canAcknowledgeAttention: boolean;
  canLogExternalContact: boolean;
  canAssignResponsible: boolean;
  canWatch: boolean;
  canUnwatch: boolean;
  canMute: boolean;
  canUnmute: boolean;
  canMarkFeedbackReviewed: boolean;
  canSetFollowUpOn: boolean;
  canSetPlannedFor: boolean;
  canClose: boolean;
  canClassify: boolean;
  canRecordShareIntent: boolean;
  canCreateFollowUpRequest: boolean;
  allowedStatuses: string[];
}

export interface ValidationHintsMetadata {
  businessUpdateMaxLength: number;
  internalNoteMaxLength: number;
  statusMessageMaxLength: number;
  acknowledgeReasonMaxLength: number;
  externalContactSummaryMaxLength: number;
  feedbackReviewNoteMaxLength: number;
  followUpNoteMaxLength: number;
  allowedFollowUpReasons: string[];
  messageRequiredForStatuses: string[];
}

export interface ContactActionItem {
  type: "call" | "email";
  available: boolean;
  target: string;
}

export interface KeepRequestParticipantItem {
  accountUserId: string;
  displayName: string;
  role: string;
  participationType: string;
  notificationsEnabled: boolean;
  isEligible: boolean;
  attachedAtUtc: string;
  detachedAtUtc: string | null;
}

export interface CurrentUserDetailParticipation {
  participationType: string;
  notificationsEnabled: boolean | null;
}

export interface KeepRequestEventItem {
  id: string;
  eventType: string;
  content: string | null;
  visibility: string;
  occurredAtUtc: string;
  actorType: string;
  actorAccountUserId: string | null;
  actorDisplayName: string | null;
  statusAfter: string | null;
  messageIntent: string | null;
  communicationChannel: string | null;
  externalContactDirection: string | null;
  externalContactChannel: string | null;
  externalContactOutcome: string | null;
  externalContactRequiresFollowUp: boolean | null;
  externalContactSetFirstResponse: boolean | null;
  externalContactClearedAttention: boolean | null;
  participationAction: string | null;
  participationTargetAccountUserId: string | null;
  participationTargetDisplayName: string | null;
  participationPreviousResponsibleAccountUserId: string | null;
  participationInternalNote: string | null;
  plannedForDate: string | null;
  followUpOnDate: string | null;
  followUpOnReason: string | null;
  feedbackWasResolved: boolean | null;
  relatedEventId: string | null;
}

export interface KeepRequestNavigation {
  previousId: string | null;
  nextId: string | null;
  position: number;
  total: number;
}

export interface KeepRequestDetailResult {
  requestId: string;
  referenceCode: string;
  status: string;
  origin: string;
  source: string | null;
  needsShare: boolean;
  businessName: string;
  customerName: string;
  customerPhone: string;
  customerEmail: string | null;
  description: string;
  currentStatusText: string | null;
  pageToken: string;
  version: string;
  expiresAtUtc: string | null;
  createdAtUtc: string;
  lastBusinessActivityAt: string | null;
  lastCustomerActivityAt: string | null;
  terminatedAtUtc: string | null;
  followUpOnDate: string | null;
  followUpOnReason: string | null;
  followUpOnNote: string | null;
  plannedForDate: string | null;
  attentionLevel: string;
  waitingDirection: string;
  attentionReason: string | null;
  priorityBand: string;
  attentionSinceUtc: string | null;
  nextAttentionAtUtc: string | null;
  attentionClearedAtUtc: string | null;
  attentionClearedByAccountUserId: string | null;
  attentionClearReason: string | null;
  firstResponseDueAtUtc: string | null;
  firstRespondedAtUtc: string | null;
  firstResponderAccountUserId: string | null;
  firstResponseEventId: string | null;
  feedbackWasResolved: boolean | null;
  feedbackComment: string | null;
  feedbackSubmittedAtUtc: string | null;
  feedbackCommentVisible: boolean;
  feedbackReviewedAtUtc: string | null;
  feedbackReviewedByAccountUserId: string | null;
  feedbackReviewNote: string | null;
  feedbackReviewAgeBucket: string | null;
  feedbackReviewDueAtUtc: string | null;
  customerPageLastViewedAtUtc: string | null;
  customerPageViewedAfterLatestUpdate: boolean | null;
  intakeUrgency: string;
  businessPriority: string | null;
  contactPreference: string;
  serviceAddressLine1: string | null;
  serviceAddressLine2: string | null;
  serviceCity: string | null;
  serviceState: string | null;
  serviceZip: string | null;
  contactActions: ContactActionItem[];
  participants: KeepRequestParticipantItem[];
  currentUserParticipation: CurrentUserDetailParticipation;
  events: KeepRequestEventItem[];
  availableActions: AvailableActionsMetadata;
  validation: ValidationHintsMetadata;
  navigation: KeepRequestNavigation | null;
  pendingNotification: PendingNotificationSummary | null;
}

export interface KeepRequestRelatedWorkItem {
  requestId: string;
  referenceCode: string;
  status: string;
  lastActivityAtUtc: string;
}

export interface KeepRequestRelatedWorkResult {
  totalCount: number;
  items: KeepRequestRelatedWorkItem[];
}

// GAP-052b / ADR-451: reload-recovery projection of the durable prepare/confirm obligation.
// canConfirmAsCurrentUser reflects the same-actor rule the server enforces; no raw preparer ID.
export interface PendingNotificationSummary {
  relatedUpdateEventId: string;
  channel: string;
  preparedAtUtc: string;
  canConfirmAsCurrentUser: boolean;
}

export type ShareIntentMethod = "sms_qr" | "email" | "whatsapp" | "copy_message" | "copy_link" | "manual_other";

export interface CreateSmsHandoffResult {
  handoffUrl: string;
  expiresAtUtc: string;
}

export interface CreateCallHandoffResult {
  handoffUrl: string;
  expiresAtUtc: string;
}

// GAP-052b / ADR-451: Channel is "sms" or "email" — the only two permitted notification channels.
export interface UpdateNotificationBody {
  relatedUpdateEventId: string;
  channel: string;
}

export interface LogExternalContactBody {
  direction: string;
  channel: string;
  outcome?: string;
  requiresBusinessFollowUp?: boolean;
  summary?: string;
}

export interface UpdateServiceLocationBody {
  addressLine1: string;
  addressLine2?: string;
  city: string;
  state: string;
  zip?: string;
}

// --- Request list ---

export interface KeepRequestRankingInfo {
  rankingGroup: string;
  rankingOrder: number;
  rankingReason: string;
  severity: string;
  isOverdue: boolean;
  elapsedSinceUtc: string | null;
  dueAtUtc: string | null;
  isPostClose: boolean;
}

export interface KeepRequestAttentionInfo {
  attentionLevel: string;
  waitingDirection: string;
  attentionReason: string | null;
  priorityBand: string;
  attentionSinceUtc: string | null;
  nextAttentionAtUtc: string | null;
  firstResponseDueAtUtc: string | null;
  firstRespondedAtUtc: string | null;
  firstResponsePending: boolean;
  firstResponseOverdue: boolean;
}

export interface KeepRequestPreviewInfo {
  previewText: string | null;
  previewSource: string | null;
  previewTruncated: boolean;
  previewAtUtc: string | null;
}

export interface KeepRequestOriginalSummaryInfo {
  fullText: string;
}

export interface KeepRequestParticipationInfo {
  responsibleCount: number;
  watchingCount: number;
  hasResponsible: boolean;
  isUnassigned: boolean;
  currentUserParticipationType: string;
  responsibleDisplayName: string | null;
}

export interface KeepQuickAction {
  code: string;
  label: string;
  visibility: string;
  requiresVersion: boolean;
  executionMode: "inline" | "modal" | "detail";
  clearsAttention: boolean;
  countsFirstResponse: boolean;
  changesStatus: boolean;
  effectSummaryCode: string;
}

export interface KeepRequestActionsInfo {
  quickActions: KeepQuickAction[];
}

export interface KeepRequestTimingInfo {
  followUpOnDate: string | null;
  followUpOnReason: string | null;
  followUpOnNote: string | null;
  followUpOnLabel: string | null;
  hasFutureFollowUpOn: boolean;
  plannedForDate: string | null;
  plannedForLabel: string | null;
  hasFuturePlannedFor: boolean;
}

export interface KeepRequestSummary {
  id: string;
  referenceCode: string;
  status: string;
  currentStatusText: string | null;
  customerName: string;
  customerPhone: string;
  customerEmail: string | null;
  lastCustomerActivityAtUtc: string | null;
  lastBusinessActivityAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: string;
  isTerminal: boolean;
  isPostCloseFollowUp: boolean;
  needsShare: boolean;
  source: string | null;
  intakeUrgency: string;
  businessPriority: string | null;
  contactPreference: string;
  serviceAddressLine1: string | null;
  serviceAddressLine2: string | null;
  serviceCity: string | null;
  serviceState: string | null;
  serviceZip: string | null;
  feedbackWasResolved: boolean | null;
  feedbackReviewAgeBucket: string | null;
  feedbackReviewDueAtUtc: string | null;
  rowContext: string;
  ranking: KeepRequestRankingInfo;
  attention: KeepRequestAttentionInfo;
  originalSummary: KeepRequestOriginalSummaryInfo;
  latestActivity: KeepRequestPreviewInfo | null;
  hasInternalNote: boolean;
  participation: KeepRequestParticipationInfo;
  actions: KeepRequestActionsInfo;
  timing?: KeepRequestTimingInfo;
}

export interface KeepRequestViewCounts {
  default: number;
  assignedToMe: number;
  watching: number;
  unassigned: number;
  needsAttention: number;
  feedbackReview: number;
  readyToClose: number;
}

export interface KeepRequestPageInfo {
  limit: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface KeepRequestListContext {
  view: string;
  isDefaultCommandCenter: boolean;
  isHistory: boolean;
  isSearch: boolean;
}

export interface KeepRequestListResult {
  requests: KeepRequestSummary[];
  pageInfo: KeepRequestPageInfo;
  viewCounts: KeepRequestViewCounts | null;
  listContext: KeepRequestListContext;
}

export interface KeepRequestAvailableItem {
  requestId: string;
  referenceCode: string;
  customerName: string;
  status: string;
  createdAtUtc: string;
  attentionSinceUtc: string | null;
  nextAttentionAtUtc: string | null;
  priorityBand: string;
  attentionLevel: string;
  descriptionPreview: string;
  version: string;
  canSelfAssign: boolean;
  canWatch: boolean;
}

export interface KeepAvailableRequestsResult {
  requests: KeepRequestAvailableItem[];
  pageInfo: KeepRequestPageInfo;
}

export type RequestView =
  | "default"
  | "assigned_to_me"
  | "needs_attention"
  | "watching"
  | "ready_to_close"
  | "feedback_review"
  | "closed_history"
  | "cancelled_history"
  | "all_history";

export interface GetRequestsParams {
  view?: RequestView;
  status?: string;
  q?: string;
  cursor?: string;
  limit?: number;
  // GAP-044: history-only date scope (ADR-258 TerminatedAtUtc window). closedShortcut and
  // closedFrom/closedTo are mutually exclusive — the server rejects both being set.
  closedFrom?: string;
  closedTo?: string;
  closedShortcut?: string;
}

export interface CapabilityPackageStatus {
  featureKey: string;
  enabled: boolean;
}

// Price Book, Quotes & Materials — bounded catalog reads (Session 2e.3, build-log/113).
export interface CatalogItemResponse {
  id: string;
  type: string;
  displayName: string;
  externalKey: string | null;
  categoryId: string | null;
  unitOfMeasure: string;
  currency: string;
  isCommonItem: boolean;
  activeState: string;
  concurrencyVersion: string;
}

export interface CatalogItemListRowResponse {
  item: CatalogItemResponse;
  currentPricingMode: string | null;
  currentSellPrice: number | null;
  matchRank: string;
  matchReason: string | null;
}

export interface CatalogItemListResult {
  items: CatalogItemListRowResponse[];
  limit: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface CatalogItemAliasSummaryResponse {
  id: string;
  aliasText: string;
  activeState: string;
}

// Session 2e.6a, build-log/113: read-only item detail.
export interface CatalogItemDetailResult {
  item: CatalogItemResponse;
  aliases: CatalogItemAliasSummaryResponse[];
  category: CatalogCategoryResponse | null;
  currentPricingMode: string | null;
  currentSellPrice: number | null;
  currentCost: number | null;
}

export interface CatalogCategoryResponse {
  id: string;
  name: string;
  displayOrder: number;
  activeState: string;
  concurrencyVersion: string;
}

export interface CatalogCategoryListResult {
  categories: CatalogCategoryResponse[];
}

// Session 2e.5, build-log/113: atomic creation drawer.
export interface CreateAndActivateCatalogItemBody {
  type: string;
  displayName: string;
  unitOfMeasure: string;
  currency: string;
  externalKey?: string | null;
  categoryId?: string | null;
  isCommonItem: boolean;
  initialAliasTexts?: string[];
  pricingMode: string;
  cost?: number | null;
  sellPrice?: number | null;
}

export interface CreateAndActivateCatalogItemResult {
  item: CatalogItemResponse;
  versionNumber: number;
  priceBookVersionId: string;
  priceBookVersionLineId: string;
  cost: number | null;
  sellPrice: number | null;
  pricingMode: string;
}

// Session 2e.6b, build-log/113: header-only update.
export interface UpdateCatalogItemHeaderBody {
  displayName: string;
  externalKey?: string | null;
  categoryId?: string | null;
  isCommonItem: boolean;
}

export interface UpdateCatalogItemHeaderResult {
  concurrencyVersion: string;
}

// Session 2e.6c, build-log/113: reactivate and alias-management wiring.
export interface CatalogItemTransitionResult {
  concurrencyVersion: string;
}

export interface AddCatalogItemAliasBody {
  aliasText: string;
}

export interface AddCatalogItemAliasResult {
  id: string;
  catalogItemId: string;
  aliasText: string;
  activeState: string;
  catalogItemConcurrencyVersion: string;
}

export interface CatalogItemAliasTransitionResult {
  catalogItemConcurrencyVersion: string;
}

// Session 2e.6d, build-log/113: later price publish. No version header — ADR-470's
// account-scoped publish lock is the concurrency mechanism here, not CatalogItem.ConcurrencyVersion.
export interface PublishCatalogItemPriceBody {
  cost: number | null;
  sellPrice: number | null;
  reason: string;
}

export interface PublishCatalogItemPriceResult {
  versionNumber: number;
  priceBookVersionId: string;
  priceBookVersionLineId: string;
  cost: number | null;
  sellPrice: number | null;
}

export interface CreateCatalogCategoryBody {
  name: string;
  displayOrder: number;
}

export interface GetCatalogItemsParams {
  search?: string;
  type?: string;
  categoryId?: string;
  status?: string;
  cursor?: string;
  limit?: number;
}

// Price Book, Quotes & Materials — Offering/Assembly office management (Session 3.2c).
export interface OfferingAssemblyItemResponse {
  id: string;
  catalogItemId: string;
  defaultQuantity: number;
  isOptional: boolean;
  displayOrder: number;
}

export interface OfferingAssemblyResponse {
  id: string;
  primaryCatalogItemId: string;
  name: string;
  priceTreatment: string;
  activeState: string;
  concurrencyVersion: string;
  items: OfferingAssemblyItemResponse[];
}

export interface OfferingAssemblyTransitionResult {
  concurrencyVersion: string;
}

export interface CreateOfferingAssemblyWithItemsItemBody {
  catalogItemId: string;
  defaultQuantity: number;
  isOptional: boolean;
  displayOrder: number;
}

export interface CreateOfferingAssemblyWithItemsBody {
  primaryCatalogItemId: string;
  name: string;
  priceTreatment: string;
  items: CreateOfferingAssemblyWithItemsItemBody[];
}

export interface UpdateOfferingAssemblyHeaderBody {
  primaryCatalogItemId: string;
  name: string;
  priceTreatment: string;
}

export interface AddOfferingAssemblyItemBody {
  catalogItemId: string;
  defaultQuantity: number;
  isOptional: boolean;
  displayOrder: number;
}

export interface OfferingAssemblyItemAddedResult {
  itemId: string;
  concurrencyVersion: string;
}

export interface UpdateOfferingAssemblyItemBody {
  defaultQuantity: number;
  isOptional: boolean;
  displayOrder: number;
}

export interface OfferingAssemblyListRowResponse {
  id: string;
  name: string;
  primaryCatalogItemId: string;
  primaryCatalogItemDisplayName: string;
  priceTreatment: string;
  activeState: string;
  concurrencyVersion: string;
  isOperationallyEligible: boolean;
}

export interface OfferingAssemblyListResult {
  items: OfferingAssemblyListRowResponse[];
  limit: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface ActiveAssemblyDependency {
  id: string;
  name: string;
}

export interface ActiveAssemblyDependenciesResult {
  count: number;
  assemblies: ActiveAssemblyDependency[];
}

export interface OfferingAssemblyDetailItemResponse {
  id: string;
  catalogItemId: string;
  catalogItemDisplayName: string;
  defaultQuantity: number;
  isOptional: boolean;
  displayOrder: number;
}

export interface OfferingAssemblyEligibilityReasonResponse {
  code: string;
  componentCatalogItemId: string | null;
}

export interface AssemblyPricingReasonResult {
  code: string;
  catalogItemId: string;
  catalogItemDisplayName: string;
}

// Step 2 phase-one pricing summary (2026-08-13): server-authoritative only — never recompute
// price, cost, counts, or reasons on the frontend. priceStatus and marginStatus are independent
// axes; a NeedsCostReview marginStatus never implies priceStatus is NeedsReview.
export interface OfferingAssemblyPricingResult {
  priceStatus: string;
  calculatedSellPrice: number | null;
  marginStatus: string;
  missingCostLineCount: number;
  priceReasons: AssemblyPricingReasonResult[];
  marginReasons: AssemblyPricingReasonResult[];
}

export interface OfferingAssemblyDetailResult {
  id: string;
  name: string;
  primaryCatalogItemId: string;
  primaryCatalogItemDisplayName: string;
  priceTreatment: string;
  activeState: string;
  concurrencyVersion: string;
  items: OfferingAssemblyDetailItemResponse[];
  isOperationallyEligible: boolean;
  eligibilityReasons: OfferingAssemblyEligibilityReasonResponse[];
  pricing: OfferingAssemblyPricingResult;
}

export interface GetOfferingAssembliesParams {
  status?: string;
  cursor?: string;
  limit?: number;
}

export interface ScopeNudgeSuggestionConfigRowResponse {
  id: string;
  order: number;
  suggestedCatalogItemId: string | null;
  suggestedOfferingAssemblyId: string | null;
  targetDisplayName: string;
  isEligible: boolean;
}

export interface ScopeNudgeRuleConfigRowResponse {
  id: string;
  triggerCatalogItemId: string | null;
  triggerOfferingAssemblyId: string | null;
  triggerDisplayName: string;
  triggerIsEligible: boolean;
  suggestions: ScopeNudgeSuggestionConfigRowResponse[];
}

export interface ScopeNudgeRuleConfigListResponse {
  rules: ScopeNudgeRuleConfigRowResponse[];
}

export interface ScopeNudgeSuggestionBody {
  catalogItemId?: string | null;
  offeringAssemblyId?: string | null;
}

export interface CreateScopeNudgeRuleBody {
  triggerCatalogItemId?: string | null;
  triggerOfferingAssemblyId?: string | null;
  suggestions: ScopeNudgeSuggestionBody[];
}

export interface UpdateScopeNudgeRuleBody {
  suggestions: ScopeNudgeSuggestionBody[];
}

export type FollowUpResolutionOutcome = "complete" | "move" | "keep_active";
export type FollowUpCompletionReason =
  | "customer_contacted"
  | "work_completed"
  | "no_longer_needed"
  | "other";

export interface ResolveFollowUpBody {
  outcome: FollowUpResolutionOutcome;
  completionReason?: FollowUpCompletionReason | null;
  note?: string | null;
  newDate?: string | null;
  newFollowUpReason?: string | null;
}

// Session 3.4f-1, build-log/118: proposed-scope entry-point + draft-lifecycle probe.
// State is "NoScopeYet" or the scope's Status ("Draft" | "SubmittedToOffice" | "OfficeReviewed" |
// others) — never an ambiguous null-body-only response.
export interface ProposedScopeLineResponse {
  id: string;
  lineType: string;
  catalogItemId: string | null;
  offeringAssemblyId: string | null;
  quantity: number;
  isException: boolean;
  offCatalogDescription: string | null;
  offCatalogQuantity: number | null;
  note: string | null;
  displayOrder: number;
  displayNameSnapshot: string;
  unitOfMeasureSnapshot: string | null;
  offeringAssemblyNameSnapshot: string | null;
  defaultQuantitySnapshot: number | null;
}

export interface ProposedScopeDetailResult {
  id: string;
  requestId: string;
  status: string;
  concurrencyVersion: string;
  lines: ProposedScopeLineResponse[];
}

export interface CurrentProposedScopeForRequestResult {
  state: string;
  scope: ProposedScopeDetailResult | null;
}

export interface CreateProposedScopeBody {
  requestId: string;
}

export interface ProposedScopeResult {
  id: string;
  requestId: string;
  status: string;
  concurrencyVersion: string;
}

// Session 3.4f-2, build-log/118: escape-ladder mutations + the price-free field reads they consume.
export interface FieldSelectProposedScopeLineBody {
  lineType: "KnownCatalogItem" | "OffCatalogItem";
  catalogItemId?: string | null;
  quantity: number;
  offCatalogDescription?: string | null;
  note?: string | null;
}

export interface ProposedScopeLineAddedResult {
  lineId: string;
  concurrencyVersion: string;
}

export interface ExpandAssemblyBody {
  offeringAssemblyId: string;
  excludedOptionalItemIds?: string[];
}

export interface ExpandAssemblyResult {
  lineIds: string[];
  concurrencyVersion: string;
}

export interface ExpandActualWorkAssemblyBody {
  offeringAssemblyId: string;
  includedOptionalItemIds?: string[];
}

export interface ExpandActualWorkAssemblyResult {
  lineIds: string[];
  skippedCatalogItemIds: string[];
  actualWorkConcurrencyVersion: string;
}

export interface FieldCatalogItemResponse {
  id: string;
  type: string;
  displayName: string;
  externalKey: string | null;
  categoryId: string | null;
  unitOfMeasure: string;
}

export interface FieldCatalogItemListRowResponse {
  item: FieldCatalogItemResponse;
  matchRank: string;
  matchReason: string | null;
}

export interface FieldCatalogItemListResult {
  items: FieldCatalogItemListRowResponse[];
  limit: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface FieldCatalogCategoryResponse {
  id: string;
  name: string;
}

export interface FieldCatalogCategoryListResult {
  categories: FieldCatalogCategoryResponse[];
}

export interface GetFieldCatalogItemsParams {
  search?: string;
  categoryId?: string;
  limit?: number;
  cursor?: string;
}

export interface FieldOfferingAssemblyListRowResponse {
  id: string;
  name: string;
  primaryCatalogItemId: string;
  primaryCatalogItemDisplayName: string;
}

export interface FieldOfferingAssemblyListResult {
  items: FieldOfferingAssemblyListRowResponse[];
  limit: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface FieldOfferingAssemblyDetailItemResponse {
  id: string;
  catalogItemId: string;
  catalogItemDisplayName: string;
  defaultQuantity: number;
  isOptional: boolean;
  displayOrder: number;
}

export interface FieldOfferingAssemblyDetailResult {
  id: string;
  name: string;
  primaryCatalogItemId: string;
  primaryCatalogItemDisplayName: string;
  items: FieldOfferingAssemblyDetailItemResponse[];
}

export interface GetFieldOfferingAssembliesParams {
  limit?: number;
  cursor?: string;
}

// Session 3.4g, build-log/118: draft line edit/remove + submit — the same three mutations
// ProposedScopeApiService has offered since Session 3.3b, wired to the frontend for the first time.
export interface UpdateProposedScopeLineBody {
  quantity: number;
  isException: boolean;
  note?: string | null;
  displayOrder: number;
}

export interface ProposedScopeTransitionResult {
  concurrencyVersion: string;
}

// Session 5A, build-log/120: field-safe Quick scope action read — separate route/shape from the
// Owner/Admin configuration response; never carries a price or eligibility/repair field.
export interface QuickScopeActionFieldRowResponse {
  id: string;
  order: number;
  catalogItemId: string | null;
  offeringAssemblyId: string | null;
  targetDisplayName: string;
}

export interface QuickScopeActionFieldListResult {
  actions: QuickScopeActionFieldRowResponse[];
}

// Session 5, build-log/125: field-safe Paired Nudges read for one trigger — price-free, mirrors
// ScopeNudgeFieldResult/ScopeNudgeSuggestionFieldRow.
export interface ScopeNudgeSuggestionFieldRowResponse {
  id: string;
  order: number;
  catalogItemId: string | null;
  offeringAssemblyId: string | null;
  displayName: string;
  targetKind: string;
}

export interface ScopeNudgeFieldResultResponse {
  ruleId: string | null;
  triggerCatalogItemId: string | null;
  triggerOfferingAssemblyId: string | null;
  suggestions: ScopeNudgeSuggestionFieldRowResponse[];
}

// Build Log 121, ADR-486: polymorphic field-scope search — one rank-ordered sequence of Active
// catalog items and Active/operationally-eligible assemblies, replacing the Common-Item-only
// composer search. Price-free; `kind` distinguishes an assembly row from a catalog-item row.
export interface FieldScopeSearchResultResponse {
  kind: "OfferingAssembly" | "CatalogItem";
  id: string;
  displayName: string;
  defaultItemCount: number | null;
  catalogItemType: string | null;
  externalKey: string | null;
}

export interface FieldScopeSearchResult {
  items: FieldScopeSearchResultResponse[];
  limit: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface GetFieldScopeSearchParams {
  search?: string;
  limit?: number;
  cursor?: string;
}

// Direct Actual Work (ADR-487, build-log/129, Batch 5b) — price-blind field capture composer.
export interface ActualWorkCreateBody {
  requestId: string;
}

export interface ActualWorkResult {
  id: string;
  requestId: string;
  status: string;
  concurrencyVersion: string;
}

export interface ActualWorkAddLineBody {
  catalogItemId?: string | null;
  offCatalogDescription?: string | null;
  actualQuantity: number;
  note?: string | null;
}

export interface ActualWorkUpdateLineBody {
  actualQuantity: number;
  note?: string | null;
}

export interface ActualWorkLineAddedResult {
  lineId: string;
  actualWorkConcurrencyVersion: string;
}

export interface ActualWorkConcurrencyVersionResult {
  concurrencyVersion: string;
}

/** One of "DiagnosticOnly" | "NoWorkAuthorized" | "NoAccess" — see `ActualWorkOutcome`. */
export interface ActualWorkSubmitBody {
  outcome?: string | null;
  completionNote?: string | null;
}

export interface ActualWorkLineHistoryEntry {
  id: string;
  displayNameSnapshot: string;
  unitOfMeasureSnapshot: string | null;
  actualQuantity: number;
  note: string | null;
}

export interface ActualWorkOpenDraftEntry {
  id: string;
  status: string;
  outcome: string | null;
  completionNote: string | null;
  submittedAtUtc: string | null;
  concurrencyVersion: string;
  lines: ActualWorkLineHistoryEntry[];
}

export interface ActualWorkSubmittedVisitEntry {
  id: string;
  status: string;
  outcome: string | null;
  completionNote: string | null;
  submittedAtUtc: string | null;
  lines: ActualWorkLineHistoryEntry[];
}

/** `canCaptureActualWork` disambiguates a null `openDraft`: true only when the caller is the
 * request's active Responsible recorder AND holds `RequestsOperate` and `ActualWorkCapture`
 * (whether or not a Draft is open yet) — the capture UI must gate on this, not on
 * `openDraft === null` alone, or a caller without capture permission (e.g. a Viewer) would see an
 * action that fails on create. */
export interface ActualWorkHistoryResult {
  canCaptureActualWork: boolean;
  openDraft: ActualWorkOpenDraftEntry | null;
  submittedVisits: ActualWorkSubmittedVisitEntry[];
}
