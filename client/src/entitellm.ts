import { connexion } from "./connexion";
import { el } from "./fiche";
import { nombre, t } from "./langue";
import { BlocDepenses, type ConnexionLlm, type Depenses, estCloud, FormulaireConnexion, type InfosCloud, message, type PrixModele } from "./llmcloud";

interface Reglages {
  mode: string; prieres: boolean; partOrdinaires: number; attendre: boolean;
  paroles: boolean; bienfaits: boolean; chatiments: boolean; catastrophes: boolean; souffles: boolean;
  regard: boolean; regardJours: number; caractere: string; libre: boolean; juge: boolean; monde: boolean; trace: boolean;
}
interface Etat {
  connexion: ConnexionLlm; reglages: Reglages; enFile: number; enCours: string | null; derniereErreur: string | null; propositions: number;
  caractereLlm: string; prochainRegard: string | null; libreDepuis: string | null; traceChemin?: string;
  juge?: ConnexionLlm | null; depenses?: Depenses | null; cles?: Record<string, string> | null; prix?: PrixModele[] | null;
}
interface Echange {
  id: number; date: string; quand: string; occasion: string; humain: number; nomHumain: string | null; communaute: number; nomCommunaute: string | null;
  priere: number; modele: string; secondes: number; mots: string; pensee: string; actes: string[]; resultats: string[]; statut: string; statutCode: string;
  erreur: string | null; journalPremier: number; lu?: string | null; reponse?: string | null; cout?: string | null;
}

const MODES = ["Joueur", "Agit", "Propose"];
const FAMILLES: (keyof Reglages)[] = ["paroles", "bienfaits", "chatiments", "catastrophes", "souffles"];

function tete(titre: string, fermer: () => void): HTMLElement {
  const entete = el("div", "tete");
  entete.append(el("h2", undefined, titre));
  const b = el("button", "fermer", "×");
  b.type = "button";
  b.title = t("commun.fermer");
  b.addEventListener("click", fermer);
  entete.append(b);
  return entete;
}

function bouton(texte: string, action: () => void, classe?: string): HTMLButtonElement {
  const b = el("button", classe, texte);
  b.type = "button";
  b.addEventListener("click", action);
  return b;
}

function caseACocher(texte: string, aide?: string): [HTMLLabelElement, HTMLInputElement] {
  const i = el("input");
  i.type = "checkbox";
  const l = el("label", "case-config");
  l.append(i, " " + texte);
  if (aide) l.title = aide;
  return [l, i];
}

/** Un échange en texte simple (pour « Copier en texte »). */
export function echangeEnTexte(e: Echange): string {
  const qui = e.nomHumain ? `${e.nomHumain} (n°${e.humain})` : e.nomCommunaute ?? "";
  const lignes = [`## ${e.date} · ${e.occasion} · ${qui} · ${e.statut}`];
  if (e.mots) lignes.push(`${t("ellm.mots")} : « ${e.mots} »`);
  if (e.pensee) lignes.push(`${t("ellm.pensee")} : ${e.pensee}`);
  for (const a of e.actes) lignes.push(`- ${t("ellm.acte")} : ${a}`);
  for (const r of e.resultats) lignes.push(`- ${t("ellm.resultat")} : ${r}`);
  if (e.erreur) lignes.push(`${t("ellm.erreur")} : ${e.erreur}`);
  lignes.push(`(${e.modele}, ${e.secondes} s${e.cout ? ", " + e.cout : ""})`);
  if (e.lu) lignes.push("", "### " + t("ellm.lu"), e.lu);
  if (e.reponse) lignes.push("", "### " + t("ellm.reponse"), e.reponse);
  return lignes.join("\n");
}

async function copier(texte: string, retour: HTMLElement): Promise<void> {
  try {
    await navigator.clipboard.writeText(texte);
    retour.textContent = t("ellm.copie");
  } catch {
    retour.textContent = t("ellm.copieImpossible");
  }
}

/**
 * Bouton « Que ferait l'Entité ? » d'une fiche (humain, ou communauté si humain = 0) : la demande part dans la file de l'Entité-LLM ;
 * la réponse (appliquée ou proposée selon le mode) apparaît dans la fenêtre « Entité LLM ».
 */
export function blocQueFerait(humain: () => number, communaute: () => number): HTMLElement {
  // « entite-bloc » : la fiche d'un humain le range avec les formulaires de l'Entité, hors des sections repliables.
  const bloc = el("div", "que-ferait entite-bloc");
  const retour = el("span", "discret");
  const b = bouton(t("ellm.queFerait"), () => {
    b.disabled = true;
    retour.textContent = "";
    connexion.invoke<number>("QueFeraitLEntite", humain(), humain() === 0 ? communaute() : 0)
      .then((place) => { retour.textContent = " " + t("ellm.enFile", { n: place }); })
      .catch((e: unknown) => { retour.textContent = " " + (e instanceof Error ? e.message.replace(/^.*HubException: /, "") : String(e)); })
      .finally(() => { b.disabled = false; });
  }, "entite");
  b.title = t("ellm.queFeraitAide");
  bloc.append(b, retour);
  return bloc;
}

/**
 * Fenêtre « Entité LLM » : connexion au LLM local, qui joue l'Entité, propositions en attente et trace des conversations.
 */
export class PanneauEntiteLlm {
  private readonly formulaire: FormulaireConnexion;
  private readonly formulaireJuge: FormulaireConnexion;
  private readonly memeJuge: HTMLInputElement;
  private readonly blocJuge = el("div");
  private readonly retourJuge = el("p", "discret");
  private readonly depenses: BlocDepenses;
  private local = true;
  private readonly modes: HTMLInputElement[] = [];
  private readonly prieres: HTMLInputElement;
  private readonly part = el("input");
  private readonly attendre: HTMLInputElement;
  private readonly regard: HTMLInputElement;
  private readonly libre: HTMLInputElement;
  private readonly monde: HTMLInputElement;
  private readonly trace: HTMLInputElement;
  private readonly traceCommande = el("code");
  private readonly juge: HTMLInputElement;
  private readonly regardJours = el("input");
  private readonly caractere = el("textarea");
  private readonly caractereLlm = el("p", "discret");
  private readonly familles = new Map<keyof Reglages, HTMLInputElement>();
  private readonly retourReglages = el("p", "discret");
  private readonly etat = el("p", "etat-llm");
  private readonly propositions = el("ul", "liste echanges");
  private readonly echanges = el("ul", "liste echanges");
  private readonly retourCopie = el("span", "discret");
  private dernieres: Echange[] = [];

  constructor(private readonly racine: HTMLElement, private readonly ouvrirHumain: (id: number) => void,
    private readonly ouvrirCommunaute: (id: number) => void) {
    // Le LLM de l'Entité et des dialogues, puis (0.38.0, G2) celui du juge et des traductions.
    const rafraichir = async () => { this.remplir(await connexion.invoke<Etat>("EtatEntiteLlm"), false); };
    this.formulaire = new FormulaireConnexion("entite", async (c) => {
      const e = await connexion.invoke<Etat>("ReglerConnexionLlm", c);
      this.remplir(e, false);
      this.formulaire.remplir(e.connexion, this.infos(e));
    }, rafraichir);
    this.formulaireJuge = new FormulaireConnexion("juge", async (c) => {
      const e = await connexion.invoke<Etat>("ReglerConnexionJugeLlm", c);
      this.remplir(e, false);
      this.formulaireJuge.remplir(e.juge ?? e.connexion, this.infos(e));
    }, rafraichir);
    this.depenses = new BlocDepenses(rafraichir);
    const ligne = (libelle: string, ...champs: (HTMLElement | string)[]) => {
      const l = el("label", "ligne");
      l.append(el("span", undefined, libelle), " ", ...champs);
      return l;
    };
    const connexionBloc = el("section", "bloc-llm");
    connexionBloc.append(el("h3", undefined, t("ellm.connexion")), el("p", "discret", t("ellm.connexionAide")), this.formulaire.racine);
    const jugeBloc = el("section", "bloc-llm");
    let lm: HTMLLabelElement;
    [lm, this.memeJuge] = caseACocher(t("ellm.juge2.meme"));
    this.memeJuge.addEventListener("change", () => {
      this.blocJuge.hidden = this.memeJuge.checked;
      if (this.memeJuge.checked) void this.jugeCommeEntite();
    });
    this.blocJuge.append(this.formulaireJuge.racine);
    jugeBloc.append(el("h3", undefined, t("ellm.juge2.titre")), el("p", "discret", t("ellm.juge2.aide")), lm, this.retourJuge, this.blocJuge);

    const reglagesBloc = el("section", "bloc-llm");
    reglagesBloc.append(el("h3", undefined, t("ellm.quiJoue")));
    for (const m of MODES) {
      const i: HTMLInputElement = el("input");
      i.type = "radio";
      i.name = "modeEntiteLlm";
      i.value = m;
      const l = el("label", "case-config");
      l.append(i, " " + t(`ellm.mode.${m}`));
      l.title = t(`ellm.mode.${m}.aide`);
      this.modes.push(i);
      reglagesBloc.append(l, el("br"));
    }
    let l: HTMLLabelElement;
    // Entité libre : s'ajoute aux modes « agit » et « propose » (0.35.2).
    let lj: HTMLLabelElement;
    [l, this.libre] = caseACocher(t("ellm.libre"), t("ellm.libreAide"));
    [lj, this.juge] = caseACocher(t("ellm.juge"), t("ellm.jugeAide"));
    reglagesBloc.append(el("h4", undefined, t("ellm.libreTitre")), l, el("br"), lj, el("p", "discret", t("ellm.libreDetail")));
    // Parler au Monde (0.37.1) : une case à part, décochée par défaut.
    [l, this.monde] = caseACocher(t("ellm.monde"), t("ellm.mondeAide"));
    reglagesBloc.append(el("h4", undefined, t("ellm.mondeTitre")), l, el("br"));
    // Suivre les échanges dans un terminal (0.37.2) : un fichier texte en clair, et une ligne courte dans la console du serveur.
    [l, this.trace] = caseACocher(t("ellm.trace"), t("ellm.traceAide"));
    reglagesBloc.append(el("h4", undefined, t("ellm.traceTitre")), l, el("p", "discret", t("ellm.traceCommande")), this.traceCommande);
    [l, this.prieres] = caseACocher(t("ellm.prieres"), t("ellm.prieresAide"));
    reglagesBloc.append(el("h4", undefined, t("ellm.occasions")), l, el("br"));
    this.part.type = "number";
    this.part.min = "0";
    this.part.max = "100";
    this.part.step = "1";
    this.part.className = "nombre-court";
    reglagesBloc.append(ligne(t("ellm.part"), this.part, " %"), el("p", "discret", t("ellm.demandeAide")));
    [l, this.regard] = caseACocher(t("ellm.regard"), t("ellm.regardAide"));
    this.regardJours.type = "number";
    this.regardJours.min = "1";
    this.regardJours.max = "3650";
    this.regardJours.step = "1";
    this.regardJours.className = "nombre-court";
    reglagesBloc.append(l, " ", ligne(t("ellm.regardJours"), this.regardJours, " " + t("ellm.jours")));
    [l, this.attendre] = caseACocher(t("ellm.attendre"), t("ellm.attendreAide"));
    reglagesBloc.append(l, el("br"), el("h4", undefined, t("ellm.caractere")), el("p", "discret", t("ellm.caractereAide")));
    this.caractere.rows = 3;
    this.caractere.maxLength = 2000;
    this.caractere.className = "caractere-entite";
    this.caractere.placeholder = t("ellm.caractereExemple");
    reglagesBloc.append(this.caractere, this.caractereLlm, el("h4", undefined, t("ellm.familles")));
    for (const f of FAMILLES) {
      const [lf, i] = caseACocher(t(`ellm.famille.${f}`), t(`ellm.famille.${f}.aide`));
      this.familles.set(f, i);
      reglagesBloc.append(lf, el("br"));
    }
    reglagesBloc.append(bouton(t("ellm.enregistrer"), () => void this.enregistrerReglages()), this.retourReglages);

    const corps = el("div", "corps");
    corps.append(el("p", "discret", t("ellm.aide")), this.etat, connexionBloc, jugeBloc, this.depenses.racine, reglagesBloc,
      el("h3", undefined, t("ellm.propositions")), this.propositions,
      el("h3", undefined, t("ellm.conversations")),
      el("p", "discret", t("ellm.conversationsAide")),
      bouton(t("ellm.copierTout"), () => void copier(this.dernieres.map(echangeEnTexte).join("\n\n"), this.retourCopie)), this.retourCopie,
      this.echanges);
    racine.append(tete(t("ellm.titre"), () => this.fermer()), corps);
    connexion.on("EntiteLlm", (e: Etat) => {
      if (!this.ouvert) return;
      this.montrerEtat(e);
      void this.chargerEchanges();
    });
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
    const [e, local] = await Promise.all([connexion.invoke<Etat>("EtatEntiteLlm"), connexion.invoke<boolean>("ServeurLocalLlm")]);
    this.local = local;
    this.remplir(e, true, true);
    await this.chargerEchanges();
  }

  fermer(): void {
    this.racine.hidden = true;
  }

  private infos(e: Etat): InfosCloud {
    return { cles: e.cles ?? {}, prix: e.prix ?? [], depenses: e.depenses ?? null, local: this.local };
  }

  /** Remplit la fenêtre ; <connexions> : remet aussi les deux formulaires de LLM (sinon, seulement les clés et la dépense). */
  private remplir(e: Etat, connexions = true, reglages = false): void {
    const infos = this.infos(e);
    if (connexions) {
      this.formulaire.remplir(e.connexion, infos);
      this.memeJuge.checked = !e.juge;
      this.blocJuge.hidden = !e.juge;
      this.formulaireJuge.remplir(e.juge ?? e.connexion, infos);
    } else {
      this.formulaire.majInfos(infos);
      this.formulaireJuge.majInfos(infos);
    }
    this.montrerEtat(e);
    if (!reglages) return;
    for (const i of this.modes) i.checked = i.value === e.reglages.mode;
    this.prieres.checked = e.reglages.prieres;
    this.part.value = String(e.reglages.partOrdinaires);
    this.attendre.checked = e.reglages.attendre;
    this.regard.checked = e.reglages.regard;
    this.regardJours.value = String(e.reglages.regardJours);
    this.caractere.value = e.reglages.caractere ?? "";
    this.libre.checked = e.reglages.libre;
    this.juge.checked = e.reglages.juge;
    this.monde.checked = e.reglages.monde ?? false;
    this.trace.checked = e.reglages.trace ?? false;
    this.traceCommande.textContent = e.traceChemin ? `tail -f "${e.traceChemin}"` : "";
    for (const [f, i] of this.familles) i.checked = Boolean(e.reglages[f]);
    this.montrerEtat(e);
  }

  private montrerEtat(e: Etat): void {
    const cloud = estCloud(e.connexion.fournisseur) || (e.juge ? estCloud(e.juge.fournisseur) : false);
    this.depenses.montrer(e.depenses ?? null, this.local, cloud || (e.depenses?.appels ?? 0) > 0);
    const parts = [e.connexion.modele ? t("ellm.etat.modele", { modele: e.connexion.modele }) : t("ellm.etat.sansModele"),
      t(`ellm.mode.${e.reglages.mode}`), t("ellm.etat.file", { n: e.enFile })];
    if (e.enCours) parts.push(t("ellm.etat.enCours", { nom: e.enCours }));
    if (e.propositions > 0) parts.push(t("ellm.etat.propositions", { n: e.propositions }));
    if (e.prochainRegard) parts.push(t("ellm.etat.prochainRegard", { date: e.prochainRegard }));
    if (e.reglages.libre && e.reglages.mode !== "Joueur") parts.push(t("ellm.etat.libre"));
    if (e.libreDepuis) parts.push(t("ellm.etat.libreDepuis", { date: e.libreDepuis }));
    this.etat.textContent = parts.join(" · ");
    this.caractereLlm.replaceChildren();
    if (!e.reglages.caractere?.trim()) {
      if (e.caractereLlm) {
        this.caractereLlm.append(t("ellm.caractereLlm"), " ", el("q", "mots-entite", e.caractereLlm), " ",
          bouton(t("ellm.oublier"), () => void this.oublierCaractere(), "lien"));
      } else this.caractereLlm.append(t("ellm.caractereAucun"));
    }
    if (e.derniereErreur) this.etat.append(el("br"), el("span", "erreur", e.derniereErreur));
    // Avertissement sur les coûts (0.38.0) : rappelé en tête de la fenêtre tant qu'un LLM du cloud est réglé.
    if (cloud && e.depenses) {
      const d = e.depenses;
      this.etat.append(el("br"), el("span", d.atteint || d.alerte ? "erreur" : "discret",
        t("ellm.dep.enTete", { depense: dollarsCourts(d.dollars), plafond: d.plafond > 0 ? dollarsCourts(d.plafond) : t("ellm.cloud.sansPlafond") })));
    }
  }

  private async jugeCommeEntite(): Promise<void> {
    try {
      this.remplir(await connexion.invoke<Etat>("ReglerConnexionJugeLlm", null), false);
      this.retourJuge.textContent = t("ellm.enregistre");
    } catch (x) {
      this.retourJuge.textContent = message(x);
      this.memeJuge.checked = false;
      this.blocJuge.hidden = false;
    }
  }

  private async enregistrerReglages(): Promise<void> {
    const r: Reglages = {
      mode: this.modes.find((i) => i.checked)?.value ?? "Joueur", prieres: this.prieres.checked,
      partOrdinaires: Math.max(0, Math.min(100, Math.round(Number(this.part.value) || 0))), attendre: this.attendre.checked,
      paroles: false, bienfaits: false, chatiments: false, catastrophes: false, souffles: false,
      regard: this.regard.checked, regardJours: Math.max(1, Math.min(3650, Math.round(Number(this.regardJours.value) || 91))),
      caractere: this.caractere.value.trim().slice(0, 2000), libre: this.libre.checked, juge: this.juge.checked,
      monde: this.monde.checked, trace: this.trace.checked,
    };
    for (const [f, i] of this.familles) (r as unknown as Record<string, boolean>)[f] = i.checked;
    try {
      this.montrerEtat(await connexion.invoke<Etat>("ReglerEntiteLlm", r));
      this.retourReglages.textContent = t("ellm.enregistre");
    } catch (e) {
      this.retourReglages.textContent = message(e);
    }
  }

  private async oublierCaractere(): Promise<void> {
    try {
      this.montrerEtat(await connexion.invoke<Etat>("OublierCaractereLlm"));
    } catch (e) {
      this.retourReglages.textContent = message(e);
    }
  }

  private async chargerEchanges(): Promise<void> {
    const [propositions, echanges] = await Promise.all([
      connexion.invoke<Echange[]>("EchangesLlm", 0, 0, 1, 0),
      connexion.invoke<Echange[]>("EchangesLlm", 0, 0, null, 0),
    ]);
    this.dernieres = echanges;
    this.propositions.replaceChildren(...propositions.map((e) => this.ligne(e, true)));
    if (propositions.length === 0) this.propositions.append(el("li", "discret", t("ellm.aucuneProposition")));
    this.echanges.replaceChildren(...echanges.map((e) => this.ligne(e, false)));
    if (echanges.length === 0) this.echanges.append(el("li", "discret", t("ellm.aucunEchange")));
  }

  private ligne(e: Echange, proposition: boolean): HTMLLIElement {
    const li = el("li", `echange ${e.statutCode}`);
    li.append(`${e.date} · ${e.occasion} · `);
    if (e.humain > 0 && e.nomHumain) li.append(bouton(e.nomHumain, () => this.ouvrirHumain(e.humain), "lien"));
    else if (e.communaute > 0 && e.nomCommunaute) li.append(bouton(e.nomCommunaute, () => this.ouvrirCommunaute(e.communaute), "lien"));
    li.append(el("span", "statut-echange", ` · ${e.statut}`));
    if (e.mots) li.append(el("br"), el("q", "mots-entite", e.mots));
    if (e.pensee) li.append(el("br"), el("span", "discret", `${t("ellm.pensee")} : ${e.pensee}`));
    if (e.actes.length > 0) {
      const ul = el("ul", "actes-llm");
      e.actes.forEach((a, i) => {
        const x = el("li");
        x.append(el("code", undefined, a));
        if (e.resultats[i]) x.append(" → ", e.resultats[i]);
        ul.append(x);
      });
      li.append(ul);
    } else if (e.resultats.length > 0) li.append(el("br"), e.resultats.join(" "));
    if (e.erreur) li.append(el("br"), el("span", "erreur", e.erreur));
    li.append(el("br"), el("span", "discret", `${e.modele} · ${e.secondes} s${e.cout ? " · " + e.cout : ""} `));
    const details = el("pre", "lu-llm");
    details.hidden = true;
    li.append(bouton(t("ellm.voirLu"), () => void this.voirLu(e, details), "lien"), " ",
      bouton(t("ellm.copierEchange"), () => void this.copierUn(e, li), "lien"));
    if (proposition) {
      const retour = el("span", "discret");
      li.append(el("br"), bouton(t("ellm.accepter"), () => void this.accepter(e, retour), "exaucer"), " ",
        bouton(t("ellm.rejeter"), () => void this.rejeter(e, retour), "refuser"), retour);
    }
    li.append(details);
    return li;
  }

  private async complet(e: Echange): Promise<Echange> {
    return (await connexion.invoke<Echange | null>("EchangeLlm", e.id)) ?? e;
  }

  private async voirLu(e: Echange, pre: HTMLPreElement): Promise<void> {
    if (!pre.hidden) {
      pre.hidden = true;
      return;
    }
    const c = await this.complet(e);
    pre.textContent = `${c.lu ?? ""}\n\n---- ${t("ellm.reponse")} ----\n${c.reponse ?? ""}`;
    pre.hidden = false;
  }

  private async copierUn(e: Echange, li: HTMLElement): Promise<void> {
    const retour = el("span", "discret");
    li.append(" ", retour);
    await copier(echangeEnTexte(await this.complet(e)), retour);
  }

  private async accepter(e: Echange, retour: HTMLElement): Promise<void> {
    try {
      const r = await connexion.invoke<string[]>("AccepterProposition", e.id);
      retour.textContent = " " + r.join(" ");
      await this.chargerEchanges();
    } catch (x) {
      retour.textContent = " " + message(x);
    }
  }

  private async rejeter(e: Echange, retour: HTMLElement): Promise<void> {
    try {
      this.montrerEtat(await connexion.invoke<Etat>("RejeterProposition", e.id));
      await this.chargerEchanges();
    } catch (x) {
      retour.textContent = " " + message(x);
    }
  }
}

function dollarsCourts(d: number): string {
  return (d < 0.1 ? nombre(d, 3) : nombre(d, 2)) + " $";
}
