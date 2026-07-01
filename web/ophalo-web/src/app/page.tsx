import Image from "next/image";

export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-8 px-4">
      <Image
        src="/brand/ophalo-lockup-color.svg"
        alt="OpHalo"
        width={200}
        height={48}
        priority
      />
      <p className="text-ophalo-ink/60 text-lg">
        Quiet Intelligence. Clear Decisions.
      </p>
    </main>
  );
}
