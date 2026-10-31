import { Marked } from "marked";
import { el } from "./fiche";
import { langue, t } from "./langue";

/** Le README de la version installée, lu par le serveur, sans HTML brut (seul le Markdown est rendu). */
const rendu = new Marked({
  gfm: true,
  renderer: {
    html: ({ text }) => text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"),
  },
});

/**
 * Fenêtre du guide : le README dans la langue de l'interface (README.md en français, README.en.md sinon), avec ses images.
 * Les liens vers l'extérieur s'ouvrent dans un nouvel onglet ; le lien vers l'autre langue change la langue du guide.
 */
export class Guide {
  private readonly dialogue = el("dialog", "dialogue guide");
  private readonly corps = el("div", "guide-corps");
  private readonly titre = el("h2", undefined, t("guide.titre"));

  constructor() {
    const tete = el("div", "tete");
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => this.dialogue.close());
    tete.append(this.titre, fermer);
    this.dialogue.append(tete, this.corps);
    this.corps.addEventListener("click", (ev) => {
      const lien = (ev.target as HTMLElement).closest("a");
      if (!lien) return;
      const cible = lien.getAttribute("href") ?? "";
      if (/^README(\.[a-z]{2})?\.md$/.test(cible)) {
        ev.preventDefault();
        void this.charger(cible === "README.md" ? "fr" : cible.slice(7, 9));
      } else if (cible.startsWith("#")) {
        ev.preventDefault();
        this.corps.querySelector(`[id="${CSS.escape(decodeURIComponent(cible.slice(1)))}"]`)?.scrollIntoView();
      }
    });
    document.body.append(this.dialogue);
  }

  async ouvrir(): Promise<void> {
    this.titre.textContent = t("guide.titre");
    this.dialogue.showModal();
    await this.charger(langue());
  }

  private async charger(code: string): Promise<void> {
    this.corps.textContent = t("config.lecture");
    const reponse = await fetch(`/api/guide?langue=${encodeURIComponent(code)}`).catch(() => null);
    if (!reponse?.ok) {
      this.corps.textContent = t("guide.absent");
      return;
    }
    const html = await rendu.parse(await reponse.text());
    this.corps.innerHTML = html;
    for (const img of this.corps.querySelectorAll("img")) {
      const src = img.getAttribute("src") ?? "";
      if (src.startsWith("docs/")) img.src = `/api/guide/${src}`;
      img.loading = "lazy";
    }
    for (const a of this.corps.querySelectorAll("a")) {
      const href = a.getAttribute("href") ?? "";
      if (/^https?:/.test(href)) {
        a.target = "_blank";
        a.rel = "noopener noreferrer";
      }
    }
    for (const h of this.corps.querySelectorAll("h1, h2, h3")) {
      h.id = (h.textContent ?? "").toLowerCase().trim().replace(/[^\p{L}\p{N}\s-]/gu, "").replace(/\s+/g, "-");
    }
    this.corps.scrollTop = 0;
  }
}
