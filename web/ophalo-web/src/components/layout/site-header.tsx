import Image from "next/image";
import Link from "next/link";
import { NavLink } from "@/components/layout/nav-link";

export function SiteHeader() {
  return (
    <header className="site-header">
      <div className="container">
        <Link href="/" className="logo" aria-label="OpHalo home">
          <Image
            src="/brand/ophalo-lockup-color.svg"
            alt="OpHalo"
            width={112}
            height={36}
            priority
            className="site-logo-lockup"
          />
        </Link>
        <div className="site-header-right">
          <NavLink href="/about">About</NavLink>
          <Link href="/signin" className="site-nav-link">
            Sign in
          </Link>
          <Link href="/start" className="pilot-access">
            Join the Pilot
          </Link>
        </div>
      </div>
    </header>
  );
}
