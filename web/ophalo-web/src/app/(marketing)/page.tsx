import Link from "next/link";
import KeepListMockup from "./_components/KeepListMockup";

export default function HomePage() {
  return (
    <div className="mkt-page">

      {/* 1. Hero */}
      <section className="mkt-hero">
        <div className="mkt-container">
          <div className="mkt-hero-grid">
            <div className="mkt-hero-text">
              <p className="mkt-eyebrow">Keep · Built for small service businesses</p>
              <h1 className="mkt-h1">
                Know which customers are waiting on you — before they wonder if you forgot.
              </h1>
              <p className="mkt-subhead">
                A question goes unanswered. A callback slips. A customer stops hearing
                from you — and starts wondering if you care. Keep gives every active request
                a home and shows you who needs a response.
              </p>
              <div className="mkt-cta-group">
                <Link href="/start" className="mkt-btn-primary">Try Keep</Link>
              </div>
              <p className="mkt-pilot-note">
                Free during the pilot. Availability is limited.
              </p>
            </div>
            <div className="mkt-hero-visual" aria-hidden="true">
              <KeepListMockup />
            </div>
          </div>
        </div>
      </section>

      {/* 2. Product visual strip */}
      <section className="mkt-visual-section">
        <div className="mkt-container">
          <div className="mkt-visual-strip" role="img" aria-label="Keep showing three request states: Needs attention first, then Active, then Closed">

            <div className="mkt-visual-card mkt-visual-card-attention">
              <div className="mkt-visual-card-header">
                <svg className="mkt-visual-icon" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
                  <circle cx="10" cy="10" r="8" />
                  <line x1="10" y1="6" x2="10" y2="11" />
                  <circle cx="10" cy="14" r="0.75" fill="currentColor" stroke="none" />
                </svg>
                <span className="mkt-status-label mkt-label-attention">Needs attention</span>
              </div>
              <p className="mkt-visual-card-title">Rivera — water heater repair</p>
              <p className="mkt-visual-card-meta">Customer asked a question — waiting on you</p>
            </div>

            <div className="mkt-visual-card mkt-visual-card-active">
              <div className="mkt-visual-card-header">
                <svg className="mkt-visual-icon" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
                  <circle cx="10" cy="10" r="8" />
                  <polyline points="10,6 10,10 13,12" />
                </svg>
                <span className="mkt-status-label mkt-label-active">Active</span>
              </div>
              <p className="mkt-visual-card-title">Johnson — HVAC maintenance</p>
              <p className="mkt-visual-card-meta">Update sent — nothing needed right now</p>
            </div>

            <div className="mkt-visual-card mkt-visual-card-closed">
              <div className="mkt-visual-card-header">
                <svg className="mkt-visual-icon" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <circle cx="10" cy="10" r="8" />
                  <polyline points="6.5,10.5 9,13 13.5,7.5" />
                </svg>
                <span className="mkt-status-label mkt-label-closed">Closed</span>
              </div>
              <p className="mkt-visual-card-title">Patel — gutter repair</p>
              <p className="mkt-visual-card-meta">Work finished — loop closed</p>
            </div>

          </div>
        </div>
      </section>

      {/* 3. Problem */}
      <section className="mkt-section mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h2">Small business communication is personal. That&apos;s also where it breaks down.</h2>
          <p className="mkt-body">
            You&apos;re texting customers from your phone, calling back between jobs, tracking it all in your head.
            It works — until a text gets buried, a callback slips, or a customer you meant to follow up with stops hearing from you.
          </p>
          <p className="mkt-body mkt-body-spacer">
            Keep doesn&apos;t change how you communicate. It makes sure nothing gets lost.
          </p>
        </div>
      </section>

      {/* 4. Not a CRM */}
      <section className="mkt-section mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h2">Keep is not a CRM.</h2>
          <p className="mkt-body">
            CRMs try to run your whole business. Keep does one thing well: it shows you which active
            requests need a response — without replacing your tools or changing how you work.
          </p>
          <ul className="mkt-bullets">
            <li>No pipelines</li>
            <li>No stages</li>
            <li>No heavy setup</li>
            <li>Just clear visibility into your active requests</li>
          </ul>
        </div>
      </section>

      {/* 5. Request Intake + Customer Page Proof */}
      <section className="mkt-section-roomy mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h2">Customers can start a request — and it all lands in one place.</h2>
          <p className="mkt-body">
            When a request comes in — through text, email, social media, or anywhere else — you enter it
            into Keep. Keep creates a simple request page for that customer: a place to ask questions, share
            updates, and stay informed. Every active request lives in one place, no matter how it started.
          </p>
          <ul className="mkt-bullets">
            <li>No more scattered inbound messages</li>
            <li>No more missed requests</li>
            <li>No more &ldquo;Did we ever reply to that?&rdquo;</li>
            <li>Every active request tracked in Keep</li>
          </ul>
          <div className="mkt-cp-mockup" aria-hidden="true">
            <div className="mkt-cp-hero">
              <div className="mkt-cp-bar" />
              <div className="mkt-cp-hero-body">
                <p className="mkt-cp-business">Summit Plumbing</p>
                <p className="mkt-cp-headline">Your request is in progress.</p>
                <p className="mkt-cp-subline">We&apos;ll keep this page updated as your request moves forward.</p>
                <p className="mkt-cp-trust">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z" />
                    <path d="m9 12 2 2 4-4" />
                  </svg>
                  Private to this request
                </p>
              </div>
            </div>
            <div className="mkt-cp-update">
              <div className="mkt-cp-update-badges">
                <span className="mkt-cp-update-badge">Latest from Summit Plumbing</span>
              </div>
              <p className="mkt-cp-update-msg">Part arrived — we&apos;re scheduled for Thursday morning.</p>
              <p className="mkt-cp-update-date">Jun 9</p>
            </div>
          </div>
        </div>
      </section>

      {/* 6. What Keep Does */}
      <section className="mkt-section mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h2">One place to see every active request — and who&apos;s waiting on you.</h2>
          <div className="mkt-card-grid">
            <div className="mkt-feature-card">
              <div className="mkt-card-icon mkt-icon-attention" aria-hidden="true">
                <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
                  <circle cx="10" cy="10" r="8" />
                  <line x1="10" y1="6" x2="10" y2="11" />
                  <circle cx="10" cy="14" r="0.75" fill="currentColor" stroke="none" />
                </svg>
              </div>
              <h3 className="mkt-card-title">Needs attention</h3>
              <p className="mkt-card-body">When a customer asks a question, requests an update, or waits too long for a first response, the request rises to the top.</p>
            </div>
            <div className="mkt-feature-card">
              <div className="mkt-card-icon mkt-icon-active" aria-hidden="true">
                <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
                  <circle cx="10" cy="10" r="8" />
                  <polyline points="10,6 10,10 13,12" />
                </svg>
              </div>
              <h3 className="mkt-card-title">Active requests</h3>
              <p className="mkt-card-body">Everything else stays visible and current — without nagging you.</p>
            </div>
            <div className="mkt-feature-card">
              <div className="mkt-card-icon mkt-icon-customer" aria-hidden="true">
                <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
                  <circle cx="10" cy="7" r="3" />
                  <path d="M4 17c0-3.3 2.7-6 6-6s6 2.7 6 6" />
                </svg>
              </div>
              <h3 className="mkt-card-title">Customer page</h3>
              <p className="mkt-card-body">Each request has a simple customer page — no login required.</p>
            </div>
            <div className="mkt-feature-card">
              <div className="mkt-card-icon mkt-icon-neutral" aria-hidden="true">
                <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
                  <line x1="6" y1="5" x2="16" y2="5" />
                  <line x1="6" y1="10" x2="16" y2="10" />
                  <line x1="6" y1="15" x2="16" y2="15" />
                  <circle cx="3.5" cy="5" r="0.75" fill="currentColor" stroke="none" />
                  <circle cx="3.5" cy="10" r="0.75" fill="currentColor" stroke="none" />
                  <circle cx="3.5" cy="15" r="0.75" fill="currentColor" stroke="none" />
                </svg>
              </div>
              <h3 className="mkt-card-title">A factual record</h3>
              <p className="mkt-card-body">Every update, question, and status change is recorded as it happens.</p>
            </div>
          </div>
        </div>
      </section>

      {/* 7. How It Works */}
      <section className="mkt-section-roomy mkt-bg-warm-white" id="how-it-works">
        <div className="mkt-container">
          <h2 className="mkt-h2">How Keep works</h2>
          <div className="mkt-steps">
            <div className="mkt-step">
              <div className="mkt-step-left">
                <div className="mkt-step-num">1</div>
                <div className="mkt-step-line" aria-hidden="true" />
              </div>
              <div className="mkt-step-body">
                <h3 className="mkt-card-title">Start a request</h3>
                <p className="mkt-card-body">You or your customer starts a request in Keep. Takes 30 seconds.</p>
              </div>
            </div>
            <div className="mkt-step">
              <div className="mkt-step-left">
                <div className="mkt-step-num">2</div>
                <div className="mkt-step-line" aria-hidden="true" />
              </div>
              <div className="mkt-step-body">
                <h3 className="mkt-card-title">Share the page</h3>
                <p className="mkt-card-body">Send your customer their no-login request page — by text, email, or however you already reach them.</p>
              </div>
            </div>
            <div className="mkt-step">
              <div className="mkt-step-left">
                <div className="mkt-step-num">3</div>
                <div className="mkt-step-line" aria-hidden="true" />
              </div>
              <div className="mkt-step-body">
                <h3 className="mkt-card-title">Post updates. Customer replies.</h3>
                <p className="mkt-card-body">You post updates through Keep. Your customer views them on their page and can respond — no login needed.</p>
              </div>
            </div>
            <div className="mkt-step">
              <div className="mkt-step-left">
                <div className="mkt-step-num">4</div>
              </div>
              <div className="mkt-step-body">
                <h3 className="mkt-card-title">Keep surfaces what needs you</h3>
                <p className="mkt-card-body">When a customer asks a question, requests an update, or waits too long for a response, the request moves to Needs attention.</p>
              </div>
            </div>
          </div>
          <div className="mkt-before-after">
            <div className="mkt-before-after-col">
              <p className="mkt-before-after-label">Before Keep</p>
              <p className="mkt-card-body">You scroll through texts, voicemails, and email threads guessing who&apos;s waiting on you.</p>
            </div>
            <div className="mkt-before-after-col">
              <p className="mkt-before-after-label mkt-before-after-with">With Keep</p>
              <p className="mkt-card-body">Requests that need a response rise to the top. Nothing waits on memory.</p>
            </div>
          </div>
        </div>
      </section>

      {/* 8. Pilot CTA */}
      <section className="mkt-section-compact mkt-bg-soft-sand mkt-pilot-block">
        <div className="mkt-container">
          <h2 className="mkt-h3">Help shape Keep before public release.</h2>
          <p className="mkt-body">Free during the pilot. No setup required.</p>
          <div className="mkt-cta-group">
            <Link href="/start" className="mkt-btn-primary">Try Keep</Link>
            <Link href="/pilot" className="mkt-btn-secondary">Learn about the pilot</Link>
          </div>
        </div>
      </section>

    </div>
  );
}
