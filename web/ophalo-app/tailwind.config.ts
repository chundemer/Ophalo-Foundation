import type { Config } from "tailwindcss";

export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      fontFamily: {
        sans: [
          "Inter Variable",
          "Inter",
          "ui-sans-serif",
          "system-ui",
          "sans-serif",
        ],
        serif: [
          "Source Serif 4 Variable",
          "Source Serif 4",
          "ui-serif",
          "Georgia",
          "serif",
        ],
      },
    },
  },
} satisfies Config;
