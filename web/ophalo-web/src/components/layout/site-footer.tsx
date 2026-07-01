import Image from "next/image";
import Link from "next/link";

export function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="container">
        <Link href="/" className="footer-logo" aria-label="OpHalo home">
          <Image
            src="/brand/ophalo-lockup-color.svg"
            alt="OpHalo"
            width={87}
            height={28}
            className="site-logo-lockup"
          />
        </Link>
        <div className="footer-bottom">
          <span className="footer-copyright">© OpHalo LLC</span>
          <a href="tel:+19013134063" className="footer-contact">(901) 313-4063</a>
          <nav className="footer-links">
            <Link href="/about">About</Link>
            <Link href="/privacy">Privacy</Link>
            <Link href="/terms">Terms</Link>
          </nav>
        </div>
      </div>
    </footer>
  );
}
