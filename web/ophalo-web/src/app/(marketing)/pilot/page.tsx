import Link from "next/link";
import KeepListMockup from "../_components/KeepListMockup";

export const metadata = {
  title: "Pilot — Keep by OpHalo",
  description: "Join the Keep pilot. Free access for small service businesses during the pilot program.",
};

export default function KeepPilotPage() {
  return (
    <div className="mkt-page">

      <section className="mkt-hero">
        <div className="mkt-container">
          <div className="mkt-hero-grid">
            <div className="mkt-hero-text">
              <p className="mkt-eyebrow">Keep · Pilot</p>
              <h1 className="mkt-h1">
                Keep every customer follow-up from slipping.
              </h1>
              <p className="mkt-subhead">
                Keep shows you who&apos;s still waiting on you and keeps your customers in the loop —
                without changing how you already run your business.
              </p>
              <div className="mkt-cta-group">
                <Link href="/start" className="mkt-btn-primary">Join the Pilot</Link>
              </div>
              <p className="mkt-pilot-note">
                Free during the pilot. Availability is limited.
              </p>
              <p className="mkt-pilot-note">
                Built for small service businesses that run on calls, texts, and email.
              </p>
            </div>
            <div className="mkt-hero-visual" aria-hidden="true">
              <KeepListMockup />
            </div>
          </div>
        </div>
      </section>

      <section className="mkt-section mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h3">What you get during the pilot</h2>
          <p className="mkt-body">We&apos;re onboarding an initial group of service businesses. Here&apos;s what you get.</p>
          <div className="mkt-icon-grid">
            <div className="mkt-icon-item">
              <span className="mkt-icon-emoji">📱</span>
              <p className="mkt-icon-label">Early access</p>
              <p className="mkt-icon-desc">Use Keep before public release.</p>
            </div>
            <div className="mkt-icon-item">
              <span className="mkt-icon-emoji">🛠️</span>
              <p className="mkt-icon-label">Hands-on setup</p>
              <p className="mkt-icon-desc">We help you get your first requests in and your workflow set.</p>
            </div>
            <div className="mkt-icon-item">
              <span className="mkt-icon-emoji">📞</span>
              <p className="mkt-icon-label">Direct line</p>
              <p className="mkt-icon-desc">Text or call (901)&nbsp;313-4063 during the pilot. Same-day, no ticket queue.</p>
            </div>
            <div className="mkt-icon-item">
              <span className="mkt-icon-emoji">🚀</span>
              <p className="mkt-icon-label">Shape the product</p>
              <p className="mkt-icon-desc">Your day-to-day decides what we build next.</p>
            </div>
            <div className="mkt-icon-item">
              <span className="mkt-icon-emoji">💰</span>
              <p className="mkt-icon-label">Free during pilot</p>
              <p className="mkt-icon-desc">No cost while we&apos;re in pilot.</p>
            </div>
          </div>
        </div>
      </section>

      <section className="mkt-section mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h3">What Keep helps you solve</h2>
          <p className="mkt-body">
            Keep helps you stay on top of customer requests without changing how you run your business.
          </p>
          <ul className="mkt-bullets">
            <li>Fewer requests buried or forgotten</li>
            <li>Fewer &ldquo;any update?&rdquo; messages from customers</li>
            <li>One simple page where customers can check status themselves</li>
            <li>A clearer picture of where things slip over time</li>
          </ul>
        </div>
      </section>

      <section className="mkt-section-compact mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h3">Who it&apos;s for</h2>
          <p className="mkt-body">
            Built for small service businesses that juggle customer requests across calls, texts, and email.
          </p>
          <div className="mkt-icon-row">
            <div className="mkt-icon-row-item">
              <span className="mkt-icon-row-emoji">📞</span>
              <span className="mkt-icon-row-label">Phone calls</span>
            </div>
            <div className="mkt-icon-row-item">
              <span className="mkt-icon-row-emoji">💬</span>
              <span className="mkt-icon-row-label">Text messages</span>
            </div>
            <div className="mkt-icon-row-item">
              <span className="mkt-icon-row-emoji">✉️</span>
              <span className="mkt-icon-row-label">Email</span>
            </div>
          </div>
          <p className="mkt-body mkt-body-spacer">
            Keep doesn&apos;t replace your phone or your inbox. You reach customers the way you
            always have — Keep makes sure nothing slips, and gives every customer a status page
            so they stop chasing you for updates.
          </p>
        </div>
      </section>

      <section className="mkt-section mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h3">How the pilot works</h2>
          <div className="mkt-timeline">
            <div className="mkt-timeline-step">
              <div className="mkt-timeline-num">1</div>
              <p className="mkt-timeline-label">Join the pilot</p>
              <p className="mkt-timeline-desc">Create your account and get instant access.</p>
            </div>
            <div className="mkt-timeline-step">
              <div className="mkt-timeline-num">2</div>
              <p className="mkt-timeline-label">Add your first request</p>
              <p className="mkt-timeline-desc">Log a real customer request and see how it works. From there, every request lives in one place.</p>
            </div>
            <div className="mkt-timeline-step">
              <div className="mkt-timeline-num">3</div>
              <p className="mkt-timeline-label">We stay close</p>
              <p className="mkt-timeline-desc">We check in to make sure you&apos;re rolling, and you can text us anytime.</p>
            </div>
          </div>
        </div>
      </section>

      <section className="mkt-section-compact mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h3">What you can do in Keep today</h2>
          <ul className="mkt-bullets">
            <li>Capture every customer request in one place</li>
            <li>See at a glance who needs attention and who&apos;s waiting</li>
            <li>Send a status update — your customer gets an email and a live status page</li>
            <li>Copy a ready-to-send update to text or post in your own voice</li>
            <li>Mark requests handled and keep a clean record of what happened</li>
          </ul>
        </div>
      </section>

      <section className="mkt-section-compact mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h3">What we ask of you</h2>
          <p className="mkt-body">
            The pilot runs while we make sure onboarding, request handling, and reliability hold up
            in real businesses. In return for early, free access, we ask pilot businesses to use Keep
            with real customers and tell us what&apos;s working and what isn&apos;t — a quick check-in
            now and then, nothing heavy.
          </p>
          <p className="mkt-body mkt-body-spacer">
            Before public release, you&apos;ll get clear next steps — including pricing and anything
            that&apos;s changing. No surprises.
          </p>
        </div>
      </section>

      <section className="mkt-section mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h3">Common questions</h2>
          <div className="mkt-faq">
            <details className="mkt-faq-item">
              <summary>
                <span className="mkt-faq-q">Is Keep finished?</span>
              </summary>
              <p className="mkt-faq-a">
                No — and that&apos;s the point of the pilot. The core works today, and you&apos;ll
                help shape the rest.
              </p>
            </details>
            <details className="mkt-faq-item">
              <summary>
                <span className="mkt-faq-q">Is my business data safe?</span>
              </summary>
              <p className="mkt-faq-a">
                Yes. Your customer information is private to your business and never sold or shared.
                We&apos;ll walk through exactly what&apos;s collected during onboarding.
              </p>
            </details>
            <details className="mkt-faq-item">
              <summary>
                <span className="mkt-faq-q">Do my customers need an account?</span>
              </summary>
              <p className="mkt-faq-a">
                No. They get a private link to a simple status page — no login, no app to download.
              </p>
            </details>
            <details className="mkt-faq-item">
              <summary>
                <span className="mkt-faq-q">What does it cost?</span>
              </summary>
              <p className="mkt-faq-a">
                Nothing during the pilot. We&apos;ll share pricing before public release, and pilot
                businesses get the first and best terms.
              </p>
            </details>
            <details className="mkt-faq-item">
              <summary>
                <span className="mkt-faq-q">What happens after the pilot?</span>
              </summary>
              <p className="mkt-faq-a">
                You&apos;ll get clear next steps and pricing before anything changes. No surprise charges.
              </p>
            </details>
          </div>
        </div>
      </section>

      <section className="mkt-final-cta">
        <div className="mkt-container">
          <h2 className="mkt-h2">
            Get on top of customer follow-ups before the quiet ones cost you a job.
          </h2>
          <p className="mkt-body">Free during the pilot.</p>
          <div className="mkt-cta-group">
            <Link href="/start" className="mkt-btn-primary">Join the Pilot</Link>
          </div>
        </div>
      </section>

    </div>
  );
}
