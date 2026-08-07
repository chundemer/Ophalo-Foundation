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
