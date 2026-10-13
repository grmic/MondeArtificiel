import { connexion } from "./connexion";

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
  private minuteur?: number;

  constructor(private readonly dest: () => Destinataires, libelle: string, private readonly avecMonde = false) {
    this.texte.type = "search";
    this.texte.placeholder = "nom…";
    this.texte.maxLength = 40;
    for (const c of [this.memeGroupe, this.seuls, this.pres]) c.type = "checkbox";
    this.pres.checked = true;
    const relancer = () => {
      window.clearTimeout(this.minuteur);
      this.minuteur = window.setTimeout(() => void this.chercher(), 250);
    };
    this.texte.addEventListener("input", relancer);
    for (const c of [this.memeGroupe, this.seuls, this.pres]) c.addEventListener("change", relancer);
    const filtres = el("div", "ligne discret");
    filtres.append(el("label", undefined), el("label", undefined), el("label", undefined));
    const [a, b, c] = Array.from(filtres.children) as HTMLLabelElement[];
    a.append(this.memeGroupe, " même communauté");
    b.append(this.seuls, " seuls");
    c.append(this.pres, " les plus proches d'abord");
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
      });
      const avant = this.liste.value;
      const options: HTMLOptionElement[] = [];
      if (this.avecMonde) {
        const o = el("option", undefined, "laisser le monde choisir");
        o.value = "0";
        o.dataset.nom = "quelqu'un";
        options.push(o);
      }
      for (const t of trouves.filter((t) => !d.humains.includes(t.id))) {
        const o = el("option", undefined,
          `${t.nom} (${t.sexe === "femme" ? "F" : "H"}, ${t.age} ans${t.communaute ? `, ${t.communaute}` : ""}${t.seul ? ", seul" + (t.sexe === "femme" ? "e" : "") : ""}${d.pres ? `, ${t.distanceKm} km` : ""})`);
        o.value = String(t.id);
        o.dataset.nom = t.nom;
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
    this.bloc.append(el("summary", "entite", "✦ L'Entité parle ou inspire un amour…"));
    const action = el("select");
    remplir(action, [{ cle: "parler", libelle: "Parler" }, { cle: "inspirer", libelle: "Inspirer un amour" }]);
    const parler = this.parler();
    const inspirer = this.inspirer();
    inspirer.hidden = true;
    action.addEventListener("change", () => {
      parler.hidden = action.value !== "parler";
      inspirer.hidden = action.value !== "inspirer";
    });
    this.bloc.append(ligne("Que faire", action), parler, inspirer, this.resultat,
      el("p", "discret", "Rien n'est garanti : chacun comprend, croit, obéit, hésite ou refuse selon ce qu'il est. Le journal marque tout cela d'un ✦."));
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
      this.ecrire(["Le serveur n'a pas répondu."]);
    }
    return this.choix;
  }

  private ecrire(lignes: string[]): void {
    this.resultat.replaceChildren(...lignes.map((l) => el("li", undefined, l)));
  }

  private montrer(resultats: Resultat[]): void {
    this.ecrire(resultats.length === 0 ? ["Personne n'a rien reçu."] : resultats.map((r) => `${r.nom} : ${r.resultat}`));
  }

  private parler(): HTMLElement {
    const bloc = el("div");
    const parole = el("select");
    const mode = el("select");
    const precision = el("select");
    const lignePrecision = ligne("", precision);
    const qui = new Designer(this.dest, "Qui");
    const apercu = el("p", "entite");
    const bouton = el("button", undefined, "Parler");
    bouton.type = "button";
    const actuelle = () => this.choix?.paroles.find((p) => p.cle === parole.value);
    const accorder = () => {
      const p = actuelle();
      if (!p || !this.choix) return;
      qui.bloc.hidden = !p.cible;
      lignePrecision.hidden = !p.precision;
      const titre = lignePrecision.firstElementChild as HTMLElement;
      if (p.precision === "direction") {
        titre.textContent = "Vers";
        remplir(precision, this.choix.directions);
      } else if (p.precision === "plante") {
        titre.textContent = "Plante";
        remplir(precision, this.choix.plantes);
      } else if (p.precision === "communaute") {
        titre.textContent = "Contre";
        remplir(precision, [{ cle: "0", libelle: "quiconque" }, ...this.choix.communautes]);
      }
      const extra = precision.selectedOptions[0]?.textContent ?? "";
      const phrase = p.modele.replace("{cible}", qui.nom).replace("{extra}", extra)
        .replace("Ne va pas à la guerre contre la quiconque.", "Ne va pas à la guerre.");
      apercu.textContent = `${this.dest().libelle} recevra ${mode.selectedOptions[0]?.textContent ?? ""} : « ${phrase} »`;
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
        this.ecrire(["Choisissez d'abord la personne désignée."]);
        return;
      }
      if (p.nuit && !window.confirm(`L'Entité va ordonner : « ${apercu.textContent?.split("« ")[1]?.replace(" »", "") ?? p.libelle} ». Continuer ?`)) return;
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
        this.ecrire([e instanceof Error && e.message.includes("HubException") ? e.message.split("HubException: ").pop() ?? "Refusé." : "La parole n'a pas pu être dite."]);
      } finally {
        bouton.disabled = false;
      }
    });
    bloc.append(ligne("Parole", parole), ligne("Comment", mode), qui.bloc, lignePrecision, apercu, bouton);
    return bloc;
  }

  private inspirer(): HTMLElement {
    const bloc = el("div");
    const pour = new Designer(this.dest, "Pour", true);
    const force = el("select");
    remplir(force, [{ cle: "leger", libelle: "un penchant léger (quelques semaines)" }, { cle: "passion", libelle: "une passion (des mois)" }]);
    const mutuel = el("input");
    const orientation = el("input");
    mutuel.type = orientation.type = "checkbox";
    const options = el("div", "ligne");
    const l1 = el("label");
    l1.append(mutuel, " réciproque (coup de foudre)");
    const l2 = el("label");
    l2.append(orientation, " changer son orientation s'il le faut");
    options.append(l1, l2);
    const bouton = el("button", undefined, "Inspirer");
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
        this.ecrire(["Le penchant n'a pas pu être inspiré."]);
      } finally {
        bouton.disabled = false;
      }
    });
    bloc.append(pour.bloc, ligne("Force", force), options, bouton,
      el("p", "discret", "Le penchant naît sans un mot et s'efface avec le temps. Envers un parent, il fait presque toujours horreur."));
    return bloc;
  }
}
