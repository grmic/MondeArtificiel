import { Container, Graphics, type Sprite } from "pixi.js";
import { connexion } from "./connexion";
import { nombre, t } from "./langue";
import { calqueLisse, type VueCarte } from "./carte";

/** Terrain et mer, tels que renvoyés par le hub (méthode « Terrain », voir TerrainDto côté serveur). */
interface TerrainJson {
  largeur: number;
  hauteur: number;
  roches: string;
  sols: string;
  pente: string;
  debit: string;
  profondeur: string;
  rivages: string;
  recifs: string;
  lits: number[];
  nomsRoches: string[];
  nomsSols: string[];
  nomsRivages: string[];
  grottes: { x: number; y: number; sousRoche: boolean; metres: number; source: boolean }[];
  sources: { x: number; y: number; type: "Douce" | "Chaude" | "Salee"; nom: string }[];
  gisements: { x: number; y: number; type: string; profondeur: string; connu: number }[];
}

interface Terrain extends Omit<TerrainJson, "roches" | "sols" | "pente" | "debit" | "profondeur" | "rivages" | "recifs"> {
  roches: Uint8Array;
  sols: Uint8Array;
  pente: Uint8Array;
  debit: Uint8Array;
  profondeur: Uint8Array;
  rivages: Uint8Array;
  recifs: Uint8Array;
}

const CALQUES = ["roches", "sols", "eau", "fonds", "grottes", "gisements"] as const;
type Calque = (typeof CALQUES)[number];

/** Couleurs des roches, dans l'ordre de l'énumération Roche : granite, basalte, volcanique, calcaire, craie, grès, schiste, alluvions, évaporites. */
const COULEURS_ROCHES: [number, number, number][] = [
  [196, 120, 140], [70, 70, 80], [150, 40, 40], [140, 190, 230], [235, 235, 210], [220, 160, 90], [110, 120, 150], [240, 220, 120], [250, 250, 255],
];

/** Couleurs des sols, dans l'ordre de l'énumération Sol (le premier, sans sol, n'est pas peint). */
const COULEURS_SOLS: [number, number, number][] = [
  [0, 0, 0], [90, 170, 70], [60, 50, 40], [140, 100, 60], [200, 190, 120], [210, 210, 170], [230, 210, 140], [190, 70, 40],
  [60, 40, 30], [150, 150, 150], [230, 200, 150], [250, 250, 250],
];

function decoder(texte: string): Uint8Array {
  return Uint8Array.from(atob(texte), (c) => c.charCodeAt(0));
}

/** Une image lissée (voir calqueLisse), peinte case par case par une fonction qui rend [r, g, b, a] ou rien. */
function image(largeur: number, hauteur: number, peindre: (i: number) => [number, number, number, number] | null): Sprite {
  const canvas = document.createElement("canvas");
  canvas.width = largeur;
  canvas.height = hauteur;
  const ctx = canvas.getContext("2d")!;
  const donnees = ctx.createImageData(largeur, hauteur);
  for (let i = 0; i < largeur * hauteur; i++) {
    const c = peindre(i);
    if (!c) continue;
    donnees.data.set(c, i * 4);
  }
  ctx.putImageData(donnees, 0, 0);
  return calqueLisse(canvas);
}

/**
 * Calques du terrain sur la carte (roches, sols, eaux, fonds marins, grottes et sources, gisements), à cocher dans un petit
 * panneau ; le choix est retenu par le navigateur. Le survol d'une case dit sa roche, son sol, sa pente, sa rivière, sa mer,
 * sa grotte et son gisement, et combien de vivants le connaissent.
 */
export class Calques {
  private terrain?: Terrain;
  private readonly choix = new Set<Calque>();
  private minuteur?: number;

  constructor(
    private readonly vue: VueCarte,
    private readonly bouton: HTMLButtonElement,
    private readonly panneau: HTMLElement,
  ) {
    try {
      for (const c of (localStorage.getItem("monde.calques") ?? "").split(",")) if ((CALQUES as readonly string[]).includes(c)) this.choix.add(c as Calque);
    } catch {
      // Stockage indisponible : aucun calque au départ.
    }
    for (const c of CALQUES) {
      const label = document.createElement("label");
      const caseACocher = document.createElement("input");
      caseACocher.type = "checkbox";
      caseACocher.checked = this.choix.has(c);
      caseACocher.addEventListener("change", () => {
        if (caseACocher.checked) this.choix.add(c);
        else this.choix.delete(c);
        try {
          localStorage.setItem("monde.calques", [...this.choix].join(","));
        } catch {
          // Le choix vaut pour cette page seulement.
        }
        this.dessiner();
      });
      label.append(caseACocher, ` ${t(`calques.${c}`)}`);
      label.title = t(`calques.${c}Aide`);
      this.panneau.append(label);
    }
    this.bouton.classList.toggle("actif", this.choix.size > 0);
    this.bouton.addEventListener("click", () => {
      this.panneau.style.top = `${Math.round(this.bouton.getBoundingClientRect().bottom + 8)}px`;
      this.panneau.hidden = !this.panneau.hidden;
      if (!this.terrain) void this.recharger();
    });
    // Ce que les humains savent des gisements change : relu toutes les 30 secondes.
    this.minuteur = window.setInterval(() => void this.recharger(), 30_000);
  }

  /** Relit le terrain du monde courant (après un changement de carte ou de langue) et redessine. */
  async recharger(): Promise<void> {
    const json = await connexion.invoke<TerrainJson>("Terrain").catch(() => null);
    if (!json) return;
    this.terrain = {
      ...json,
      roches: decoder(json.roches), sols: decoder(json.sols), pente: decoder(json.pente), debit: decoder(json.debit),
      profondeur: decoder(json.profondeur), rivages: decoder(json.rivages), recifs: decoder(json.recifs),
    };
    this.dessiner();
  }

  arreter(): void {
    window.clearInterval(this.minuteur);
  }

  private dessiner(): void {
    this.bouton.classList.toggle("actif", this.choix.size > 0);
    const tr = this.terrain;
    if (!tr || this.choix.size === 0 || this.vue.largeurCarte() !== tr.largeur) {
      this.vue.afficherCalques(null);
      return;
    }
    const { largeur: l, hauteur: h } = tr;
    const dessin = new Container();
    if (this.choix.has("fonds")) {
      // Mer : plus sombre avec la profondeur ; plateau clair, récifs roses.
      dessin.addChild(image(l, h, (i) => {
        if (tr.recifs[i]) return [255, 120, 170, 220];
        const p = tr.profondeur[i];
        if (p === 0) return null;
        const f = Math.min(1, Math.log10(1 + p) / 2.5);
        return [Math.round(120 * (1 - f)), Math.round(210 - 150 * f), Math.round(240 - 120 * f), 210];
      }));
    }
    if (this.choix.has("roches")) {
      dessin.addChild(image(l, h, (i) => {
        if (tr.profondeur[i] > 0) return null;
        const c = COULEURS_ROCHES[tr.roches[i]];
        return c ? [c[0], c[1], c[2], 170] : null;
      }));
    }
    if (this.choix.has("sols")) {
      dessin.addChild(image(l, h, (i) => {
        if (tr.sols[i] === 0) return null;
        const c = COULEURS_SOLS[tr.sols[i]];
        return c ? [c[0], c[1], c[2], 170] : null;
      }));
    }
    if (this.choix.has("eau")) {
      // Rivières : un trait de case en case vers l'aval, d'autant plus épais que le débit est fort.
      const g = new Graphics();
      for (let k = 0; k + 1 < tr.lits.length; k += 2) {
        const a = tr.lits[k], b = tr.lits[k + 1];
        const epaisseur = 0.15 + tr.debit[a] / 60;
        g.moveTo((a % l) + 0.5, Math.floor(a / l) + 0.5).lineTo((b % l) + 0.5, Math.floor(b / l) + 0.5)
          .stroke({ width: epaisseur, color: 0x5fd0ff, alpha: 0.9, cap: "round" });
      }
      for (const s of tr.sources) {
        g.circle(s.x + 0.5, s.y + 0.5, 0.45).fill({ color: s.type === "Chaude" ? 0xff7a50 : s.type === "Salee" ? 0xffffff : 0x5fd0ff });
      }
      dessin.addChild(g);
    }
    if (this.choix.has("grottes")) {
      const g = new Graphics();
      for (const c of tr.grottes) {
        const x = c.x + 0.5, y = c.y + 0.5, r = c.sousRoche ? 0.4 : 0.6;
        g.poly([x, y - r, x + r, y + r, x - r, y + r]).fill({ color: c.sousRoche ? 0x8a7a66 : 0x1a1a1a }).stroke({ width: 0.12, color: 0xffffff, alpha: 0.7 });
      }
      dessin.addChild(g);
    }
    if (this.choix.has("gisements")) {
      // Losange doré ; plein si au moins un vivant s'en souvient.
      const g = new Graphics();
      for (const gis of tr.gisements) {
        const x = gis.x + 0.5, y = gis.y + 0.5, r = 0.55;
        g.poly([x, y - r, x + r, y, x, y + r, x - r, y]);
        if (gis.connu > 0) g.fill({ color: 0xffd040 });
        g.stroke({ width: 0.15, color: 0xffd040 });
      }
      dessin.addChild(g);
    }
    this.vue.afficherCalques(dessin);
  }

  /** Ce que porte une case, en lignes de texte (rien tant que le terrain n'est pas chargé). */
  texteEn(x: number, y: number): string[] {
    const tr = this.terrain;
    if (!tr || x < 0 || y < 0 || x >= tr.largeur || y >= tr.hauteur) return [];
    const i = y * tr.largeur + x;
    const lignes: string[] = [];
    if (tr.profondeur[i] > 0) {
      lignes.push(t(tr.recifs[i] ? "calques.merRecif" : "calques.mer", { m: nombre(tr.profondeur[i] * 10) }));
    } else {
      lignes.push(t("calques.sol", { roche: tr.nomsRoches[tr.roches[i]] ?? "?", sol: tr.nomsSols[tr.sols[i]] ?? "?", pente: nombre(tr.pente[i] * 40) }));
      if (tr.rivages[i]) lignes.push(t("calques.rivage", { rivage: tr.nomsRivages[tr.rivages[i]] ?? "" }));
    }
    if (tr.debit[i] > 0) lignes.push(t("calques.riviere", { q: nombre(Math.pow(10, (tr.debit[i] - 1) / 40) - 1, 1) }));
    for (const c of tr.grottes) if (c.x === x && c.y === y) lignes.push(t(c.sousRoche ? "calques.abri" : c.source ? "calques.grotteSource" : "calques.grotte", { m: c.metres }));
    for (const s of tr.sources) if (s.x === x && s.y === y) lignes.push(t("calques.source", { type: s.nom }));
    for (const g of tr.gisements) {
      if (g.x === x && g.y === y) lignes.push(t("calques.gisement", { type: g.type, profondeur: g.profondeur, n: g.connu }));
    }
    return lignes;
  }
}
