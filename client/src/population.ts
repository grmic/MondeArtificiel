import { connexion } from "./connexion";
import { el } from "./fiche";
import { nombre, t } from "./langue";

/** Une espèce et sa population (PopulationEspeceDto côté serveur, 0.44.2). */
export interface EspecePopulation {
  numero: number;
  nom: string;
  biome: string;
  biomeNumero: number;
  forme: number;
  poids: number;
  regime: number;
  /** Bêtes sauvages estimées d'après le gibier ; il y a `mois` mois (null : pas encore de relevé assez ancien). */
  sauvages: number;
  sauvagesAvant: number | null;
  mois: number;
  gardeesAdultes: number;
  gardeesPetits: number;
  troupeaux: number;
  domestique: boolean;
  disparue: boolean;
}

export interface TroupeauCarte {
  x: number;
  y: number;
  espece: number;
  adultes: number;
  petits: number;
  domestique: boolean;
}

interface PopulationJson {
  especes: EspecePopulation[];
  gibier: string;
  kgVifParPart: number;
  troupeaux: TroupeauCarte[];
}

/** La population lue, avec le gibier de chaque case décodé et les espèces de chaque terrain. */
export class Population {
  readonly parNumero = new Map<number, EspecePopulation>();
  private readonly parBiome = new Map<number, EspecePopulation[]>();

  private constructor(
    readonly especes: EspecePopulation[],
    readonly gibier: Uint16Array,
    readonly kgVifParPart: number,
    readonly troupeaux: TroupeauCarte[],
  ) {
    for (const e of especes) {
      this.parNumero.set(e.numero, e);
      if (e.disparue) continue;
      const l = this.parBiome.get(e.biomeNumero) ?? [];
      l.push(e);
      this.parBiome.set(e.biomeNumero, l);
    }
  }

  static async lire(): Promise<Population | null> {
    const json = await connexion.invoke<PopulationJson>("PopulationFaune").catch(() => null);
    if (!json) return null;
    const octets = Uint8Array.from(atob(json.gibier), (c) => c.charCodeAt(0));
    const gibier = new Uint16Array(octets.length / 2);
    for (let i = 0; i < gibier.length; i++) gibier[i] = octets[2 * i] | (octets[2 * i + 1] << 8);
    return new Population(json.especes, gibier, json.kgVifParPart, json.troupeaux);
  }

  /** Espèces qui vivent sur ce terrain. */
  especesDe(biome: number): EspecePopulation[] {
    return this.parBiome.get(biome) ?? [];
  }

  /**
   * Bêtes sauvages estimées d'une espèce sur une case (même règle que le serveur : le gibier partagé à parts égales entre les
   * espèces du terrain, en poids vivant, divisé par le poids d'une bête).
   */
  surCase(i: number, biome: number, e: EspecePopulation): number {
    const l = this.especesDe(biome);
    if (!l.includes(e)) return 0;
    return (this.gibier[i] * this.kgVifParPart) / l.length / Math.max(1, e.poids);
  }
}

/**
 * Couleur d'une espèce sur la carte, tirée de son numéro : du violet au jaune en passant par le rouge, pour ne pas se confondre
 * avec les verts, les bruns et les bleus du terrain.
 */
export function couleurEspece(numero: number): number {
  const h = (270 + ((numero * 61.8) % 150)) % 360;
  const c = 0.75;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  const m = 0.2;
  return (Math.round((r + m) * 255) << 16) | (Math.round((g + m) * 255) << 8) | Math.round((b + m) * 255);
}

/** Un nombre estimé, arrondi à ce qu'on peut dire : « environ 12 000 ». */
export function environ(n: number): string {
  if (n < 1) return n > 0 ? "< 1" : "0";
  const chiffres = Math.max(0, Math.floor(Math.log10(n)) - 1);
  const p = Math.pow(10, chiffres);
  return nombre(Math.round(n / p) * p);
}

type Colonne = "nom" | "biome" | "poids" | "regime" | "sauvages" | "tendance" | "gardees";

const ACTUALISATION_MS = 15000;

/**
 * Fenêtre « Faune » (0.44.2, choix A1, B1, D1, E1) : chaque espèce du monde avec ses bêtes sauvages estimées, leur tendance sur
 * un an, et ses bêtes gardées ; on trie par colonne ; un clic sur une espèce la montre sur la carte.
 */
export class PanneauFaune {
  private readonly corps = el("div", "corps");
  private readonly tableau = el("table", "tableau-faune");
  private readonly resume = el("p", "discret");
  private tri: Colonne = "sauvages";
  private descendant = true;
  private population: Population | null = null;
  private minuteur?: number;

  constructor(
    private readonly racine: HTMLElement,
    /** Montre l'emplacement d'une espèce sur la carte (0 : toutes). */
    private readonly montrer: (numero: number) => void,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, t("faune.titre")));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    const toutes = el("button", "lien", t("faune.voirToutes"));
    toutes.type = "button";
    toutes.addEventListener("click", () => this.montrer(0));
    const defile = el("div", "defile-faune");
    defile.append(this.tableau);
    this.corps.append(el("p", "discret", t("faune.aide")), this.resume, toutes, defile, el("p", "discret", t("faune.estimation")), el("p", "discret", t("faune.decor")));
    racine.append(tete, this.corps);
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else void this.ouvrir();
  }

  async ouvrir(): Promise<void> {
    this.racine.hidden = false;
    await this.charger();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.charger(), ACTUALISATION_MS);
  }

  fermer(): void {
    this.racine.hidden = true;
    window.clearInterval(this.minuteur);
  }

  private async charger(): Promise<void> {
    const p = await Population.lire();
    if (!this.ouvert) return;
    if (!p) {
      this.tableau.replaceChildren(el("caption", "discret", t("commun.pasDeReponse")));
      return;
    }
    this.population = p;
    this.afficher();
  }

  private valeur(e: EspecePopulation, c: Colonne): number | string {
    switch (c) {
      case "nom": return e.nom;
      case "biome": return e.biome;
      case "poids": return e.poids;
      case "regime": return e.regime;
      case "sauvages": return e.sauvages;
      case "tendance": return e.sauvagesAvant ? e.sauvages / e.sauvagesAvant - 1 : -Infinity;
      case "gardees": return e.gardeesAdultes + e.gardeesPetits;
    }
  }

  private afficher(): void {
    const p = this.population;
    if (!p) return;
    const vivantes = p.especes.filter((e) => !e.disparue);
    const sauvages = vivantes.reduce((s, e) => s + e.sauvages, 0);
    const gardees = p.especes.reduce((s, e) => s + e.gardeesAdultes + e.gardeesPetits, 0);
    this.resume.textContent = t("faune.resume", { especes: vivantes.length, sauvages: environ(sauvages), gardees: nombre(gardees), troupeaux: p.troupeaux.length });

    const colonnes: Colonne[] = ["nom", "biome", "poids", "regime", "sauvages", "tendance", "gardees"];
    const tete = el("tr");
    for (const c of colonnes) {
      const th = el("th");
      const b = el("button", "lien", t(`faune.col.${c}`) + (this.tri === c ? (this.descendant ? " ▼" : " ▲") : ""));
      b.type = "button";
      b.addEventListener("click", () => {
        this.descendant = this.tri === c ? !this.descendant : c !== "nom" && c !== "biome";
        this.tri = c;
        this.afficher();
      });
      th.append(b);
      tete.append(th);
    }
    const lignes = [...p.especes].sort((a, b) => {
      const va = this.valeur(a, this.tri);
      const vb = this.valeur(b, this.tri);
      const r = typeof va === "string" ? va.localeCompare(vb as string) : va - (vb as number);
      return this.descendant ? -r : r;
    }).map((e) => {
      const tr = el("tr", e.disparue ? "discret" : undefined);
      const nom = el("button", "lien", e.nom.charAt(0).toUpperCase() + e.nom.slice(1));
      nom.type = "button";
      nom.title = t("faune.montrer");
      nom.addEventListener("click", () => this.montrer(e.numero));
      const pastille = el("span", "pastille-faune");
      pastille.style.background = `#${couleurEspece(e.numero).toString(16).padStart(6, "0")}`;
      const tdNom = el("td");
      tdNom.append(pastille, nom);
      if (e.disparue) tdNom.append(" · ", t("faune.disparue"));
      const tendance = el("td");
      if (e.sauvagesAvant !== null && e.sauvagesAvant > 0) {
        const ecart = e.sauvages / e.sauvagesAvant - 1;
        const fleche = ecart > 0.02 ? "▲" : ecart < -0.02 ? "▼" : "=";
        tendance.textContent = `${fleche} ${ecart > 0 ? "+" : ""}${nombre(Math.round(ecart * 100))} % · ${t("faune.mois", { n: e.mois })}`;
        tendance.className = ecart > 0.02 ? "hausse" : ecart < -0.02 ? "baisse" : "";
      } else tendance.textContent = "…";
      tendance.title = t("faune.tendanceAide");
      const garde = e.gardeesAdultes + e.gardeesPetits > 0
        ? t("faune.gardees", { a: e.gardeesAdultes, p: e.gardeesPetits, n: e.troupeaux }) + (e.domestique ? ` · ${t("faune.domestiquee")}` : "")
        : "—";
      tr.append(
        tdNom,
        el("td", undefined, e.biome),
        el("td", "nombre", `${nombre(e.poids)} kg`),
        el("td", undefined, t(`faune.regime.${e.regime}`)),
        el("td", "nombre", e.disparue ? "0" : `≈ ${environ(e.sauvages)}`),
        tendance,
        el("td", undefined, garde),
      );
      return tr;
    });
    this.tableau.replaceChildren(el("thead"), el("tbody"));
    this.tableau.tHead!.append(tete);
    this.tableau.tBodies[0].append(...lignes);
  }
}
