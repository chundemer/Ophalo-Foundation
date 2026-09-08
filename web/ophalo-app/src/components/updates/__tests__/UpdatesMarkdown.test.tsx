import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { UpdatesMarkdown, renderUpdatesHtml } from "../UpdatesMarkdown";

describe("renderUpdatesHtml — sanitisation", () => {
  it("keeps allowed inline formatting", () => {
    const html = renderUpdatesHtml("A **bold** and _em_ and a [link](https://ophalo.com).");
    expect(html).toContain("<strong>bold</strong>");
    expect(html).toContain("<em>em</em>");
    expect(html).toContain('href="https://ophalo.com"');
  });

  it("drops script, iframe, event handlers, headings and tables", () => {
    const html = renderUpdatesHtml(
      '<script>alert(1)</script><iframe src="x"></iframe><img src="guides/img/a.png" alt="x" onerror="alert(1)"><h1>Title</h1><table><tr><td>c</td></tr></table>',
    );
    expect(html).not.toContain("<script");
    expect(html).not.toContain("<iframe");
    expect(html).not.toContain("onerror");
    expect(html).not.toContain("<h1");
    expect(html).not.toContain("<table");
  });

  it("drops javascript: and data: hrefs but keeps the link text", () => {
    const js = renderUpdatesHtml("[x](javascript:alert(1))");
    expect(js).not.toContain("javascript:");
    expect(js).toContain("x");
    const data = renderUpdatesHtml("[y](data:text/html;base64,abcd)");
    expect(data).not.toContain("data:");
  });

  it("rewrites a guides/img path to the backend proxy and adds loading=lazy", () => {
    const html = renderUpdatesHtml("![A diagram](guides/img/record-work-a1b2c3d4.png)");
    expect(html).toContain('src="/updates/guides/img/record-work-a1b2c3d4.png"');
    expect(html).toContain('loading="lazy"');
    expect(html).toContain('alt="A diagram"');
  });

  it("drops an image whose src is not a guides/img path", () => {
    const html = renderUpdatesHtml('![x](https://evil.example/pixel.png)');
    expect(html).not.toContain("<img");
  });

  it("drops a guide image with an empty alt", () => {
    const html = renderUpdatesHtml("![](guides/img/a.png)");
    expect(html).not.toContain("<img");
  });
});

describe("UpdatesMarkdown — broken image handling", () => {
  it("hides an <img> that fires an error event, leaving the surrounding text intact", () => {
    const { container } = render(
      <UpdatesMarkdown markdown={"Before diagram. ![A diagram](guides/img/a.png) After diagram."} />,
    );
    const md = container.querySelector(".updates-md")!;
    expect(md.textContent).toContain("Before diagram.");
    const img = container.querySelector("img")!;
    expect(img).not.toHaveAttribute("hidden");

    img.dispatchEvent(new Event("error"));

    expect(img).toHaveAttribute("hidden");
    expect(md.textContent).toContain("After diagram.");
  });
});
