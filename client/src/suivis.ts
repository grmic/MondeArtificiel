import { connexion } from "./connexion";
import { el } from "./fiche";
import { t } from "./langue";

/**
 * Un humain ou une communauté que l'on suit. Depuis la 0.40.0, les suivis vivent dans la base du monde, communs à tous les
 * navigateurs ; `heritier` : à sa mort, le suivi passe à son héritier ; `enVie` faux pour un mort ou un groupe dispersé.
 */
export interface Suivi {
  genre: "humain" | "communaute";
  id: number;
  nom: string;
  heritier?: boolean;
  enVie?: boolean;
}

interface Evenement {
  id: number;
  date: string;
  texte: string;
  significatif: boolean;
}

/** Une alerte d'un suivi, rédigée par le serveur ; `proches` : ceux que l'on peut suivre à sa mort. */
interface AlerteSuivi {
  id: number;
  secondes: number;
  date: string;
  sorte: string;
  humain: number;
  communaute: number;
  evenement: number;
  texte: string;
  lue: boolean;
  pause: boolean;
  proches: { id: number; nom: string }[];
}

interface Alertes {
  alertes: AlerteSuivi[];
  nonLues: number;
}

interface ReglesAlertes {
  sortes: string[];
  prevenir: boolean[];
  pause: boolean[];
  humain: number;
}

/** Suivis gardés par ce navigateur avant la 0.40.0, envoyés une fois au monde. */
const CLE_SUIVIS = "monde.suivis";
const ACTUALISATION_MS = 10000;
/** Nouvelles demandées pour chacun, puis gardées en tout. */
const PAR_SUIVI = 15;
const NOUVELLES_MAX = 40;
const ALERTES_PAGE = 50;
/** Cartes d'alerte empilées au plus (avec celles des catastrophes). */
const CARTES_MAX = 3;

let liste: Suivi[] = [];

function annoncer(): void {
  window.dispatchEvent(new Event("suivis"));
}

function recevoir(suivis: Suivi[] | null | undefined): void {
  liste = Array.isArray(suivis) ? suivis : [];
  annoncer();
}

connexion.on("Suivis", (s: Suivi[]) => recevoir(s));

/** Suivis que ce navigateur gardait chez lui (avant la 0.40.0). */
function lireAnciens(): Suivi[] {
  try {
    const brut = JSON.parse(localStorage.getItem(CLE_SUIVIS) ?? "[]") as unknown;
    if (!Array.isArray(brut)) return [];
    return brut.filter((s): s is Suivi =>
      typeof s === "object" && s !== null && (s.genre === "humain" || s.genre === "communaute")
      && Number.isInteger(s.id) && s.id > 0 && typeof s.nom === "string");
  } catch {
    return [];
  }
}

/**
 * À chaque connexion : les suivis gardés par ce navigateur avant la 0.40.0 rejoignent ceux du monde (une seule fois, puis
 * ils sont effacés d'ici), et la liste du monde est lue.
 */
export async function chargerSuivis(): Promise<void> {
  const anciens = lireAnciens();
  if (anciens.length > 0) {
    try {
      recevoir(await connexion.invoke<Suivi[]>("ImporterSuivis", anciens.map((s) => ({ genre: s.genre, id: s.id, nom: s.nom, heritier: false, enVie: true }))));
      try {
        localStorage.removeItem(CLE_SUIVIS);
      } catch {
        // Stockage refusé : ils seront renvoyés, sans effet, à la prochaine connexion.
      }
      return;
    } catch {
      // Le serveur n'a pas pu : on garde la liste ici pour une autre fois.
    }
  }
  recevoir(await connexion.invoke<Suivi[]>("Suivis").catch(() => [] as Suivi[]));
}

export function suivis(): readonly Suivi[] {
  return liste;
}

export function estSuivi(genre: Suivi["genre"], id: number): boolean {
  return liste.some((s) => s.genre === genre && s.id === id);
}

export async function basculerSuivi(genre: Suivi["genre"], id: number): Promise<void> {
  const retire = estSuivi(genre, id);
  recevoir(await (retire
    ? connexion.invoke<Suivi[]>("NePlusSuivre", genre, id)
    : connexion.invoke<Suivi[]>("Suivre", genre, id, false)));
}

async function choisirHeritier(id: number, heritier: boolean): Promise<void> {
  recevoir(await connexion.invoke<Suivi[]>("Suivre", "humain", id, heritier));
}

/** Nouveau monde : ses suivis (aucun au départ) sont relus. */
export function oublierSuivis(): void {
  liste = [];
  annoncer();
  void chargerSuivis();
}

/** Bouton « ☆ Suivre » / « ★ Suivi » pour la tête d'une fiche. */
export function boutonSuivre(genre: Suivi["genre"], id: number, _nom?: string): HTMLButtonElement {
  const b = el("button", "suivre");
  b.type = "button";
  const peindre = () => {
    const oui = estSuivi(genre, id);
    b.textContent = oui ? t("suivis.suivi") : t("suivis.suivre");
    b.title = oui ? t("suivis.neplusAide") : t("suivis.suivreAide");
    b.classList.toggle("actif", oui);
  };
  b.addEventListener("click", () => {
    b.disabled = true;
    void basculerSuivi(genre, id).catch(() => undefined).finally(() => {
      b.disabled = false;
      peindre();
    });
  });
  peindre();
  return b;
}

/** Boutons « ☆ Suivre X » des proches d'un suivi mort. */
function boutonsProches(a: AlerteSuivi): HTMLElement[] {
  return a.proches.filter((p) => !estSuivi("humain", p.id)).map((p) => {
    const b = el("button", "proche", t("suivis.suivreProche", { nom: p.nom }));
    b.type = "button";
    b.addEventListener("click", () => {
      b.disabled = true;
      void basculerSuivi("humain", p.id).then(() => b.remove()).catch(() => (b.disabled = false));
    });
    return b;
  });
}

/**
 * Alertes des suivis : un bandeau en bas de l'écran (avec les alertes des catastrophes), et une pastille sur le bouton
 * « Suivis » qui compte les alertes non lues ; ouvrir la fenêtre « Suivis » les marque lues.
 */
export class AlertesSuivis {
  private nonLues = 0;
  private readonly titreBouton: string;

  constructor(
    private readonly racine: HTMLElement,
    private readonly bouton: HTMLElement,
    private readonly ouvrirHumain: (id: number) => void,
    private readonly ouvrirCommunaute: (id: number) => void,
    private readonly ouverte: () => boolean,
  ) {
    this.titreBouton = bouton.title;
    connexion.on("AlertesSuivis", (alertes: AlerteSuivi[]) => this.montrer(alertes));
    connexion.on("AlertesNonLues", (n: number) => this.compter(n));
  }

  /** À la connexion : le nombre d'alertes non lues. */
  async charger(): Promise<void> {
    const r = await connexion.invoke<Alertes>("Alertes", 1, 0).catch(() => null);
    if (r) this.compter(r.nonLues);
  }

  async marquerLues(): Promise<void> {
    if (this.nonLues === 0) return;
    this.compter(0);
    await connexion.invoke("MarquerAlertesLues").catch(() => undefined);
  }

  private compter(n: number): void {
    // Fenêtre ouverte : ce qui arrive est vu aussitôt.
    if (n > 0 && this.ouverte()) {
      this.nonLues = 0;
      void connexion.invoke("MarquerAlertesLues").catch(() => undefined);
      window.dispatchEvent(new Event("alertes-suivis"));
    } else this.nonLues = n;
    this.bouton.dataset.pastille = this.nonLues > 0 ? String(Math.min(this.nonLues, 99)) : "";
    this.bouton.classList.toggle("avec-pastille", this.nonLues > 0);
    this.bouton.title = this.titreBouton + (this.nonLues > 0 ? ` · ${t("suivis.nonLues", { n: this.nonLues })}` : "");
  }

  ouvrir(a: AlerteSuivi): void {
    if (a.humain > 0) this.ouvrirHumain(a.humain);
    else if (a.communaute > 0) this.ouvrirCommunaute(a.communaute);
  }

  private montrer(alertes: AlerteSuivi[]): void {
    if (alertes.length === 0) return;
    if (this.ouverte()) window.dispatchEvent(new Event("alertes-suivis"));
    // La plus importante : une qui a mis le monde en pause, sinon la dernière.
    const a = alertes.filter((x) => x.pause).at(-1) ?? alertes.at(-1)!;
    const carte = el("div", `alerte-carte suivi${a.pause ? " grave" : ""}`);
    const texte = el("button", "lien", `${a.texte}${alertes.length > 1 ? ` (+${alertes.length - 1})` : ""}${a.pause ? ` — ${t("suivis.enPause")}` : ""}`);
    texte.type = "button";
    texte.title = t("suivis.ouvrirAlerte");
    texte.addEventListener("click", () => this.ouvrir(a));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.retirer(carte));
    const proches = alertes.flatMap(boutonsProches);
    const corps = el("div", "corps-alerte");
    corps.append(texte);
    if (proches.length > 0) {
      const ligne = el("div", "proches");
      ligne.append(...proches);
      corps.append(ligne);
    }
    carte.append(el("span", "icone", "★"), corps, fermer);
    this.racine.prepend(carte);
    while (this.racine.children.length > CARTES_MAX) this.racine.lastElementChild?.remove();
    this.racine.hidden = false;
    // Une alerte qui a mis le monde en pause, ou qui propose de suivre un proche, reste jusqu'à ce qu'on la ferme.
    if (!a.pause && proches.length === 0) window.setTimeout(() => this.retirer(carte), 12_000);
  }

  private retirer(carte: HTMLElement): void {
    carte.remove();
    if (this.racine.children.length === 0) this.racine.hidden = true;
  }
}

/**
 * Fenêtre « Suivis » : les humains et les communautés que l'on suit, les alertes, leurs dernières nouvelles mêlées de la plus
 * récente à la plus ancienne, et le réglage des alertes. Les humains suivis sont entourés sur la carte tant qu'elle est ouverte.
 */
export class PanneauSuivis {
  private readonly corps = el("div", "corps");
  private readonly qui = el("ul", "liste suivis");
  private readonly alertes = el("ul", "liste alertes-suivis");
  private readonly plus = el("button", "lien", t("suivis.plusAnciennes"));
  private readonly nouvelles = el("ul", "liste nouvelles");
  private readonly reglages = el("details", "reglages-alertes");
  private minuteur?: number;
  private derniere = 0;
  private alertesBandeau?: AlertesSuivis;

  constructor(
    private readonly racine: HTMLElement,
    private readonly ouvrirHumain: (id: number) => void,
    private readonly ouvrirCommunaute: (id: number) => void,
    private readonly entourer: (ids: number[]) => void,
  ) {
    const tete = el("div", "tete");
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(el("h2", undefined, t("suivis.titre")), fermer);
    this.plus.type = "button";
    this.plus.hidden = true;
    this.plus.addEventListener("click", () => void this.chargerAlertes(true));
    this.reglages.addEventListener("toggle", () => {
      if (this.reglages.open) void this.chargerReglages();
    });
    this.reglages.append(el("summary", undefined, t("suivis.reglages")));
    this.corps.append(this.qui, el("h3", undefined, t("suivis.alertes")), this.alertes, this.plus,
      el("h3", undefined, t("suivis.nouvelles")), this.nouvelles, this.reglages);
    racine.append(tete, this.corps);
    window.addEventListener("suivis", () => {
      if (this.ouvert) void this.charger();
    });
    window.addEventListener("alertes-suivis", () => {
      if (this.ouvert) void this.chargerAlertes(false);
    });
  }

  /** Le bandeau des alertes (pour marquer lues celles que l'on voit ici). */
  lier(alertes: AlertesSuivis): void {
    this.alertesBandeau = alertes;
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else this.ouvrir();
  }

  ouvrir(): void {
    this.racine.hidden = false;
    void this.charger();
    void this.chargerAlertes(false);
    void this.alertesBandeau?.marquerLues();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.charger(), ACTUALISATION_MS);
  }

  fermer(): void {
    if (!this.ouvert) return;
    this.racine.hidden = true;
    window.clearInterval(this.minuteur);
    this.entourer([]);
  }

  private async chargerAlertes(suite: boolean): Promise<void> {
    const r = await connexion.invoke<Alertes>("Alertes", ALERTES_PAGE, suite ? this.derniere : 0).catch(() => null);
    if (!r || !this.ouvert) return;
    const lignes = r.alertes.map((a) => {
      const li = el("li", a.lue ? undefined : "non-lue");
      const texte = el("button", "lien", `${a.date} · ${a.texte}`);
      texte.type = "button";
      texte.title = t("suivis.ouvrirAlerte");
      texte.addEventListener("click", () => (a.humain > 0 ? this.ouvrirHumain(a.humain) : this.ouvrirCommunaute(a.communaute)));
      li.append("★ ", texte, ...boutonsProches(a));
      return li;
    });
    if (suite) this.alertes.append(...lignes);
    else this.alertes.replaceChildren(...(lignes.length > 0 ? lignes : [el("li", "discret", t("suivis.alertesRien"))]));
    if (r.alertes.length > 0) this.derniere = r.alertes[r.alertes.length - 1].id;
    this.plus.hidden = r.alertes.length < ALERTES_PAGE;
  }

  private async chargerReglages(): Promise<void> {
    const r = await connexion.invoke<ReglesAlertes>("ReglesAlertes").catch(() => null);
    if (!r) return;
    const table = el("table", "regles");
    const entete = el("tr");
    entete.append(el("th", undefined, t("suivis.sorte")), el("th", undefined, t("suivis.prevenir")), el("th", undefined, t("suivis.pause")));
    table.append(entete);
    const cases: { prevenir: HTMLInputElement; pause: HTMLInputElement }[] = [];
    const choisir = async () => {
      const prevenir = cases.map((c) => c.prevenir.checked);
      const pause = cases.map((c) => c.pause.checked);
      for (const c of cases) c.pause.disabled = !c.prevenir.checked;
      await connexion.invoke("ChoisirReglesAlertes", prevenir, pause).catch(() => undefined);
    };
    r.sortes.forEach((sorte, i) => {
      if (i === 0 || i === r.humain) {
        const titre = el("tr", "groupe");
        titre.append(el("th", undefined, t(i === 0 ? "suivis.pourHumain" : "suivis.pourCommunaute")));
        table.append(titre);
      }
      const tr = el("tr");
      const prevenir = el("input");
      prevenir.type = "checkbox";
      prevenir.checked = r.prevenir[i];
      const pause = el("input");
      pause.type = "checkbox";
      pause.checked = r.pause[i];
      pause.disabled = !prevenir.checked;
      for (const c of [prevenir, pause]) c.addEventListener("change", () => void choisir());
      cases.push({ prevenir, pause });
      const c1 = el("td");
      c1.append(prevenir);
      const c2 = el("td");
      c2.append(pause);
      tr.append(el("td", undefined, t(`alerte.sorte.${sorte}`)), c1, c2);
      table.append(tr);
    });
    this.reglages.replaceChildren(el("summary", undefined, t("suivis.reglages")), el("p", "discret", t("suivis.reglagesAide")), table);
  }

  private async charger(): Promise<void> {
    const tous = [...liste];
    this.qui.replaceChildren(...tous.map((s) => {
      const li = el("li", s.enVie === false ? "mort" : undefined);
      const nom = el("button", "lien", s.nom + (s.enVie === false ? ` (${t(s.genre === "humain" ? "suivis.mort" : "suivis.disparue")})` : ""));
      nom.type = "button";
      nom.title = s.genre === "humain" ? t("commun.ouvrirSaFiche") : t("suivis.ouvrirCommunaute");
      nom.addEventListener("click", () => (s.genre === "humain" ? this.ouvrirHumain(s.id) : this.ouvrirCommunaute(s.id)));
      const retirer = el("button", "fermer", "×");
      retirer.type = "button";
      retirer.title = t("suivis.neplus");
      retirer.addEventListener("click", () => void basculerSuivi(s.genre, s.id).catch(() => undefined));
      li.append(s.genre === "humain" ? "👤 " : "👥 ", nom, " ");
      if (s.genre === "humain" && s.enVie !== false) {
        const etiquette = el("label", "heritier");
        etiquette.title = t("suivis.heritierAide");
        const c = el("input");
        c.type = "checkbox";
        c.checked = s.heritier === true;
        c.addEventListener("change", () => void choisirHeritier(s.id, c.checked).catch(() => (c.checked = !c.checked)));
        etiquette.append(c, " ", t("suivis.heritier"));
        li.append(etiquette, " ");
      }
      li.append(retirer);
      return li;
    }));
    if (tous.length === 0) {
      this.qui.replaceChildren(el("li", "discret", t("suivis.personne")));
      this.nouvelles.replaceChildren();
      this.entourer([]);
      return;
    }
    this.entourer(tous.filter((s) => s.genre === "humain" && s.enVie !== false).map((s) => s.id));
    const listes = await Promise.all(tous.map((s) => connexion
      .invoke<Evenement[]>("Journal", s.genre === "humain" ? { humain: s.id, limite: PAR_SUIVI } : { communaute: s.id, limite: PAR_SUIVI })
      .catch(() => [] as Evenement[])));
    if (!this.ouvert) return;
    const vus = new Set<number>();
    const mele = listes.flat().filter((e) => !vus.has(e.id) && vus.add(e.id)).sort((a, b) => b.id - a.id).slice(0, NOUVELLES_MAX);
    this.nouvelles.replaceChildren(...(mele.length === 0
      ? [el("li", "discret", t("suivis.rien"))]
      : mele.map((e) => el("li", e.significatif ? "significatif" : undefined, `${e.significatif ? "★ " : ""}${e.date} · ${e.texte}`))));
  }
}
