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

/** Une bête connue des humains (FauveDto, 0.45.1) : celle qui a attaqué. `groupe` : son numéro dans le flux Camp de la vue 3D. */
export interface FauveConnu {
  id: number;
  espece: number;
  nom: string;
  x: number;
  y: number;
  attaques: number;
  victimes: number;
  enVie: boolean;
  vueLe: string;
  groupe: number;
  details: string[];
}

/** Une attaque de l'année écoulée (AttaqueCarteDto, 0.45.1). */
export interface AttaqueCarte {
  x: number;
  y: number;
  jours: number;
  espece: number;
  mort: boolean;
  evenement: number;
  groupe: number;
}

interface PopulationJson {
  especes: EspecePopulation[];
  gibier: string;
  kgVifParPart: number;
  troupeaux: TroupeauCarte[];
  connus?: FauveConnu[] | null;
  attaques?: AttaqueCarte[] | null;
}

/** Carnassiers pour un kilo de proies (Carbone et Gittleman, 2002), comme dans la simulation (0.45.0). */
const CARNASSIERS_PAR_KG = 0.009;

/** Forme des grands nageurs (crocodiliens, 0.45.0). */
export const FORME_NAGEUR = 8;

/** La population lue, avec le gibier de chaque case décodé et les espèces de chaque terrain. */
export class Population {
  readonly parNumero = new Map<number, EspecePopulation>();
  private readonly parBiome = new Map<number, EspecePopulation[]>();
  readonly predateurs: boolean;

  private constructor(
    readonly especes: EspecePopulation[],
    readonly gibier: Uint16Array,
    readonly kgVifParPart: number,
    readonly troupeaux: TroupeauCarte[],
    readonly connus: FauveConnu[] = [],
    readonly attaques: AttaqueCarte[] = [],
  ) {
    // Monde où vivent les prédateurs (0.45.0) : le serveur envoie les grands nageurs, et compte les carnassiers d'après les proies.
    this.predateurs = especes.some((e) => e.forme === FORME_NAGEUR);
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
    return new Population(json.especes, gibier, json.kgVifParPart, json.troupeaux, json.connus ?? [], json.attaques ?? []);
  }

  /** Espèces qui vivent sur ce terrain. */
  especesDe(biome: number): EspecePopulation[] {
    return this.parBiome.get(biome) ?? [];
  }

  /**
   * Bêtes sauvages estimées d'une espèce sur une case (même règle que le serveur : le gibier partagé à parts égales entre les
   * espèces du terrain, en poids vivant, divisé par le poids d'une bête). Depuis la 0.45.0, là où vivent les prédateurs, les
   * carnassiers se comptent d'après les proies (environ 9 kg pour une tonne) et le gibier se partage entre les autres espèces ;
   * la carte suppose les proies à leur niveau, le tableau de la fenêtre Faune donne le compte exact.
   */
  surCase(i: number, biome: number, e: EspecePopulation): number {
    const l = this.especesDe(biome);
    if (!l.includes(e)) return 0;
    const kg = this.gibier[i] * this.kgVifParPart;
    if (!this.predateurs) return kg / l.length / Math.max(1, e.poids);
    if (e.forme === FORME_NAGEUR) return (Math.max(this.gibier[i], 300) * this.kgVifParPart * CARNASSIERS_PAR_KG) / Math.max(1, e.poids);
    if (e.regime === 2) return (kg * CARNASSIERS_PAR_KG) / Math.max(1, e.poids);
    const proies = l.filter((x) => x.regime !== 2 && x.forme !== FORME_NAGEUR).length;
    return proies > 0 ? kg / proies / Math.max(1, e.poids) : 0;
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
  /** 0.45.1 : les bêtes connues des humains et les attaques de l'année. */
  private readonly connus = el("div", "fauves-connus");
  private tri: Colonne = "sauvages";
  private descendant = true;
  private population: Population | null = null;
  private minuteur?: number;

  constructor(
    private readonly racine: HTMLElement,
    /** Montre l'emplacement d'une espèce sur la carte (0 : toutes). */
    private readonly montrer: (numero: number) => void,
    /** 0.45.1 : va voir en 3D une espèce (groupe 0 : la plus proche), ou une bête connue (son groupe dans le flux Camp, sa case). */
    private readonly allerVoir: ((espece: number, groupe: number, x: number, y: number) => void) | null = null,
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
    this.corps.append(el("p", "discret", t("faune.aide")), this.resume, toutes, defile, this.connus, el("p", "discret", t("faune.estimation")),
      el("p", "discret", t("faune.decor")));
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
      if (this.allerVoir && !e.disparue) {
        const voir = el("button", "lien voir3d", " 🎥");
        voir.type = "button";
        voir.title = t("faune.allerVoir");
        voir.addEventListener("click", () => this.allerVoir?.(e.numero, 0, -1, -1));
        tdNom.append(voir);
      }
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
    this.afficherConnus(p);
  }

  /** Les bêtes connues (celles qui ont attaqué), les vivantes d'abord, et ce qu'on a vu cette année (0.45.1). */
  private afficherConnus(p: Population): void {
    this.connus.replaceChildren();
    if (!p.predateurs) return;
    const morts = p.attaques.filter((a) => a.mort).length;
    this.connus.append(el("h3", undefined, t("faune.connus")),
      el("p", "discret", p.attaques.length === 0 ? t("faune.attaquesAucune") : t("faune.attaques", { n: p.attaques.length, m: morts })));
    if (p.connus.length === 0) {
      this.connus.append(el("p", "discret", t("faune.connusAucun")));
      return;
    }
    const ul = el("ul", "liste-fauves");
    for (const f of p.connus) {
      const li = el("li", f.enVie ? undefined : "discret");
      const titre = el("b", undefined, f.nom.charAt(0).toUpperCase() + f.nom.slice(1));
      li.append(titre, ` · ${t("faune.connuVue", { date: f.vueLe })}`);
      if (f.enVie && this.allerVoir) {
        const voir = el("button", "lien voir3d", ` 🎥 ${t("faune.allerVoirBete")}`);
        voir.type = "button";
        voir.addEventListener("click", () => this.allerVoir?.(f.espece, f.groupe, f.x, f.y));
        li.append(voir);
      }
      li.append(el("div", "discret", f.details.join(" ")));
      ul.append(li);
    }
    this.connus.append(ul);
  }
}
