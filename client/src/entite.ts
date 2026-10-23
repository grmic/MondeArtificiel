import { connexion } from "./connexion";
import { nombre, t } from "./langue";

interface Choix {
  cle: string;
  libelle: string;
}

interface Parole {
  cle: string;
  libelle: string;
  rubrique: string;
  cible: boolean;
  /** « direction », « plante », « communaute » ou null. */
  precision: string | null;
  /** La phrase, avec {cible} et {extra} à remplacer. */
  modele: string;
  nuit: boolean;
}

interface ChoixParoles {
  paroles: Parole[];
  modes: Choix[];
  directions: Choix[];
  plantes: Choix[];
  communautes: Choix[];
}

interface Trouve {
  id: number;
  nom: string;
  sexe: string;
  age: number;
  communaute: string | null;
  seul: boolean;
  distanceKm: number;
  karma: number;
}

/** Filtres de karma proposés : bornes incluses (null : sans limite). */
const KARMAS: { cle: string; libelle: string; min: number | null; max: number | null }[] = [
  { cle: "", libelle: "entite.karma.tout", min: null, max: null },
  { cle: "lumineux", libelle: "entite.karma.lumineux", min: 50, max: null },
  { cle: "bon", libelle: "entite.karma.bon", min: 5, max: null },
  { cle: "neutre", libelle: "entite.karma.neutre", min: -5, max: 5 },
  { cle: "charge", libelle: "entite.karma.charge", min: null, max: -5 },
  { cle: "lourd", libelle: "entite.karma.lourd", min: null, max: -20 },
];

/** « +2 », « −30 » : un karma signé, à la française. */
function signe(v: number): string {
  const n = nombre(Math.abs(v), 1);
  return v > 0 ? `+${n}` : v < 0 ? `−${n}` : "0";
}

interface Resultat {
  humain: number;
  nom: string;
  resultat: string;
}

/** À qui l'Entité s'adresse : des humains et/ou tous les membres de communautés ; près de qui chercher, et dans quel groupe. */
export interface Destinataires {
  humains: number[];
  communautes: number[];
  pres: number;
  communaute: number;
  libelle: string;
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, classe?: string, texte?: string): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texte !== undefined) e.textContent = texte;
  return e;
}

function remplir(select: HTMLSelectElement, choix: Choix[]): void {
  const avant = select.value;
  select.replaceChildren(...choix.map((c) => {
    const o = el("option", undefined, c.libelle);
    o.value = c.cle;
    return o;
  }));
  if (choix.some((c) => c.cle === avant)) select.value = avant;
}

function ligne(libelle: string, ...champs: (HTMLElement | string)[]): HTMLLabelElement {
  const l = el("label", "ligne");
  l.append(el("span", undefined, libelle), ...champs);
  return l;
}

/**
 * Désigner quelqu'un : une recherche par le nom, filtrée (même communauté, seuls, adultes, tout près), puis une liste.
 * La recherche se fait au serveur (40 résultats au plus).
 */
class Designer {
  readonly bloc = el("div");
  private readonly texte = el("input");
  private readonly liste = el("select");
  private readonly memeGroupe = el("input");
  private readonly seuls = el("input");
  private readonly pres = el("input");
  private readonly karma = el("select");
  private minuteur?: number;

  constructor(private readonly dest: () => Destinataires, libelle: string, private readonly avecMonde = false) {
    this.texte.type = "search";
    this.texte.placeholder = t("entite.nom");
    this.texte.maxLength = 40;
    for (const c of [this.memeGroupe, this.seuls, this.pres]) c.type = "checkbox";
    this.pres.checked = true;
    const relancer = () => {
      window.clearTimeout(this.minuteur);
      this.minuteur = window.setTimeout(() => void this.chercher(), 250);
    };
    this.texte.addEventListener("input", relancer);
    for (const c of [this.memeGroupe, this.seuls, this.pres, this.karma]) c.addEventListener("change", relancer);
    remplir(this.karma, KARMAS.map((k) => ({ cle: k.cle, libelle: t(k.libelle) })));
    this.karma.title = t("entite.karmaAide");
    const filtres = el("div", "ligne discret");
    filtres.append(el("label", undefined), el("label", undefined), el("label", undefined));
    const [a, b, c] = Array.from(filtres.children) as HTMLLabelElement[];
    a.append(this.memeGroupe, " " + t("entite.memeCommunaute"));
    b.append(this.seuls, " " + t("entite.seuls"));
    c.append(this.pres, " " + t("entite.proches"));
    filtres.append(this.karma);
    this.bloc.append(ligne(libelle, this.texte, this.liste), filtres);
  }

  get valeur(): number {
    return Number(this.liste.value) || 0;
  }

  get nom(): string {
    return this.liste.selectedOptions[0]?.dataset.nom ?? "…";
  }

  async chercher(): Promise<void> {
    const d = this.dest();
    try {
      const trouves = await connexion.invoke<Trouve[]>("ChercherHumains", {
        texte: this.texte.value,
        communaute: this.memeGroupe.checked ? d.communaute : 0,
        seuls: this.seuls.checked,
        adultes: false,
        sexe: null,
        pres: this.pres.checked ? d.pres : 0,
        karmaMin: KARMAS.find((k) => k.cle === this.karma.value)?.min ?? null,
        karmaMax: KARMAS.find((k) => k.cle === this.karma.value)?.max ?? null,
      });
      const avant = this.liste.value;
      const options: HTMLOptionElement[] = [];
      if (this.avecMonde) {
        const o = el("option", undefined, t("entite.mondeChoisit"));
        o.value = "0";
        o.dataset.nom = t("entite.quelquun");
        options.push(o);
      }
      for (const h of trouves.filter((x) => !d.humains.includes(x.id))) {
        const g = h.sexe === "femme" ? "f" : "m";
        const details = [t("entite.sexeAge", { g, age: t("age.ans", { n: h.age }) }), h.communaute, h.seul ? t("entite.seul", { g }) : null,
          d.pres ? t("entite.km", { v: h.distanceKm }) : null, t("entite.karma", { v: signe(h.karma) })].filter((x) => x).join(t("commun.virgule"));
        const o = el("option", undefined, `${h.nom} (${details})`);
        o.value = String(h.id);
        o.dataset.nom = h.nom;
        options.push(o);
      }
      this.liste.replaceChildren(...options);
      if (options.some((o) => o.value === avant)) this.liste.value = avant;
      this.liste.dispatchEvent(new Event("change"));
    } catch {
      this.liste.replaceChildren();
    }
  }

  surChange(f: () => void): void {
    this.liste.addEventListener("change", f);
  }
}

/**
 * Formulaire de l'Entité : parler (une phrase composée par menus, reçue en rêve, par une voix, un signe ou une apparition)
 * ou inspirer un amour. Chacun en fait ce que son caractère lui dicte ; le journal le marque comme venu de l'Entité.
 */
export class FormulaireEntite {
  readonly bloc = el("details", "entite-bloc");
  private readonly resultat = el("ul", "liste discret");
  private choix: ChoixParoles | null = null;

  constructor(private readonly dest: () => Destinataires) {
    this.bloc.append(el("summary", "entite", t("entite.parleOuInspire")));
    const action = el("select");
    remplir(action, [{ cle: "parler", libelle: t("entite.parler") }, { cle: "inspirer", libelle: t("entite.inspirerAmour") }]);
    const parler = this.parler();
    const inspirer = this.inspirer();
    inspirer.hidden = true;
    action.addEventListener("change", () => {
      parler.hidden = action.value !== "parler";
      inspirer.hidden = action.value !== "inspirer";
    });
    this.bloc.append(ligne(t("entite.queFaire"), action), parler, inspirer, this.resultat,
      el("p", "discret", t("entite.rienGaranti")));
  }

  /** Un autre destinataire : on referme le formulaire et l'on efface les réponses d'avant. */
  oublier(): void {
    this.bloc.open = false;
    this.resultat.replaceChildren();
  }

  /** Vrai si l'on est en train de remplir le formulaire (il ne faut pas le redessiner). */
  get enCours(): boolean {
    return this.bloc.contains(document.activeElement) || this.bloc.matches(":hover");
  }

  private async charger(): Promise<ChoixParoles | null> {
    try {
      this.choix = await connexion.invoke<ChoixParoles>("ChoixParoles");
    } catch {
      this.ecrire([t("commun.pasDeReponse")]);
    }
    return this.choix;
  }

  private ecrire(lignes: string[]): void {
    this.resultat.replaceChildren(...lignes.map((l) => el("li", undefined, l)));
  }

  private montrer(resultats: Resultat[]): void {
    this.ecrire(resultats.length === 0 ? [t("entite.personneRecu")] : resultats.map((r) => t("priere.resultat", { nom: r.nom, texte: r.resultat })));
  }

  private parler(): HTMLElement {
    const bloc = el("div");
    const parole = el("select");
    const mode = el("select");
    const precision = el("select");
    const lignePrecision = ligne("", precision);
    const qui = new Designer(this.dest, t("entite.qui"));
    const apercu = el("p", "entite");
    const bouton = el("button", undefined, t("entite.parler"));
    let phraseDite = "";
    bouton.type = "button";
    const actuelle = () => this.choix?.paroles.find((p) => p.cle === parole.value);
    const accorder = () => {
      const p = actuelle();
      if (!p || !this.choix) return;
      qui.bloc.hidden = !p.cible;
      lignePrecision.hidden = !p.precision;
      const titre = lignePrecision.firstElementChild as HTMLElement;
      if (p.precision === "direction") {
        titre.textContent = t("entite.vers");
        remplir(precision, this.choix.directions);
      } else if (p.precision === "plante") {
        titre.textContent = t("entite.plante");
        remplir(precision, this.choix.plantes);
      } else if (p.precision === "communaute") {
        titre.textContent = t("entite.contre");
        remplir(precision, [{ cle: "0", libelle: t("entite.quiconque") }, ...this.choix.communautes]);
      }
      const extra = precision.selectedOptions[0]?.textContent ?? "";
      // « Ne va pas à la guerre » contre quiconque : la phrase sans adversaire.
      phraseDite = p.precision === "communaute" && precision.value === "0" ? t(`phrase.${p.cle}`)
        : p.modele.replace("{cible}", qui.nom).replace("{extra}", extra);
      apercu.textContent = t("entite.apercu", { qui: this.dest().libelle, mode: mode.selectedOptions[0]?.textContent ?? "", phrase: phraseDite });
      bouton.classList.toggle("danger", p.nuit);
    };
    parole.addEventListener("change", () => {
      accorder();
      if (actuelle()?.cible) void qui.chercher();
    });
    for (const s of [mode, precision]) s.addEventListener("change", accorder);
    qui.surChange(accorder);
    this.bloc.addEventListener("toggle", async () => {
      if (!this.bloc.open) return;
      const c = this.choix ?? await this.charger();
      if (!c) return;
      // Les paroles groupées par rubrique.
      const avant = parole.value;
      parole.replaceChildren();
      for (const rubrique of [...new Set(c.paroles.map((p) => p.rubrique))]) {
        const groupe = el("optgroup");
        groupe.label = rubrique;
        for (const p of c.paroles.filter((x) => x.rubrique === rubrique)) {
          const o = el("option", undefined, p.libelle);
          o.value = p.cle;
          groupe.append(o);
        }
        parole.append(groupe);
      }
      if (avant) parole.value = avant;
      remplir(mode, c.modes);
      if (!mode.value) mode.value = "Voix";
      accorder();
      if (actuelle()?.cible) void qui.chercher();
    });
    bouton.addEventListener("click", async () => {
      const p = actuelle();
      if (!p) return;
      if (p.cible && !qui.valeur) {
        this.ecrire([t("entite.choisirPersonne")]);
        return;
      }
      if (p.nuit && !window.confirm(t("entite.confirmer", { phrase: phraseDite || p.libelle }))) return;
      const d = this.dest();
      bouton.disabled = true;
      try {
        this.montrer(await connexion.invoke<Resultat[]>("Parler", {
          humains: d.humains,
          communautes: d.communautes,
          parole: p.cle,
          mode: mode.value,
          cible: p.cible ? qui.valeur : 0,
          param: p.precision ? Number(precision.value) : 0,
        }));
      } catch (e) {
        this.ecrire([e instanceof Error && e.message.includes("HubException") ? e.message.split("HubException: ").pop() ?? t("commun.refuse") : t("entite.paroleEchec")]);
      } finally {
        bouton.disabled = false;
      }
    });
    bloc.append(ligne(t("entite.parole"), parole), ligne(t("agir.comment"), mode), qui.bloc, lignePrecision, apercu, bouton);
    return bloc;
  }

  private inspirer(): HTMLElement {
    const bloc = el("div");
    const pour = new Designer(this.dest, t("entite.pour"), true);
    const force = el("select");
    remplir(force, [{ cle: "leger", libelle: t("entite.leger") }, { cle: "passion", libelle: t("entite.passion") }]);
    const mutuel = el("input");
    const orientation = el("input");
    mutuel.type = orientation.type = "checkbox";
    const options = el("div", "ligne");
    const l1 = el("label");
    l1.append(mutuel, " " + t("entite.reciproque"));
    const l2 = el("label");
    l2.append(orientation, " " + t("entite.orientation"));
    options.append(l1, l2);
    const bouton = el("button", undefined, t("entite.inspirer"));
    bouton.type = "button";
    this.bloc.addEventListener("toggle", () => {
      if (this.bloc.open) void pour.chercher();
    });
    bouton.addEventListener("click", async () => {
      const d = this.dest();
      bouton.disabled = true;
      try {
        this.montrer(await connexion.invoke<Resultat[]>("Inspirer", {
          humains: d.humains,
          communautes: d.communautes,
          cible: pour.valeur,
          mutuel: mutuel.checked,
          passion: force.value === "passion",
          orientation: orientation.checked,
        }));
      } catch {
        this.ecrire([t("entite.inspirerEchec")]);
      } finally {
        bouton.disabled = false;
      }
    });
    bloc.append(pour.bloc, ligne(t("agir.force"), force), options, bouton,
      el("p", "discret", t("entite.penchantAide")));
    return bloc;
  }
}
