import { connexion } from "./connexion";
import type { VueCarte } from "./carte";
import { boutonCause, el } from "./fiche";

export interface CatastropheVue {
  id: number;
  type: string;
  libelle: string;
  nom: string;
  origine: "naturelle" | "entite";
  secondes: number;
  debut: string;
  fin: string | null;
  active: boolean;
  x: number;
  y: number;
  rayon: number;
  intensite: string;
  morts: number;
  blesses: number;
  surface: string | null;
  communautes: { id: number; nom: string }[];
  temoins: number;
  phenomene: boolean;
}

export interface DetailCatastrophe {
  catastrophe: CatastropheVue;
  cases: number[];
  front: number[];
}

export interface Geologie {
  contexte: string;
  description: string;
  seismesFortsParSiecle: number;
  magnitudeMax: number;
  failles: { x1: number; y1: number; x2: number; y2: number; sousMarine: boolean }[];
  volcans: { numero: number; nom: string; x: number; y: number; reposMoyenAns: number; enEruption: boolean }[];
  anomalie: number;
}

interface Maladie {
  id: number;
  nom: string;
  apparueLe: string;
  r0: number;
  letalite: number;
  incubationJours: number;
  dureeJours: number;
  immunite: string;
  malades: number;
  immunises: number;
  morts: number;
  evenement: number;
}

export interface Alerte {
  catastrophe: number;
  type: string;
  texte: string;
  grave: boolean;
}

/** Ce que l'Entité peut provoquer, avec l'intensité proposée et ce qu'elle mesure. */
const PROVOQUABLES: [string, string, number, string][] = [
  ["seisme", "Séisme", 6.5, "magnitude (3 à 9,5)"],
  ["eruption", "Éruption du volcan le plus proche", 3, "indice d'explosivité (1 à 7)"],
  ["tsunami", "Vague géante", 8, "hauteur en mètres"],
  ["crue", "Crue", 2, "ampleur (1 à 3)"],
  ["incendie", "Incendie", 0, ""],
  ["cyclone", "Tempête", 3, "catégorie (1 à 5)"],
  ["froid", "Vague de froid", 8, "°C de moins"],
  ["canicule", "Canicule", 7, "°C de plus"],
  ["epidemie", "Maladie nouvelle (le plus proche)", 0, ""],
  ["eclipse", "Éclipse totale", 0, ""],
  ["comete", "Comète", 0, ""],
  ["aurore", "Aurore", 0, ""],
  ["bolide", "Boule de feu", 0, ""],
  ["impact", "Astéroïde", 10, "mégatonnes"],
];

/**
 * Catastrophes et phénomènes du monde : en cours d'abord, puis l'histoire ; un clic montre les terres touchées sur la carte.
 * En bas : la géologie du continent (failles, volcans), les maladies apparues, et ce que l'Entité peut provoquer.
 */
export class PanneauCatastrophes {
  private liste: CatastropheVue[] = [];
  private choisie = 0;
  private communaute: { id: number; nom: string } | null = null;
  private geologie?: Geologie;
  private minuteur?: number;
  private viser = false;
  private cible: { x: number; y: number } | null = null;
  private readonly corps = el("div", "corps");

  constructor(
    private readonly racine: HTMLElement,
    private readonly vue: VueCarte,
    private readonly ouvrirCommunaute: (id: number) => void,
    private readonly message: (texte: string) => void,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, "Catastrophes"));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    racine.append(tete, this.corps);
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  /** Vrai quand le prochain clic sur la carte choisit le lieu d'une catastrophe provoquée. */
  get enVisee(): boolean {
    return this.ouvert && this.viser;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else void this.ouvrir();
  }

  /** Ouvre le panneau ; avec une catastrophe, la montre sur la carte ; avec une communauté, ne garde que celles qui l'ont touchée. */
  async ouvrir(id = 0, communaute: { id: number; nom: string } | null = null): Promise<void> {
    this.racine.hidden = false;
    if (communaute) this.communaute = communaute;
    if (id) this.choisie = id;
    this.geologie = await connexion.invoke<Geologie>("Geologie").catch(() => undefined);
    await this.rafraichir();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.rafraichir(), 4000);
  }

  fermer(): void {
    window.clearInterval(this.minuteur);
    this.racine.hidden = true;
    this.viser = false;
    this.choisie = 0;
    this.communaute = null;
    this.vue.afficherCatastrophes(null, [], null);
  }

  /** Clic sur la carte en mode visée : le lieu de la catastrophe à provoquer. */
  viserEn(x: number, y: number): void {
    this.cible = { x, y };
    this.viser = false;
    this.dessiner(null);
    void this.rafraichir();
  }

  private async rafraichir(): Promise<void> {
    if (!this.ouvert) return;
    const [liste, maladies] = await Promise.all([
      connexion.invoke<CatastropheVue[]>("Catastrophes", this.communaute?.id ?? null, 200).catch(() => [] as CatastropheVue[]),
      connexion.invoke<Maladie[]>("Maladies").catch(() => [] as Maladie[]),
    ]);
    if (!this.ouvert) return;
    this.liste = liste;
    const detail = this.choisie ? await connexion.invoke<DetailCatastrophe | null>("Catastrophe", this.choisie).catch(() => null) : null;
    if (!this.ouvert) return;
    this.vue.afficherCatastrophes(this.geologie ?? null, liste.filter((c) => c.active), detail);
    this.dessiner(maladies);
  }

  private maladies: Maladie[] = [];

  private dessiner(maladies: Maladie[] | null): void {
    if (maladies) this.maladies = maladies;
    const defilement = this.corps.scrollTop;
    const c = this.corps;
    c.replaceChildren();

    if (this.communaute) {
      const suivi = el("p", "suivi");
      const tous = el("button", undefined, "× tout le continent");
      tous.type = "button";
      tous.addEventListener("click", () => {
        this.communaute = null;
        void this.rafraichir();
      });
      suivi.append(`Ce qui a frappé la ${minuscule(this.communaute.nom)} `, tous);
      c.append(suivi);
    }

    const actives = this.liste.filter((x) => x.active);
    const passees = this.liste.filter((x) => !x.active);
    c.append(el("h3", undefined, `En cours (${actives.length})`));
    c.append(this.lignes(actives, "Rien en ce moment."));
    c.append(el("h3", undefined, "Histoire"));
    c.append(this.lignes(passees, "Rien encore."));

    if (!this.communaute) {
      c.append(el("h3", undefined, "Maladies"));
      const ul = el("ul", "liste");
      for (const m of this.maladies) {
        const li = el("li", m.malades > 0 ? "maladie active" : "maladie");
        li.append(el("strong", undefined, m.nom), ` · apparue ${m.apparueLe} · contagion R0 ${m.r0.toFixed(2)} · létalité ${m.letalite} %`,
          ` · ${m.malades} malades, ${m.immunises} immunisés, ${m.morts} morts · immunité ${m.immunite} `);
        if (m.evenement > 0) li.append(boutonCause(li, m.evenement, "premier malade"));
        ul.append(li);
      }
      if (this.maladies.length === 0) ul.append(el("li", "discret", "Aucune maladie nouvelle n'est apparue."));
      c.append(ul);

      if (this.geologie) {
        const g = this.geologie;
        c.append(el("h3", undefined, "Le continent"));
        c.append(el("p", undefined, g.description));
        c.append(el("p", "discret",
          `Séismes de magnitude 5 ou plus : environ ${g.seismesFortsParSiecle} par siècle · magnitude maximale ${g.magnitudeMax.toFixed(1)}`
          + ` · ${g.volcans.length} volcan${g.volcans.length > 1 ? "s" : ""}`
          + (g.anomalie !== 0 ? ` · température : ${g.anomalie > 0 ? "+" : ""}${g.anomalie} °C par rapport à la normale` : "")));
        if (g.volcans.length > 0) {
          const ul2 = el("ul", "liste");
          for (const v of g.volcans) {
            ul2.append(el("li", v.enEruption ? "alerte" : undefined,
              `${v.nom} (${v.x}, ${v.y}) · une éruption tous les ${v.reposMoyenAns} ans en moyenne${v.enEruption ? " · en éruption" : ""}`));
          }
          c.append(ul2);
        }
      }
      c.append(this.formulaireEntite());
    }
    c.scrollTop = defilement;
  }

  private lignes(liste: CatastropheVue[], vide: string): HTMLElement {
    const ul = el("ul", "liste catastrophes");
    for (const x of liste) {
      const li = el("li", `catastrophe ${x.type}${x.id === this.choisie ? " choisie" : ""}${x.phenomene ? " phenomene" : ""}`);
      const titre = el("button", "lien", x.nom);
      titre.type = "button";
      titre.title = "Montrer sur la carte";
      titre.addEventListener("click", () => {
        this.choisie = this.choisie === x.id ? 0 : x.id;
        void this.rafraichir();
      });
      li.append(el("span", "date", x.debut), " ", titre);
      if (x.origine === "entite") li.append(el("span", "entite", " ✦ Entité"));
      const details = [x.intensite, x.surface ? `${x.surface} touchés` : "",
        x.morts ? `${x.morts} mort${x.morts > 1 ? "s" : ""}` : "", x.blesses ? `${x.blesses} blessé${x.blesses > 1 ? "s" : ""}` : "",
        x.temoins ? `${x.temoins} témoin${x.temoins > 1 ? "s" : ""}` : "", x.fin ? `jusqu'au ${x.fin}` : ""].filter(Boolean);
      if (details.length > 0) li.append(el("span", "discret", ` · ${details.join(" · ")}`));
      if (x.communautes.length > 0) {
        li.append(el("br"), "Touchées : ");
        x.communautes.forEach((co, i) => {
          if (i > 0) li.append(", ");
          const b = el("button", "lien", co.nom);
          b.type = "button";
          b.addEventListener("click", () => this.ouvrirCommunaute(co.id));
          li.append(b);
        });
      }
      ul.append(li);
    }
    if (liste.length === 0) ul.append(el("li", "discret", vide));
    return ul;
  }

  /** Formulaire de l'Entité : choisir une catastrophe, son intensité, puis un lieu sur la carte. */
  private formulaireEntite(): HTMLElement {
    const bloc = el("details", "entite-bloc");
    // Le panneau est redessiné toutes les 4 secondes : le bloc garde son état ouvert ou fermé.
    bloc.open = this.entiteOuverte || this.viser || this.cible !== null;
    bloc.addEventListener("toggle", () => (this.entiteOuverte = bloc.open));
    bloc.append(el("summary", "entite", "✦ L'Entité provoque…"));
    const type = el("select");
    for (const [cle, libelle] of PROVOQUABLES) type.append(Object.assign(el("option", undefined, libelle), { value: cle }));
    type.value = this.typeChoisi;
    const intensite = el("input");
    intensite.type = "number";
    intensite.step = "0.1";
    const unite = el("span", "discret");
    const majIntensite = () => {
      const p = PROVOQUABLES.find((x) => x[0] === type.value)!;
      intensite.hidden = p[3] === "";
      if (this.typeChoisi !== type.value) intensite.value = String(p[2]);
      unite.textContent = p[3];
      this.typeChoisi = type.value;
    };
    intensite.value = this.intensiteChoisie ?? String(PROVOQUABLES[0][2]);
    majIntensite();
    type.addEventListener("change", majIntensite);
    intensite.addEventListener("change", () => (this.intensiteChoisie = intensite.value));
    const lieu = el("button", undefined, this.viser ? "Clique sur la carte…" : this.cible ? `Lieu : ${this.cible.x}, ${this.cible.y}` : "Choisir le lieu sur la carte");
    lieu.type = "button";
    lieu.addEventListener("click", () => {
      this.viser = true;
      lieu.textContent = "Clique sur la carte…";
    });
    const go = el("button", "danger", "Provoquer");
    go.type = "button";
    go.disabled = !this.cible;
    go.addEventListener("click", async () => {
      if (!this.cible) return;
      const valeur = Number(intensite.value);
      const id = await connexion.invoke<number>("Declencher", { type: type.value, x: this.cible.x, y: this.cible.y, intensite: Number.isFinite(valeur) ? valeur : 0 })
        .catch((e: Error) => {
          this.message(e.message.replace(/^.*HubException: /, ""));
          return -1;
        });
      if (id === 0) this.message("Rien ne s'est produit (pas de volcan, rien à brûler, ou personne à rendre malade).");
      if (id > 0) {
        this.choisie = id;
        this.cible = null;
        await this.rafraichir();
      }
    });
    const l1 = el("div", "ligne");
    l1.append(type);
    const l2 = el("div", "ligne");
    l2.append(intensite, unite);
    const l3 = el("div", "ligne");
    l3.append(lieu, go);
    bloc.append(l1, l2, l3, el("p", "discret", "Le journal gardera la trace de l'intervention de l'Entité."));
    return bloc;
  }
  private typeChoisi = PROVOQUABLES[0][0];
  private entiteOuverte = false;
  private intensiteChoisie?: string;
}

function minuscule(nom: string): string {
  return nom.charAt(0).toLowerCase() + nom.slice(1);
}

/**
 * Bandeau d'alerte : une catastrophe qui menace des humains (rouge, le monde est peut-être en pause), ou un phénomène
 * du ciel (discret, s'efface seul). Un clic ouvre la catastrophe.
 */
export class BandeauAlertes {
  private minuteur?: number;

  constructor(private readonly racine: HTMLElement, private readonly ouvrir: (id: number) => void) {
    connexion.on("Alertes", (alertes: Alerte[]) => this.montrer(alertes));
  }

  private montrer(alertes: Alerte[]): void {
    const graves = alertes.filter((a) => a.grave);
    const a = graves.at(-1) ?? alertes.at(-1);
    if (!a) return;
    window.clearTimeout(this.minuteur);
    this.racine.className = `alerte-catastrophe${a.grave ? " grave" : ""}`;
    this.racine.replaceChildren();
    const texte = el("button", "lien", a.texte + (alertes.length > 1 ? ` (+${alertes.length - 1})` : ""));
    texte.type = "button";
    texte.title = "Voir sur la carte";
    texte.addEventListener("click", () => this.ouvrir(a.catastrophe));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => (this.racine.hidden = true));
    this.racine.append(el("span", "icone", a.grave ? "⚠" : "✦"), texte, fermer);
    this.racine.hidden = false;
    // Une alerte grave reste jusqu'à ce qu'on la ferme ; un phénomène s'efface seul.
    if (!a.grave) this.minuteur = window.setTimeout(() => (this.racine.hidden = true), 10_000);
  }
}
