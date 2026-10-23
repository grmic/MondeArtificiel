import { connexion } from "./connexion";
import type { VueCarte } from "./carte";
import { boutonCause, el } from "./fiche";
import { nombre, t } from "./langue";

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
  /** Morts de faim, soif, froid, épuisement ou maladie dans les terres dévastées, après la catastrophe. */
  mortsSuites: number;
  /** Date où les terres dévastées reverdiront (ou ont reverdi). */
  suitesJusqua: string | null;
  enSuites: boolean;
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
export const PROVOQUABLES: [string, string, number, string][] = [
  ["seisme", 6.5, true],
  ["eruption", 3, true],
  ["tsunami", 8, true],
  ["crue", 2, true],
  ["incendie", 0, false],
  ["cyclone", 3, true],
  ["froid", 8, true],
  ["canicule", 7, true],
  ["epidemie", 0, false],
  ["eclipse", 0, false],
  ["comete", 0, false],
  ["aurore", 0, false],
  ["bolide", 0, false],
  ["impact", 10, true],
].map(([cle, valeur, mesure]) => [cle as string, t(`provoquer.${cle}`), valeur as number, mesure ? t(`provoquer.${cle}.unite`) : ""]);

/**
 * Catastrophes et phénomènes du monde. En tête, ce que l'Entité peut provoquer ; puis ce qui est en cours, l'histoire
 * (un clic montre les terres touchées sur la carte), les maladies apparues et la géologie du continent (failles, volcans).
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
  /** Ce qui est redessiné toutes les 4 secondes ; le formulaire de l'Entité, lui, est construit une fois. */
  private readonly contenu = el("div");
  private readonly entite: HTMLDetailsElement;
  private readonly boutonLieu: HTMLButtonElement;
  private readonly boutonProvoquer: HTMLButtonElement;

  constructor(
    private readonly racine: HTMLElement,
    private readonly vue: VueCarte,
    private readonly ouvrirCommunaute: (id: number) => void,
    private readonly message: (texte: string) => void,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, t("barre.catastrophes")));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    racine.append(tete, this.corps);
    [this.entite, this.boutonLieu, this.boutonProvoquer] = this.formulaireEntite();
    this.corps.append(this.entite, this.contenu);
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
    const c = this.contenu;
    c.replaceChildren();

    if (this.communaute) {
      const suivi = el("p", "suivi");
      const tous = el("button", undefined, t("catastrophes.toutLeContinent"));
      tous.type = "button";
      tous.addEventListener("click", () => {
        this.communaute = null;
        void this.rafraichir();
      });
      suivi.append(`${t("catastrophes.cequiAFrappe", { nom: this.communaute.nom, nomMin: minuscule(this.communaute.nom) })} `, tous);
      c.append(suivi);
    }

    const actives = this.liste.filter((x) => x.active);
    const suites = this.liste.filter((x) => !x.active && x.enSuites);
    const passees = this.liste.filter((x) => !x.active && !x.enSuites);
    c.append(el("h3", undefined, t("catastrophes.enCours", { n: actives.length })));
    c.append(this.lignes(actives, t("catastrophes.rienEnCeMoment")));
    if (suites.length > 0) {
      c.append(el("h3", undefined, t("catastrophes.terresDevastees", { n: suites.length })));
      c.append(this.lignes(suites, ""));
    }
    c.append(el("h3", undefined, t("catastrophes.histoire")));
    c.append(this.lignes(passees, t("catastrophes.rienEncore")));

    if (!this.communaute) {
      c.append(el("h3", undefined, t("catastrophes.maladies")));
      const ul = el("ul", "liste");
      for (const m of this.maladies) {
        const li = el("li", m.malades > 0 ? "maladie active" : "maladie");
        li.append(el("strong", undefined, m.nom), ` · ${t("catastrophes.maladie", { apparue: m.apparueLe, r0: nombre(m.r0, 2), letalite: m.letalite, malades: m.malades, immunises: m.immunises, morts: m.morts, immunite: m.immunite })} `);
        if (m.evenement > 0) li.append(boutonCause(li, m.evenement, t("catastrophes.premierMalade")));
        ul.append(li);
      }
      if (this.maladies.length === 0) ul.append(el("li", "discret", t("catastrophes.aucuneMaladie")));
      c.append(ul);

      if (this.geologie) {
        const g = this.geologie;
        c.append(el("h3", undefined, t("catastrophes.leContinent")));
        c.append(el("p", undefined, g.description));
        c.append(el("p", "discret",
          t("catastrophes.seismes", { parSiecle: g.seismesFortsParSiecle, magnitude: nombre(g.magnitudeMax, 1) })
          + ` · ${t("catastrophes.volcans", { n: g.volcans.length })}`
          + (g.anomalie !== 0 ? ` · ${t("catastrophes.anomalie", { ecart: `${g.anomalie > 0 ? "+" : ""}${nombre(g.anomalie, 1)}` })}` : "")));
        if (g.volcans.length > 0) {
          const ul2 = el("ul", "liste");
          for (const v of g.volcans) {
            ul2.append(el("li", v.enEruption ? "alerte" : undefined,
              `${v.nom} (${v.x}, ${v.y}) · ${t("catastrophes.volcan", { n: v.reposMoyenAns })}${v.enEruption ? ` · ${t("catastrophes.enEruption")}` : ""}`));
          }
          c.append(ul2);
        }
      }
    }
    // Le formulaire n'est jamais reconstruit : un menu ouvert ou une valeur en cours de saisie restent en place.
    this.entite.hidden = this.communaute !== null;
    if (this.viser || this.cible) this.entite.open = true;
    this.boutonLieu.textContent = this.viser ? t("catastrophes.cliqueCarte") : this.cible ? t("catastrophes.lieu", { x: String(this.cible.x), y: String(this.cible.y) }) : t("catastrophes.choisirLieu");
    this.boutonProvoquer.disabled = !this.cible;
    this.corps.scrollTop = defilement;
  }

  private lignes(liste: CatastropheVue[], vide: string): HTMLElement {
    const ul = el("ul", "liste catastrophes");
    for (const x of liste) {
      const li = el("li", `catastrophe ${x.type}${x.id === this.choisie ? " choisie" : ""}${x.phenomene ? " phenomene" : ""}`);
      const titre = el("button", "lien", x.nom);
      titre.type = "button";
      titre.title = t("commun.montrerSurCarte");
      titre.addEventListener("click", () => {
        this.choisie = this.choisie === x.id ? 0 : x.id;
        void this.rafraichir();
      });
      li.append(el("span", "date", x.debut), " ", titre);
      if (x.origine === "entite") li.append(el("span", "entite", ` ✦ ${t("commun.entite")}`));
      const details = [x.intensite, x.surface ? t("catastrophes.touches", { surface: x.surface }) : "",
        x.morts ? t("catastrophes.morts", { n: x.morts }) : "", x.blesses ? t("catastrophes.blesses", { n: x.blesses }) : "",
        x.mortsSuites ? t("catastrophes.mortsSuites", { n: x.mortsSuites }) : "",
        x.temoins ? t("catastrophes.temoins", { n: x.temoins }) : "", x.fin ? t("catastrophes.jusquau", { date: x.fin }) : "",
        x.suitesJusqua ? (x.enSuites ? t("catastrophes.devasteesJusqua", { date: x.suitesJusqua }) : t("catastrophes.remises", { date: x.suitesJusqua })) : ""].filter(Boolean);
      if (details.length > 0) li.append(el("span", "discret", ` · ${details.join(" · ")}`));
      if (x.communautes.length > 0) {
        li.append(el("br"), t("catastrophes.touchees"));
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
  private formulaireEntite(): [HTMLDetailsElement, HTMLButtonElement, HTMLButtonElement] {
    const bloc = el("details", "entite-bloc");
    bloc.append(el("summary", "entite", t("catastrophes.entiteProvoque")));
    const type = el("select");
    for (const [cle, libelle] of PROVOQUABLES) type.append(Object.assign(el("option", undefined, libelle), { value: cle }));
    const intensite = el("input");
    intensite.type = "number";
    intensite.step = "0.1";
    const unite = el("span", "discret");
    const majIntensite = () => {
      const p = PROVOQUABLES.find((x) => x[0] === type.value)!;
      intensite.hidden = p[3] === "";
      intensite.value = String(p[2]);
      unite.textContent = p[3];
    };
    majIntensite();
    type.addEventListener("change", majIntensite);
    const lieu = el("button", undefined, t("catastrophes.choisirLieu"));
    lieu.type = "button";
    lieu.addEventListener("click", () => {
      this.viser = true;
      lieu.textContent = t("catastrophes.cliqueCarte");
    });
    const go = el("button", "danger", t("catastrophes.provoquer"));
    go.type = "button";
    go.disabled = true;
    go.addEventListener("click", async () => {
      if (!this.cible) return;
      const valeur = Number(intensite.value);
      const id = await connexion.invoke<number>("Declencher", { type: type.value, x: this.cible.x, y: this.cible.y, intensite: Number.isFinite(valeur) ? valeur : 0 })
        .catch((e: Error) => {
          this.message(e.message.replace(/^.*HubException: /, ""));
          return -1;
        });
      if (id === 0) this.message(t("catastrophes.rienProduit"));
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
    bloc.append(l1, l2, l3, el("p", "discret", t("catastrophes.traceJournal")));
    return [bloc, lieu, go];
  }
}

function minuscule(nom: string): string {
  return nom.charAt(0).toLowerCase() + nom.slice(1);
}

/** Clé du réglage « Afficher les alertes de catastrophe » (propre à ce navigateur). */
const CLE_ALERTES = "monde.alertesCatastrophes";

/** Vrai si les alertes de catastrophe s'affichent (par défaut). */
export function alertesAffichees(): boolean {
  try {
    return localStorage.getItem(CLE_ALERTES) !== "non";
  } catch {
    return true;
  }
}

export function afficherAlertes(oui: boolean): void {
  try {
    localStorage.setItem(CLE_ALERTES, oui ? "oui" : "non");
  } catch {
    // Stockage refusé : le réglage vaut pour cette page seulement.
  }
  window.dispatchEvent(new CustomEvent("alertes-catastrophes", { detail: oui }));
}

/** Alertes empilées au plus. */
const ALERTES_MAX = 3;

/**
 * Alertes : une catastrophe qui menace des humains (rouge, le monde est peut-être en pause), ou un phénomène du ciel
 * (discret, s'efface seul). Elles s'empilent en bas de l'écran, au-dessus de la barre d'échelle, loin des menus ; un clic
 * ouvre la catastrophe. Le bouton « Catastrophes » porte une pastille : les alertes graves arrivées depuis qu'on a ouvert la
 * fenêtre pour la dernière fois. Le réglage de la Configuration cache les alertes, pas la pastille.
 */
export class BandeauAlertes {
  private nonVues = 0;
  private readonly titreBouton: string;

  constructor(private readonly racine: HTMLElement, private readonly bouton: HTMLElement, private readonly ouvrir: (id: number) => void) {
    this.titreBouton = bouton.title;
    connexion.on("Alertes", (alertes: Alerte[]) => this.montrer(alertes));
    window.addEventListener("alertes-catastrophes", (e) => {
      if (!(e as CustomEvent<boolean>).detail) this.vider();
    });
  }

  /** La fenêtre Catastrophes vient d'être ouverte : tout est vu. */
  vues(): void {
    this.nonVues = 0;
    this.pastille();
  }

  vider(): void {
    this.racine.replaceChildren();
    this.racine.hidden = true;
  }

  private montrer(alertes: Alerte[]): void {
    const graves = alertes.filter((a) => a.grave);
    const a = graves.at(-1) ?? alertes.at(-1);
    if (!a) return;
    if (graves.length > 0) {
      this.nonVues += graves.length;
      this.pastille();
    }
    if (!alertesAffichees()) return;
    const carte = el("div", `alerte-carte${a.grave ? " grave" : ""}`);
    const texte = el("button", "lien", a.texte + (alertes.length > 1 ? ` (+${alertes.length - 1})` : ""));
    texte.type = "button";
    texte.title = t("commun.voirSurCarte");
    texte.addEventListener("click", () => this.ouvrir(a.catastrophe));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.retirer(carte));
    carte.append(el("span", "icone", a.grave ? "⚠" : "✦"), texte, fermer);
    // La plus récente en haut ; au-delà de trois, la plus ancienne s'en va.
    this.racine.prepend(carte);
    while (this.racine.children.length > ALERTES_MAX) this.racine.lastElementChild?.remove();
    this.racine.hidden = false;
    // Une alerte grave reste jusqu'à ce qu'on la ferme ; un phénomène s'efface seul.
    if (!a.grave) window.setTimeout(() => this.retirer(carte), 10_000);
  }

  private retirer(carte: HTMLElement): void {
    carte.remove();
    if (this.racine.children.length === 0) this.racine.hidden = true;
  }

  private pastille(): void {
    this.bouton.dataset.pastille = this.nonVues > 0 ? String(Math.min(this.nonVues, 99)) : "";
    this.bouton.classList.toggle("avec-pastille", this.nonVues > 0);
    this.bouton.title = this.titreBouton + (this.nonVues > 0 ? ` · ${t("catastrophes.nonVues", { n: this.nonVues })}` : "");
  }
}
