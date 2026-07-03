# Mobile Store Submission Checklist

Authority document for Apple App Store and Google Play submission readiness.
All items below are required before submitting OpHalo Keep for review.
Most are scoped to S19 (store submission readiness session).

---

## Auth and Security

- [ ] Replace custom URL scheme (`ophalo://`) with Universal Links (Apple Associated Domains)
      and Android App Links before public store release. Custom scheme is an accepted S16/S17
      bridge only (ADR-390).
- [ ] Implement Apple Associated Domains (`apple-app-site-association` on `ophalo.com`)
      and Android App Links (`assetlinks.json`).
- [ ] Reviewer/demo login: Apple and Google reviewers need deterministic access. Either:
      - Seed a reviewer account with documented email access, or
      - Implement a numeric in-app code bypass (deferred from S16).
- [ ] Confirm mobile session lifetime policy (deferred ADR from S16 scope).

## Build and Signing

- [ ] Configure EAS Build profiles (development / preview / production).
- [ ] Set up iOS signing certificates and provisioning profiles in EAS.
- [ ] Set up Android keystore in EAS.
- [ ] Configure `eas.json` with production build profile targeting `com.ophalo.keep`.
- [ ] Run a production EAS build and install on a real device (not simulator) before submission.

## App Store Metadata

- [ ] App icon (1024×1024 for App Store; adaptive icon for Android).
- [ ] Screenshots for all required device sizes (iPhone 6.9", 6.5", iPad if applicable;
      Android phone and tablet).
- [ ] App name, subtitle, description, keywords (App Store Connect).
- [ ] Short description and full description (Google Play Console).
- [ ] Support URL and marketing URL.
- [ ] Version number and build number set correctly in `app.json` / EAS.

## Privacy and Compliance

- [ ] Apple privacy nutrition labels: declare data collected (email address, usage data,
      device identifiers). Do not under-declare — reviewers check.
- [ ] Google Play data safety form: complete all sections including data collection,
      sharing, and security practices.
- [ ] Privacy policy URL accessible from the app store listing and from within the app.
- [ ] Confirm no third-party analytics or tracking SDKs are included (currently clean —
      verify before adding any S17/S18 dependencies).

## TestFlight / Internal Testing

- [ ] Submit a TestFlight build for internal testing before external review.
- [ ] Confirm the full auth flow (magic link → web handoff → app callback → authenticated
      shell) works on a real device, not just simulator.
- [ ] Test on at least one older supported iOS version and one Android version.

## Review Submission

- [ ] Complete App Store Connect review submission form, including:
      - Sign-in credentials for reviewer (see reviewer login item above).
      - Notes for reviewer explaining the magic-link auth flow.
      - Demo video if the reviewer flow is non-obvious.
- [ ] Complete Google Play Console content rating questionnaire.
- [ ] Confirm no placeholder content, debug UI, or dev-only token fields remain in
      the production build (remove dev token paste field from `signin.tsx`).

---

_Last updated: S16e. Revisit at the start of S19._
