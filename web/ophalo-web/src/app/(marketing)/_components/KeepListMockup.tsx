export default function KeepListMockup() {
  return (
    <div className="mkt-hero-mockup">
      <div className="mkt-mockup-header">
        <p className="mkt-mockup-title">Keep</p>
      </div>
      <div className="mkt-mockup-body">

        <div className="mkt-mockup-summary">
          <span className="mkt-mockup-summary-open">3 open</span>
          <span className="mkt-mockup-summary-attention">2 need attention</span>
          <span className="mkt-mockup-summary-muted">Oldest: Waiting 2h</span>
        </div>

        <div className="mkt-mockup-sechead">
          <span className="mkt-mockup-sechead-title">
            <svg className="mkt-mockup-sechead-icon-attention" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M10.3 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.7 3.86a2 2 0 0 0-3.4 0Z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
            Needs attention
          </span>
          <span className="mkt-mockup-count mkt-mockup-count-attention">2</span>
        </div>

        <div className="mkt-mockup-card mkt-mockup-card-attention">
          <div className="mkt-mockup-card-main">
            <div className="mkt-mockup-card-top">
              <p className="mkt-mockup-name">Rivera</p>
              <div className="mkt-mockup-badges">
                <span className="mkt-badge mkt-badge-attention">Answer question</span>
                <span className="mkt-badge mkt-badge-attention">Waiting 2h</span>
              </div>
            </div>
            <p className="mkt-mockup-msg">
              <span className="mkt-mockup-chip">Customer</span>
              <span className="mkt-mockup-msg-text">Will the new water heater be in this week?</span>
            </p>
          </div>
          <div className="mkt-mockup-actions">
            <span className="mkt-mockup-btn-teal">Answer customer</span>
            <span className="mkt-mockup-btn-quiet">Contact</span>
          </div>
        </div>

        <div className="mkt-mockup-card mkt-mockup-card-attention">
          <div className="mkt-mockup-card-main">
            <div className="mkt-mockup-card-top">
              <p className="mkt-mockup-name">Miller</p>
              <div className="mkt-mockup-badges">
                <span className="mkt-badge mkt-badge-attention">Send update</span>
                <span className="mkt-badge mkt-badge-attention">Waiting 1d</span>
              </div>
            </div>
            <p className="mkt-mockup-msg">
              <span className="mkt-mockup-chip">Customer</span>
              <span className="mkt-mockup-msg-text">Any update on the estimate?</span>
            </p>
          </div>
          <div className="mkt-mockup-actions">
            <span className="mkt-mockup-btn-teal">Send update</span>
            <span className="mkt-mockup-btn-quiet">Contact</span>
          </div>
        </div>

        <div className="mkt-mockup-sechead">
          <span className="mkt-mockup-sechead-title">
            <svg className="mkt-mockup-sechead-icon-active" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <circle cx="12" cy="12" r="10" />
              <polyline points="12 6 12 12 16 14" />
            </svg>
            Active
          </span>
          <span className="mkt-mockup-count mkt-mockup-count-active">1</span>
        </div>

        <div className="mkt-mockup-card mkt-mockup-card-active">
          <div className="mkt-mockup-card-main">
            <div className="mkt-mockup-card-top">
              <p className="mkt-mockup-name">Johnson</p>
            </div>
            <p className="mkt-mockup-meta">Update sent 1 hour ago</p>
          </div>
          <div className="mkt-mockup-actions">
            <span className="mkt-mockup-btn-teal">Send update</span>
            <span className="mkt-mockup-btn-quiet">Contact</span>
          </div>
        </div>

      </div>
    </div>
  );
}
