import { connexion } from "./connexion";
import { el } from "./fiche";
import { t } from "./langue";

interface ConnexionLlm { fournisseur: string; adresse: string; modele: string; contexte: number }
interface Reglages {
  mode: string; prieres: boolean; partOrdinaires: number; attendre: boolean;
  paroles: boolean; bienfaits: boolean; chatiments: boolean; catastrophes: boolean; souffles: boolean;
}
interface Etat { connexion: ConnexionLlm; reglages: Reglages; enFile: number; enCours: string | null; derniereErreur: string | null; propositions: number }
interface Echange {
  id: number; date: string; quand: string; occasion: string; humain: number; nomHumain: string | null; communaute: number; nomCommunaute: string | null;
  priere: number; modele: string; secondes: number; mots: string; pensee: string; actes: string[]; resultats: string[]; statut: string; statutCode: string;
  erreur: string | null; journalPremier: number; lu?: string | null; reponse?: string | null;
}

const FOURNISSEURS: [string, string][] = [["ollama", "Ollama"], ["lmstudio", "LM Studio"], ["llamacpp", "llama.cpp"]];
const ADRESSES: Record<string, string> = { ollama: "http://localhost:11434", lmstudio: "http://localhost:1234", llamacpp: "http://localhost:8080" };
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
  lignes.push(`(${e.modele}, ${e.secondes} s)`);
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
  const bloc = el("div", "que-ferait");
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
  private readonly fournisseur = el("select");
  private readonly adresse = el("input");
  private readonly modele = el("input");
  private readonly modeles = el("datalist");
  private readonly retourConnexion = el("p", "discret");
  private readonly modes: HTMLInputElement[] = [];
  private readonly prieres: HTMLInputElement;
  private readonly part = el("input");
  private readonly attendre: HTMLInputElement;
  private readonly familles = new Map<keyof Reglages, HTMLInputElement>();
  private readonly retourReglages = el("p", "discret");
  private readonly etat = el("p", "etat-llm");
  private readonly propositions = el("ul", "liste echanges");
  private readonly echanges = el("ul", "liste echanges");
  private readonly retourCopie = el("span", "discret");
  private dernieres: Echange[] = [];

  constructor(private readonly racine: HTMLElement, private readonly ouvrirHumain: (id: number) => void,
    private readonly ouvrirCommunaute: (id: number) => void) {
    for (const [cle, nom] of FOURNISSEURS) this.fournisseur.append(Object.assign(el("option", undefined, nom), { value: cle }));
    this.fournisseur.addEventListener("change", () => { this.adresse.value = ADRESSES[this.fournisseur.value] ?? ""; });
    this.adresse.type = "url";
    this.adresse.size = 28;
    this.modeles.id = "listeModelesLlm";
    this.modele.setAttribute("list", this.modeles.id);
    this.modele.size = 28;
    this.modele.placeholder = t("ellm.modeleExemple");
    const ligne = (libelle: string, ...champs: (HTMLElement | string)[]) => {
      const l = el("label", "ligne");
      l.append(el("span", undefined, libelle), " ", ...champs);
      return l;
    };
    const connexionBloc = el("section", "bloc-llm");
    connexionBloc.append(el("h3", undefined, t("ellm.connexion")), el("p", "discret", t("ellm.connexionAide")),
      ligne(t("ellm.fournisseur"), this.fournisseur), ligne(t("ellm.adresse"), this.adresse), ligne(t("ellm.modele"), this.modele, this.modeles),
      bouton(t("ellm.tester"), () => void this.tester()), " ", bouton(t("ellm.enregistrer"), () => void this.enregistrerConnexion()), this.retourConnexion);

    const reglagesBloc = el("section", "bloc-llm");
    reglagesBloc.append(el("h3", undefined, t("ellm.quiJoue")));
    for (const m of MODES) {
      const i = el("input");
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
    [l, this.prieres] = caseACocher(t("ellm.prieres"), t("ellm.prieresAide"));
    reglagesBloc.append(el("h4", undefined, t("ellm.occasions")), l, el("br"));
    this.part.type = "number";
    this.part.min = "0";
    this.part.max = "100";
    this.part.step = "1";
    this.part.className = "nombre-court";
    reglagesBloc.append(ligne(t("ellm.part"), this.part, " %"), el("p", "discret", t("ellm.demandeAide")));
    [l, this.attendre] = caseACocher(t("ellm.attendre"), t("ellm.attendreAide"));
    reglagesBloc.append(l, el("br"), el("h4", undefined, t("ellm.familles")));
    for (const f of FAMILLES) {
      const [lf, i] = caseACocher(t(`ellm.famille.${f}`), t(`ellm.famille.${f}.aide`));
      this.familles.set(f, i);
      reglagesBloc.append(lf, el("br"));
    }
    reglagesBloc.append(bouton(t("ellm.enregistrer"), () => void this.enregistrerReglages()), this.retourReglages);

    const corps = el("div", "corps");
    corps.append(el("p", "discret", t("ellm.aide")), this.etat, connexionBloc, reglagesBloc,
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
    const e = await connexion.invoke<Etat>("EtatEntiteLlm");
    this.remplir(e);
    await this.chargerEchanges();
  }

  fermer(): void {
    this.racine.hidden = true;
  }

  private remplir(e: Etat): void {
    this.fournisseur.value = e.connexion.fournisseur;
    this.adresse.value = e.connexion.adresse;
    this.modele.value = e.connexion.modele;
    for (const i of this.modes) i.checked = i.value === e.reglages.mode;
    this.prieres.checked = e.reglages.prieres;
    this.part.value = String(e.reglages.partOrdinaires);
    this.attendre.checked = e.reglages.attendre;
    for (const [f, i] of this.familles) i.checked = Boolean(e.reglages[f]);
    this.montrerEtat(e);
  }

  private montrerEtat(e: Etat): void {
    const parts = [e.connexion.modele ? t("ellm.etat.modele", { modele: e.connexion.modele }) : t("ellm.etat.sansModele"),
      t(`ellm.mode.${e.reglages.mode}`), t("ellm.etat.file", { n: e.enFile })];
    if (e.enCours) parts.push(t("ellm.etat.enCours", { nom: e.enCours }));
    if (e.propositions > 0) parts.push(t("ellm.etat.propositions", { n: e.propositions }));
    this.etat.textContent = parts.join(" · ");
    if (e.derniereErreur) this.etat.append(el("br"), el("span", "erreur", e.derniereErreur));
  }

  private lireConnexion(): ConnexionLlm {
    return { fournisseur: this.fournisseur.value, adresse: this.adresse.value.trim(), modele: this.modele.value.trim(), contexte: 16384 };
  }

  private async tester(): Promise<void> {
    this.retourConnexion.textContent = t("ellm.test.enCours");
    try {
      const liste = await connexion.invoke<string[]>("ModelesLlm", this.lireConnexion());
      this.modeles.replaceChildren(...liste.map((m) => Object.assign(el("option"), { value: m })));
      this.retourConnexion.textContent = liste.length > 0 ? t("ellm.test.ok", { n: liste.length, liste: liste.join(", ") }) : t("ellm.test.aucun");
      if (!this.modele.value && liste.length > 0) this.modele.value = liste[0];
    } catch (e) {
      this.retourConnexion.textContent = message(e);
    }
  }

  private async enregistrerConnexion(): Promise<void> {
    try {
      this.montrerEtat(await connexion.invoke<Etat>("ReglerConnexionLlm", this.lireConnexion()));
      this.retourConnexion.textContent = t("ellm.enregistre");
    } catch (e) {
      this.retourConnexion.textContent = message(e);
    }
  }

  private async enregistrerReglages(): Promise<void> {
    const r: Reglages = {
      mode: this.modes.find((i) => i.checked)?.value ?? "Joueur", prieres: this.prieres.checked,
      partOrdinaires: Math.max(0, Math.min(100, Math.round(Number(this.part.value) || 0))), attendre: this.attendre.checked,
      paroles: false, bienfaits: false, chatiments: false, catastrophes: false, souffles: false,
    };
    for (const [f, i] of this.familles) (r as unknown as Record<string, boolean>)[f] = i.checked;
    try {
      this.montrerEtat(await connexion.invoke<Etat>("ReglerEntiteLlm", r));
      this.retourReglages.textContent = t("ellm.enregistre");
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
    li.append(el("br"), el("span", "discret", `${e.modele} · ${e.secondes} s `));
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

function message(e: unknown): string {
  return e instanceof Error ? e.message.replace(/^.*HubException: /, "") : String(e);
}
