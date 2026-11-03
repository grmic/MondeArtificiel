import { connexion } from "./connexion";
import { el } from "./fiche";
import { t } from "./langue";
import { vignette } from "./visage";

interface Replique {
  rang: number; date: string; entite: string; traduction: string[]; regles: string[]; mots: string; pensee: string; libre: string[];
  juge: string | null; erreur: string | null; secondes: number; journalPremier: number; sens: string;
}
interface Dialogue {
  id: number; humain: number; nom: string; mode: string; voie: string; priere: number; textePriere: string | null; debut: string; fin: string | null;
  finTexte: string | null; enCours: boolean; attend: string | null; repliques: number; croit: string; liste: Replique[]; erreur: string | null;
}
interface DialogueResume { id: number; humain: number; nom: string; voie: string; debut: string; repliques: number; finTexte: string | null; enCours: boolean; croit: string }
interface Appel { priere: number; humain: number; nom: string; texte: string }

/** Voies d'un dialogue : le signe ne se prête pas à une conversation. */
const VOIES = ["Reve", "Voix", "Apparition"];

function bouton(texte: string, action: () => void, classe?: string): HTMLButtonElement {
  const b = el("button", classe, texte);
  b.type = "button";
  b.addEventListener("click", action);
  return b;
}

function message(e: unknown): string {
  return e instanceof Error ? e.message.replace(/^.*HubException: /, "") : String(e);
}

/** Un LLM est-il choisi ? (il joue l'humain : sans lui, pas de dialogue.) Lu une fois par fiche. */
async function modeleChoisi(): Promise<boolean> {
  try {
    const etat = await connexion.invoke<{ connexion: { modele: string } }>("EtatEntiteLlm");
    return etat.connexion.modele.length > 0;
  } catch {
    return false;
  }
}

let ouvrir: (humain: number, nom: string, priere?: number) => void = () => {};

/** Ouvre la fenêtre de dialogue avec un humain (depuis une fiche ou un appel). */
export function ouvrirDialogue(humain: number, nom: string, priere = 0): void {
  ouvrir(humain, nom, priere);
}

/** Bouton « Parler avec… » d'une fiche : grisé tant qu'aucun LLM n'est choisi (c'est lui qui joue l'humain). */
export function blocDialogue(humain: () => number, nom: () => string): HTMLElement {
  const bloc = el("div", "dialogue-bloc entite-bloc");
  const b = bouton(t("dlgc.parler", { nom: nom() }), () => ouvrirDialogue(humain(), nom()), "entite");
  b.title = t("dlgc.parlerAide");
  b.disabled = true;
  void modeleChoisi().then((oui) => {
    b.disabled = !oui;
    if (!oui) b.title = t("dlgc.sansModele");
  });
  bloc.append(b);
  return bloc;
}

/**
 * Fenêtre « Dialogue » : l'Entité (le joueur) parle à un humain par une voie (rêve, voix, apparition) ; le LLM traduit ses mots en
 * paroles codées, les règles décident, et le LLM fait répondre l'humain. L'horloge du monde est arrêtée pendant le dialogue. Un
 * second onglet liste les conversations passées.
 */
export class FenetreDialogue {
  private readonly titre = el("h2");
  private readonly corps = el("div", "corps");
  private readonly saisie = el("textarea");
  private readonly dire: HTMLButtonElement;
  private readonly finir: HTMLButtonElement;
  private readonly pied = el("div", "dialogue-pied");
  private readonly retour = el("p", "discret");
  private id = 0;
  /** Humain dont on attend le sommeil pour lui parler en rêve (0 : aucun). */
  private attente = 0;
  private dernier: Dialogue | null = null;

  constructor(private readonly racine: HTMLElement, private readonly ouvrirHumain: (id: number) => void) {
    const tete = el("div", "tete");
    const conversations = bouton(t("dlgc.conversations"), () => void this.liste(), "lien-discret");
    conversations.title = t("dlgc.conversationsAide");
    const fermer = bouton("×", () => this.fermer(), "fermer");
    fermer.title = t("commun.fermer");
    tete.append(this.titre, conversations, fermer);
    this.saisie.rows = 3;
    this.saisie.maxLength = 1200;
    this.saisie.placeholder = t("dlgc.saisie");
    this.saisie.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        void this.envoyer();
      }
    });
    this.dire = bouton(t("dlgc.dire"), () => void this.envoyer(), "entite");
    this.dire.title = t("dlgc.direAide");
    this.finir = bouton(t("dlgc.finir"), () => void this.terminer());
    this.finir.title = t("dlgc.finirAide");
    const boutons = el("div", "boutons");
    boutons.append(this.dire, " ", this.finir);
    this.pied.append(this.saisie, boutons, this.retour);
    racine.append(tete, this.corps, this.pied);
    ouvrir = (h, n, p) => void this.preparer(h, n, p ?? 0);
    connexion.on("Dialogue", (d: Dialogue | null) => {
      if (this.racine.hidden) return;
      // Le rêve attendu s'ouvre : il s'est endormi.
      if (d && this.id === 0 && this.attente !== 0 && d.humain === this.attente) {
        this.attente = 0;
        this.id = d.id;
        this.afficher(d);
        this.saisie.focus();
        return;
      }
      if (this.id === 0) return;
      if (d && d.id === this.id) this.afficher(d);
      else if (!d && this.dernier?.enCours) void this.relire();
    });
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  fermer(): void {
    this.racine.hidden = true;
  }

  /** Pour un humain : reprend le dialogue en cours avec lui, sinon propose d'en commencer un (choix de la voie). */
  private async preparer(humain: number, nom: string, priere: number): Promise<void> {
    this.racine.hidden = false;
    this.retour.textContent = "";
    const courant = await connexion.invoke<Dialogue | null>("DialogueEnCours").catch(() => null);
    if (courant && courant.humain === humain) {
      this.id = courant.id;
      this.afficher(courant);
      return;
    }
    this.id = 0;
    this.dernier = null;
    this.titre.replaceChildren(vignette(humain, 28), " ", t("dlgc.titre", { nom }));
    this.pied.hidden = true;
    const attendu = await connexion.invoke<{ humain: number } | null>("DialogueAttendu").catch(() => null);
    if (attendu && attendu.humain === humain) {
      this.attendre(humain, nom);
      return;
    }
    const choix = el("div", "dialogue-choix");
    if (courant) choix.append(el("p", "discret", t("dlgc.autreEnCours", { nom: courant.nom })));
    choix.append(el("p", undefined, t("dlgc.choisirVoie", { nom })));
    let voie = "Reve";
    for (const v of VOIES) {
      const l = el("label", "case-config");
      const r = el("input");
      r.type = "radio";
      r.name = "voieDialogue";
      r.value = v;
      r.checked = v === voie;
      r.addEventListener("change", () => { voie = v; });
      l.append(r, " " + t(`dlgc.voie.${v}`));
      l.title = t(`dlgc.voieAide.${v}`);
      choix.append(l, el("br"));
    }
    const erreur = el("p", "discret");
    // On ne parle en rêve qu'à un dormeur : sinon, on attend son sommeil (le monde continue).
    const sommeil = el("p", "discret");
    const dort = await connexion.invoke<boolean>("Endormi", humain).catch(() => true);
    const montrerSommeil = (): void => {
      sommeil.textContent = voie === "Reve" ? t(dort ? "dlgc.dort" : "dlgc.veille", { nom }) : "";
      commencer.textContent = voie === "Reve" && !dort ? t("dlgc.attendre") : t("dlgc.commencer");
    };
    choix.querySelectorAll<HTMLInputElement>("input[name=voieDialogue]").forEach((r) => r.addEventListener("change", montrerSommeil));
    const commencer = bouton(t("dlgc.commencer"), () => {
      commencer.disabled = true;
      const attendre = voie === "Reve" && !dort;
      connexion.invoke<Dialogue | null>("OuvrirDialogue", humain, voie, priere, attendre)
        .then((d) => {
          if (d) {
            this.id = d.id;
            this.afficher(d);
            this.saisie.focus();
            return;
          }
          this.attendre(humain, nom);
        })
        .catch((e: unknown) => { erreur.textContent = message(e); commencer.disabled = false; });
    }, "entite");
    montrerSommeil();
    choix.append(sommeil, el("p", "discret", t("dlgc.aide")), commencer, erreur);
    this.corps.replaceChildren(choix);
  }

  /** En attente de son sommeil : le dialogue s'ouvrira de lui-même. */
  private attendre(humain: number, nom: string): void {
    this.attente = humain;
    const bloc = el("div", "dialogue-choix");
    const annuler = bouton(t("dlgc.annulerAttente"), () => {
      this.attente = 0;
      void connexion.invoke("AnnulerAttenteDialogue").catch(() => undefined);
      this.fermer();
    });
    bloc.append(el("p", undefined, t("dlgc.attente", { nom })), annuler);
    this.corps.replaceChildren(bloc);
  }

  private async relire(): Promise<void> {
    try {
      this.afficher(await connexion.invoke<Dialogue>("LireDialogue", this.id));
    } catch (e) {
      this.retour.textContent = message(e);
    }
  }

  private async envoyer(): Promise<void> {
    const texte = this.saisie.value.trim();
    if (!texte || this.id === 0) return;
    this.dire.disabled = true;
    try {
      const d = await connexion.invoke<Dialogue>("DireAuDialogue", this.id, texte);
      this.saisie.value = "";
      this.afficher(d);
    } catch (e) {
      this.retour.textContent = message(e);
      this.dire.disabled = false;
    }
  }

  private async terminer(): Promise<void> {
    if (this.id === 0) return;
    try {
      this.afficher(await connexion.invoke<Dialogue>("TerminerDialogue", this.id));
    } catch (e) {
      this.retour.textContent = message(e);
    }
  }

  private afficher(d: Dialogue): void {
    this.dernier = d;
    const lienNom = bouton(d.nom, () => this.ouvrirHumain(d.humain), "lien");
    this.titre.replaceChildren(vignette(d.humain, 28), " ", lienNom, el("span", "discret", ` · ${d.voie}`));
    const fil = el("div", "dialogue-fil");
    fil.append(el("p", "discret", t(d.enCours ? "dlgc.etat" : "dlgc.etatFini", { debut: d.debut, croit: d.croit, fin: d.finTexte ?? "" })));
    if (d.textePriere) fil.append(el("p", "dialogue-priere", t("dlgc.priere", { texte: d.textePriere })));
    for (const r of d.liste) fil.append(this.replique(d, r));
    if (d.attend) {
      fil.append(el("div", "dialogue-entite", `✦ ${d.attend}`));
      fil.append(el("p", "discret attente", t("dlgc.ecoute", { nom: d.nom })));
    }
    if (d.erreur) fil.append(el("p", "dialogue-erreur", d.erreur));
    if (!d.enCours) {
      fil.append(el("p", "dialogue-fin", t("dlgc.fin", { fin: d.finTexte ?? "" })));
      fil.append(bouton(t("dlgc.nouveau"), () => void this.preparer(d.humain, d.nom, 0)));
    }
    this.corps.replaceChildren(fil);
    this.pied.hidden = !d.enCours;
    this.dire.disabled = d.attend !== null;
    this.saisie.disabled = d.attend !== null;
    this.finir.disabled = d.attend !== null;
    this.retour.textContent = "";
    this.corps.scrollTop = this.corps.scrollHeight;
  }

  private replique(d: Dialogue, r: Replique): HTMLElement {
    const bloc = el("div", "dialogue-replique");
    bloc.append(el("div", "dialogue-entite", `✦ ${r.entite}`));
    const details = el("details", "dialogue-regles");
    const resume = el("summary", undefined, r.traduction.length > 0 ? t("dlgc.traduit", { liste: r.traduction.join(" · ") }) : t("dlgc.rienATraduire"));
    details.append(resume);
    const ul = el("ul", "liste");
    for (const x of r.regles) ul.append(el("li", undefined, x));
    for (const x of r.libre) ul.append(el("li", "libre", x));
    if (r.juge) ul.append(el("li", "discret", t("dlgc.juge", { raison: r.juge })));
    ul.append(el("li", "discret", t("dlgc.duree", { s: r.secondes })));
    details.append(ul);
    bloc.append(details);
    const humain = el("div", "dialogue-humain");
    humain.append(vignette(d.humain, 22), " ");
    if (r.mots) humain.append(el("b", undefined, `${d.nom} : `), `« ${r.mots} »`);
    else humain.append(el("span", "discret", t("dlgc.silence", { nom: d.nom })));
    bloc.append(humain);
    // Il parle sa langue : ce qu'il veut dire, traduit.
    if (r.mots && r.sens) bloc.append(el("p", "dialogue-sens", t("dlgc.sens", { texte: r.sens })));
    if (r.pensee) bloc.append(el("p", "dialogue-pensee", t("dlgc.pensee", { texte: r.pensee })));
    if (r.erreur) bloc.append(el("p", "dialogue-erreur", r.erreur));
    return bloc;
  }

  /** Les conversations passées (toutes, ou celles de l'humain affiché). */
  private async liste(): Promise<void> {
    this.racine.hidden = false;
    this.pied.hidden = true;
    this.titre.replaceChildren(t("dlgc.conversations"));
    const ul = el("ul", "liste dialogues");
    try {
      const liste = await connexion.invoke<DialogueResume[]>("ListeDialogues", 0);
      if (liste.length === 0) ul.append(el("li", "discret", t("dlgc.aucune")));
      for (const x of liste) {
        const li = el("li");
        li.append(vignette(x.humain, 22), " ", bouton(x.nom, () => {
          this.id = x.id;
          void this.relire();
        }, "lien"), el("span", "discret", ` · ${x.debut} · ${x.voie} · ${t("dlgc.repliques", { n: x.repliques })} · ${x.enCours ? t("dlgc.enCours") : x.finTexte ?? ""} · ${x.croit}`));
        ul.append(li);
      }
    } catch (e) {
      ul.append(el("li", "discret", message(e)));
    }
    this.corps.replaceChildren(ul);
  }
}

/** Appels empilés au plus. */
const APPELS_MAX = 3;

/**
 * « X t'appelle » : une prière forte d'un humain, quand un LLM peut le faire parler. Un clic sur « Répondre » ouvre le dialogue ;
 * l'appel s'efface seul au bout d'une minute.
 */
export class BandeauAppels {
  constructor(private readonly racine: HTMLElement) {
    connexion.on("Appel", (a: Appel | null) => {
      if (a) this.montrer(a);
    });
  }

  private montrer(a: Appel): void {
    const carte = el("div", "alerte-carte appel");
    const texte = el("span", "lien", t("dlgc.appel", { nom: a.nom, texte: a.texte }));
    const repondre = bouton(t("dlgc.repondre"), () => {
      ouvrirDialogue(a.humain, a.nom, a.priere);
      this.retirer(carte);
    }, "entite");
    repondre.title = t("dlgc.repondreAide");
    const fermer = bouton("×", () => this.retirer(carte), "fermer");
    fermer.title = t("commun.fermer");
    carte.append(el("span", "icone", "★"), texte, repondre, fermer);
    this.racine.prepend(carte);
    while (this.racine.querySelectorAll(".appel").length > APPELS_MAX) this.racine.querySelector(".appel:last-of-type")?.remove();
    this.racine.hidden = false;
    window.setTimeout(() => this.retirer(carte), 60_000);
  }

  private retirer(carte: HTMLElement): void {
    carte.remove();
    if (this.racine.children.length === 0) this.racine.hidden = true;
  }
}
