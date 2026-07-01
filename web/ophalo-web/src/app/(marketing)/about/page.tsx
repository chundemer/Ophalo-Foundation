import Link from "next/link";

export const metadata = {
  title: "About — OpHalo",
  description: "OpHalo fills the gaps in everyday service work without changing the way you already operate.",
};

export default function AboutPage() {
  return (
    <div className="mkt-page">

      <section className="mkt-section mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h2">Built for the people who do the work</h2>
          <p className="mkt-body">
            For decades, I&apos;ve hired service businesses — yard work, foundation repair, home projects.<br />
            The pattern was always the same:
          </p>
          <div className="mkt-about-list">
            <p className="mkt-about-list-item">Voicemails.</p>
            <p className="mkt-about-list-item">Unanswered emails.</p>
            <p className="mkt-about-list-item">Chasing updates.</p>
            <p className="mkt-about-list-item"><strong>Always me initiating.</strong></p>
          </div>
          <p className="mkt-body">If you run a service business, you already know this story.</p>
        </div>
      </section>

      <section className="mkt-section mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h2">Find the gaps and fill them</h2>
          <p className="mkt-body">
            My father taught me that simple rule.<br />
            It became the foundation of OpHalo.
          </p>
          <p className="mkt-body">
            We build tools that fill the gaps in everyday service work — without changing the way you already operate.
          </p>
        </div>
      </section>

      <section className="mkt-section mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h2">The gaps we see</h2>
          <div className="mkt-card-grid">

            <div className="mkt-feature-card">
              <div className="mkt-card-icon mkt-icon-neutral" aria-hidden="true">
                <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <rect x="2" y="5" width="16" height="12" rx="1.5" />
                  <polyline points="2,5 10,11 18,5" />
                  <line x1="6" y1="1" x2="14" y2="1" />
                  <line x1="8" y1="3" x2="12" y2="3" />
                </svg>
              </div>
              <h3 className="mkt-card-title">Buried Messages</h3>
              <p className="mkt-card-body">texts, emails, and voicemails that slip through.</p>
            </div>

            <div className="mkt-feature-card">
              <div className="mkt-card-icon mkt-icon-neutral" aria-hidden="true">
                <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <rect x="2" y="2" width="16" height="5" rx="1" />
                  <rect x="2" y="9" width="16" height="5" rx="1" />
                  <rect x="2" y="16" width="16" height="2" rx="1" />
                </svg>
              </div>
              <h3 className="mkt-card-title">Heavy Tools</h3>
              <p className="mkt-card-body">software that forces you into its workflow.</p>
            </div>

            <div className="mkt-feature-card">
              <div className="mkt-card-icon mkt-icon-neutral" aria-hidden="true">
                <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <rect x="2" y="2" width="16" height="12" rx="2" />
                  <polyline points="5,14 3,18 9,14" />
                </svg>
              </div>
              <h3 className="mkt-card-title">Quiet Jobs</h3>
              <p className="mkt-card-body">when customers stop hearing from you.</p>
            </div>

            <div className="mkt-feature-card">
              <div className="mkt-card-icon mkt-icon-neutral" aria-hidden="true">
                <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="10" cy="10" r="8" />
                  <path d="M10 7a2.5 2.5 0 0 1 1 4.8V13" />
                  <circle cx="10" cy="15.5" r="0.75" fill="currentColor" stroke="none" />
                </svg>
              </div>
              <h3 className="mkt-card-title">Missed Trust</h3>
              <p className="mkt-card-body">the moment a customer wonders what&apos;s going on.</p>
            </div>

          </div>
        </div>
      </section>

      <section className="mkt-section mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h2">OpHalo and Keep</h2>
          <p className="mkt-body">
            OpHalo is the company.<br />
            Keep is our first application — built to solve the quiet-job problem.
          </p>
          <p className="mkt-body">
            More OpHalo apps will follow, each designed to fill a different gap without adding complexity.
          </p>
        </div>
      </section>

      <section className="mkt-section mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h2">What We Believe</h2>
          <p className="mkt-body">Software should support your workflow — not replace it.</p>
          <p className="mkt-body">
            You work the same way you always work.<br />
            OpHalo tools help you stay ahead with less chasing, fewer buried messages, and clearer visibility.
          </p>
          <div className="mkt-about-list">
            <p className="mkt-about-list-item">No heavy systems.</p>
            <p className="mkt-about-list-item">No bloated dashboards.</p>
            <p className="mkt-about-list-item">No learning curve.</p>
          </div>
          <p className="mkt-body"><strong>Just clarity.</strong></p>
        </div>
      </section>

      <section className="mkt-section mkt-bg-soft-sand">
        <div className="mkt-container">
          <h2 className="mkt-h2">Why You Can Trust Us</h2>
          <ul className="mkt-bullets">
            <li>20 years in database + software development at a children&apos;s hospital in Tennessee</li>
            <li>A decade of small-business experience through my wife&apos;s company</li>
            <li>Built from both sides: operator and customer</li>
          </ul>
          <p className="mkt-body">OpHalo is grounded in real work, not theory.</p>
        </div>
      </section>

      <section className="mkt-section mkt-bg-warm-white">
        <div className="mkt-container">
          <h2 className="mkt-h2">A Note From Christian</h2>
          <p className="mkt-body">
            I built OpHalo because small businesses deserve software that fits the way they work — not the way software companies think they should work.
          </p>
          <p className="mkt-body">
            If Keep helps you stay on top of communication and earn more trust, then we&apos;ve done our job.
          </p>
          <p className="mkt-about-signature">— Christian</p>
        </div>
      </section>

      <section className="mkt-final-cta">
        <div className="mkt-container">
          <h2 className="mkt-h2">See if Keep fits the way you already work.</h2>
          <p className="mkt-body">Free during the pilot.</p>
          <div className="mkt-cta-group">
            <Link href="/start" className="mkt-btn-primary">Join the Pilot</Link>
          </div>
        </div>
      </section>

    </div>
  );
}
