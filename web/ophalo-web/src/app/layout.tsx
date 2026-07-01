import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "OpHalo",
  description: "Quiet Intelligence. Clear Decisions.",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body className="flex min-h-screen flex-col bg-ophalo-canvas font-sans text-ophalo-ink antialiased">{children}</body>
    </html>
  );
}
