import { afficherAlertes, alertesAffichees } from "./catastrophes";
import { connexion } from "./connexion";
import { RubriqueJournalBase } from "./journalbase";
import { el } from "./fiche";
import { Guide } from "./guide";
import { choisirLangue, langue, languesDisponibles, t } from "./langue";

/** Réglages du monde courant et limites, renvoyés par le serveur. Largeur et hauteur en cases. */
interface Configuration {
  graine: number;
  couples: number;
  largeur: number;
  hauteur: number;
  continents: number;
  /** Où partent les couples : 0 sur tous les continents, n sur le n-ième par la taille (1 : le plus grand). */
  placement: number;
  /** Mer entre les continents : 0 proches, 1 éloignés (plus de 200 km), 2 mélangés. */
  ecart: number;
  /** Génération de la carte : 0 avant la 0.31.3, 1 depuis (feuillus, mangrove, lande). */
  generation: number;
  origine: boolean;
  kilometresParCase: number;
  couplesMin: number;
  couplesMax: number;
  tailleMin: number;
  tailleMax: number;
  continentsMin: number;
  continentsMax: number;
  resume: string;
  version: string;
  auteur: string;
  journalQuotidien: boolean;
  /** Cœurs donnés au calcul : 1 l'un après l'autre (classique), 0 tous, n ce nombre en même temps. */
  coeurs: number;
  coeursMachine: number;
  /** Clémence du monde : part (0 à 100 %) des risques de maladie, de couches, des tout-petits et des catastrophes écartée. */
  clemence: number;
  /** Esprits fertiles : les idées par combinaison viennent ce nombre de fois plus souvent (1 : le monde tel quel). */
  espritsFertiles: number;
}

/** Réglages d'un nouveau monde (graine null : au hasard). */
export interface Reglages {
  graine: number | null;
  couples: number;
  largeur: number;
  hauteur: number;
  continents: number;
  placement: number;
  ecart: number;
}

/** Une sauvegarde restaurable (possible : faux si elle vient d'une version plus récente ou est illisible). */
interface Sauvegarde {
  nom: string;
  dateMonde: string;
  faiteLe: string;
  mo: number;
  vivants: number;
  graine: number;
  possible: boolean;
}

interface Apercu {
  possible: boolean;
  texte: string;
}

const GRAINE_MAX = 4_294_967_295;

function champ(libelle: string, aide: string, attributs: Record<string, string>): { ligne: HTMLElement; entree: HTMLInputElement } {
  const ligne = el("label", "reglage");
  const entree = el("input");
  entree.type = "number";
  entree.required = true;
  for (const [k, v] of Object.entries(attributs)) entree.setAttribute(k, v);
  ligne.append(el("span", "libelle", libelle), entree, el("span", "aide", aide));
  return { ligne, entree };
}

/**
 * Fenêtre « Configuration » : les réglages du monde courant (gardés dans sa base) et ceux d'un nouveau monde,
 * avec un aperçu de sa carte avant de le créer.
 */
export class PanneauConfiguration {
  private readonly corps = el("div", "corps");
  private config: Configuration | null = null;
  private readonly graine = champ(t("config.graine"), t("config.graineAide"), { min: "0", max: String(GRAINE_MAX), step: "1" });
  private readonly couples = champ(t("config.couples"), "", { step: "1" });
  private readonly largeur = champ(t("config.largeur"), "", { step: "1" });
  private readonly hauteur = champ(t("config.hauteur"), "", { step: "1" });
  private readonly continents = champ(t("config.continents"), t("config.continentsAide"), { step: "1" });
  private readonly placement = (() => {
    const ligne = el("label", "reglage");
    const choix = el("select");
    ligne.append(el("span", "libelle", t("config.depart")), choix, el("span", "aide", t("config.departAide")));
    return { ligne, choix };
  })();
  private readonly ecart = (() => {
    const ligne = el("label", "reglage");
    const choix = el("select");
    for (const v of [0, 1, 2]) {
      const o = el("option", undefined, t(`config.ecart.${v}`));
      o.value = String(v);
      choix.append(o);
    }
    ligne.append(el("span", "libelle", t("config.ecart")), choix, el("span", "aide", t("config.ecartAide")));
    return { ligne, choix };
  })();
  private readonly actuel = el("p");
  private readonly apropos = el("p", "discret apropos");
  private guide: Guide | null = null;
  private readonly message = el("p", "message-config");
  private readonly hasard = el("button", undefined, "🎲");
  private apercuAffiche = false;
  private readonly listeSauvegardes = el("ul", "liste sauvegardes");
  private readonly journalQuotidien = el("input");
  private readonly messageJournal = el("p", "message-config");
  private readonly journalBase = new RubriqueJournalBase();
  private readonly coeurs = (() => {
    const ligne = el("label", "reglage");
    const choix = el("select");
    ligne.append(el("span", "libelle", t("config.coeurs")), choix);
    return { ligne, choix };
  })();
  private readonly messageCoeurs = el("p", "message-config");
  private readonly clemence = (() => {
    const ligne = el("label", "reglage");
    const curseur = el("input");
    curseur.type = "range";
    curseur.min = "0";
    curseur.max = "100";
    curseur.step = "5";
    const valeur = el("span", "valeur", t("commun.pourcent", { v: 0 }));
    ligne.append(el("span", "libelle", t("config.clemence")), curseur, valeur);
    return { ligne, curseur, valeur };
  })();
  private readonly messageClemence = el("p", "message-config");
  /** Esprits fertiles : seulement pour les mondes qui ont la matière (génération 2 et suivantes). */
  private readonly esprits = (() => {
    const ligne = el("label", "reglage");
    const choix = el("select");
    for (const n of [1, 3, 10, 30]) {
      const o = el("option", undefined, n === 1 ? t("config.esprits.1") : t("config.esprits.n", { n }));
      o.value = String(n);
      choix.append(o);
    }
    ligne.append(el("span", "libelle", t("config.esprits")), choix);
    return { ligne, choix };
  })();
  private readonly messageEsprits = el("p", "message-config");
  private readonly blocEsprits = el("div");
  private readonly messageSauvegardes = el("p", "message-config");
  private readonly dialogueRestaurer = el("dialog", "dialogue");
  private aRestaurer: string | null = null;
  private readonly dialogueCorbeille = el("dialog", "dialogue");
  private aJeter: string | null = null;
  private readonly titreSauvegardes = el("h3", undefined, t("config.sauvegardes"));
  private readonly titreNouveau = el("h3", undefined, t("config.nouveauMonde"));

  constructor(
    private readonly racine: HTMLElement,
    private readonly dialogue: HTMLDialogElement,
    /** Affiche sur la carte l'aperçu de ces réglages (null : revient au monde courant). Rend un message d'erreur ou null. */
    private readonly montrer: (reglages: Reglages | null) => Promise<string | null>,
    private readonly informer: (texte: string) => void,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, t("config.titre")));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);

    this.hasard.type = "button";
    this.hasard.title = t("config.graineHasard");
    this.hasard.addEventListener("click", () => {
      this.graine.entree.value = String(Math.floor(Math.random() * (GRAINE_MAX + 1)));
    });
    this.graine.ligne.insertBefore(this.hasard, this.graine.ligne.querySelector(".aide"));

    const formulaire = el("form", "config");
    const boutons = el("div", "boutons");
    const apercu = el("button", undefined, t("config.apercu"));
    apercu.type = "submit";
    const revenir = el("button", undefined, t("config.revenir"));
    revenir.type = "button";
    revenir.addEventListener("click", () => void this.revenir());
    const creer = el("button", "danger", t("config.creer"));
    creer.type = "button";
    creer.addEventListener("click", () => this.demanderCreation());
    boutons.append(apercu, revenir, creer);
    this.continents.entree.addEventListener("input", () => this.remplirPlacement(Number(this.placement.choix.value)));
    formulaire.append(this.graine.ligne, this.couples.ligne, this.largeur.ligne, this.hauteur.ligne, this.continents.ligne, this.placement.ligne, this.ecart.ligne,
      boutons, this.message);
    formulaire.addEventListener("submit", (ev) => {
      ev.preventDefault();
      void this.apercu();
    });

    const sauver = el("button", undefined, t("config.sauvegarder"));
    sauver.type = "button";
    sauver.addEventListener("click", () => void this.sauvegarder());
    const boutonsSauvegarde = el("div", "boutons");
    boutonsSauvegarde.append(sauver);
    this.construireDialogue();

    const etiquetteQuotidien = el("label", "case-config");
    etiquetteQuotidien.append(this.journalQuotidien, ` ${t("config.quotidien")}`);
    this.journalQuotidien.type = "checkbox";
    this.journalQuotidien.addEventListener("change", () => void this.choisirJournalQuotidien(this.journalQuotidien.checked));
    this.coeurs.choix.addEventListener("change", () => void this.choisirCoeurs(Number(this.coeurs.choix.value)));
    this.clemence.curseur.addEventListener("input", () => (this.clemence.valeur.textContent = t("commun.pourcent", { v: Number(this.clemence.curseur.value) })));
    this.clemence.curseur.addEventListener("change", () => void this.choisirClemence(Number(this.clemence.curseur.value)));
    this.esprits.choix.addEventListener("change", () => void this.choisirEsprits(Number(this.esprits.choix.value)));
    this.blocEsprits.append(el("h3", undefined, t("config.esprits")), this.esprits.ligne, el("p", "discret", t("config.espritsAide")), this.messageEsprits);

    const lireGuide = el("button", undefined, t("guide.bouton"));
    lireGuide.type = "button";
    lireGuide.title = t("guide.aide");
    lireGuide.addEventListener("click", () => void (this.guide ??= new Guide()).ouvrir());
    const boutonsGuide = el("div", "boutons");
    boutonsGuide.append(lireGuide);

    this.corps.append(
      this.apropos,
      boutonsGuide,
      el("h3", undefined, t("config.langue")),
      this.choixLangue(),
      el("p", "discret", t("config.langueAide")),
      el("h3", undefined, t("config.mondeActuel")),
      this.actuel,
      el("h3", undefined, t("barre.journal")),
      etiquetteQuotidien,
      el("p", "discret", t("config.quotidienAide")),
      this.messageJournal,
      el("h3", undefined, t("journalBase.titre")),
      this.journalBase.element,
      el("h3", undefined, t("config.alertes")),
      this.caseAlertes(),
      el("p", "discret", t("config.alertesAide")),
      el("h3", undefined, t("config.calcul")),
      this.coeurs.ligne,
      el("p", "discret", t("config.calculAide")),
      this.messageCoeurs,
      el("h3", undefined, t("config.clemence")),
      this.clemence.ligne,
      el("p", "discret", t("config.clemenceAide")),
      this.messageClemence,
      this.blocEsprits,
      this.titreSauvegardes,
      el("p", "discret", t("config.sauvegardesAide")),
      boutonsSauvegarde,
      this.messageSauvegardes,
      this.listeSauvegardes,
      this.titreNouveau,
      el("p", "discret", t("config.nouveauAide")),
      formulaire,
    );
    racine.append(tete, this.corps);

    dialogue.addEventListener("close", () => {
      if (dialogue.returnValue === "confirmer") void this.creer();
    });
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else void this.ouvrir();
  }

  /** Ouvre la fenêtre, et la fait défiler jusqu'aux sauvegardes ou au nouveau monde si demandé. */
  async ouvrir(section?: "sauvegardes" | "nouveau"): Promise<void> {
    this.racine.hidden = false;
    this.actuel.textContent = t("config.lecture");
    const c = await connexion.invoke<Configuration>("Configuration").catch(() => null);
    if (!c) {
      this.actuel.textContent = t("commun.pasDeReponse");
      return;
    }
    this.config = c;
    this.apropos.textContent = t("config.apropos", { version: c.version, auteur: c.auteur });
    this.journalQuotidien.checked = c.journalQuotidien;
    this.messageJournal.textContent = "";
    void this.journalBase.remplir();
    this.remplirCoeurs(c);
    this.messageCoeurs.textContent = "";
    this.remplirClemence(c.clemence);
    this.messageClemence.textContent = "";
    this.esprits.choix.value = String(c.espritsFertiles);
    this.messageEsprits.textContent = "";
    this.blocEsprits.hidden = c.generation < 2;
    this.actuel.textContent = `${t("config.graine")} ${c.graine} · ${t("config.nCouples", { n: c.couples })} · ${t("config.nContinents", { n: c.continents })}${c.origine ? ` ${t("config.origine")}` : ""}. ${c.resume}`;
    const km = (cases: number) => Math.round(cases * c.kilometresParCase);
    const bornes = (e: HTMLInputElement, min: number, max: number) => {
      e.min = String(min);
      e.max = String(max);
    };
    bornes(this.couples.entree, c.couplesMin, c.couplesMax);
    bornes(this.largeur.entree, Math.ceil(c.tailleMin * c.kilometresParCase), Math.floor(c.tailleMax * c.kilometresParCase));
    bornes(this.hauteur.entree, Math.ceil(c.tailleMin * c.kilometresParCase), Math.floor(c.tailleMax * c.kilometresParCase));
    bornes(this.continents.entree, c.continentsMin, c.continentsMax);
    this.couples.ligne.querySelector(".aide")!.textContent = t("config.couplesAide", { min: c.couplesMin, max: c.couplesMax });
    const tailles = t("config.taillesAide", { min: this.largeur.entree.min, max: this.largeur.entree.max });
    this.largeur.ligne.querySelector(".aide")!.textContent = tailles;
    this.hauteur.ligne.querySelector(".aide")!.textContent = tailles;
    this.graine.entree.value = String(c.graine);
    this.couples.entree.value = String(c.couples);
    this.largeur.entree.value = String(km(c.largeur));
    this.hauteur.entree.value = String(km(c.hauteur));
    this.continents.entree.value = String(c.continents);
    this.remplirPlacement(c.placement);
    this.ecart.choix.value = String(c.ecart);
    this.message.textContent = "";
    await this.lireSauvegardes();
    if (section) (section === "sauvegardes" ? this.titreSauvegardes : this.titreNouveau).scrollIntoView({ block: "start" });
  }

  private construireDialogue(): void {
    const formulaire = el("form");
    formulaire.method = "dialog";
    const texte = el("p");
    texte.id = "texteRestaurer";
    const menu = el("menu");
    const annuler = el("button", undefined, t("commun.annuler"));
    annuler.value = "annuler";
    annuler.autofocus = true;
    const confirmer = el("button", "danger", t("config.restaurer"));
    confirmer.value = "confirmer";
    menu.append(annuler, confirmer);
    formulaire.append(el("h2", undefined, t("config.restaurerQuestion")), texte, menu);
    this.dialogueRestaurer.append(formulaire);
    document.body.append(this.dialogueRestaurer);
    this.dialogueRestaurer.addEventListener("close", () => {
      if (this.dialogueRestaurer.returnValue === "confirmer" && this.aRestaurer) void this.restaurer(this.aRestaurer);
    });

    const formCorbeille = el("form");
    formCorbeille.method = "dialog";
    const texteCorbeille = el("p");
    texteCorbeille.id = "texteCorbeille";
    const menuCorbeille = el("menu");
    const garder = el("button", undefined, t("config.garder"));
    garder.value = "annuler";
    garder.autofocus = true;
    const jeter = el("button", "danger", t("config.corbeille"));
    jeter.value = "confirmer";
    menuCorbeille.append(garder, jeter);
    formCorbeille.append(el("h2", undefined, t("config.corbeilleQuestion")), texteCorbeille, menuCorbeille);
    this.dialogueCorbeille.append(formCorbeille);
    document.body.append(this.dialogueCorbeille);
    this.dialogueCorbeille.addEventListener("close", () => {
      if (this.dialogueCorbeille.returnValue === "confirmer" && this.aJeter) void this.mettreALaCorbeille(this.aJeter);
    });
  }

  private async choisirJournalQuotidien(oui: boolean): Promise<void> {
    const c = await connexion.invoke<Configuration>("ChoisirJournalQuotidien", oui).catch(() => null);
    if (!c) {
      this.journalQuotidien.checked = !oui;
      this.messageJournal.textContent = t("config.reglageInchange");
      return;
    }
    this.journalQuotidien.checked = c.journalQuotidien;
    this.messageJournal.textContent = c.journalQuotidien ? t("config.quotidienOui") : t("config.quotidienNon");
  }

  private remplirCoeurs(c: Configuration): void {
    const options = [el("option", undefined, t("config.unCoeur"))];
    options[0].value = "1";
    for (let n = 2; n < c.coeursMachine; n++) {
      const o = el("option", undefined, t("config.nCoeurs", { n }));
      o.value = String(n);
      options.push(o);
    }
    const tous = el("option", undefined, t("config.tousCoeurs", { n: c.coeursMachine }));
    tous.value = "0";
    options.push(tous);
    this.coeurs.choix.replaceChildren(...options);
    this.coeurs.choix.value = String(c.coeurs === c.coeursMachine && c.coeursMachine > 1 ? 0 : c.coeurs);
  }

  private async choisirCoeurs(coeurs: number): Promise<void> {
    const c = await connexion.invoke<Configuration>("ChoisirCoeurs", coeurs).catch(() => null);
    if (!c) {
      if (this.config) this.remplirCoeurs(this.config);
      this.messageCoeurs.textContent = t("config.reglageInchange");
      return;
    }
    this.config = c;
    this.remplirCoeurs(c);
    this.messageCoeurs.textContent = c.coeurs === 1 ? t("config.calculUn") : t("config.calculPlusieurs");
  }

  /** Choix de la langue : les langues présentes sur le serveur, chacune dans sa propre langue ; la page se recharge. */
  private choixLangue(): HTMLLabelElement {
    const ligne = el("label", "reglage");
    const choix = el("select");
    choix.id = "configLangue";
    const langues = languesDisponibles().length > 0 ? languesDisponibles() : [{ code: langue(), nom: langue(), part: 1 }];
    for (const l of langues) {
      const o = el("option", undefined, l.part < 0.995 ? t("config.langueIncomplete", { nom: l.nom, part: Math.floor(l.part * 100) }) : l.nom);
      o.value = l.code;
      choix.append(o);
    }
    choix.value = langue();
    choix.addEventListener("change", () => {
      if (choix.value !== langue()) choisirLangue(choix.value);
    });
    ligne.append(el("span", "libelle", t("config.langueLibelle")), choix);
    return ligne;
  }

  private caseAlertes(): HTMLLabelElement {
    const etiquette = el("label", "case-config");
    const c = el("input");
    c.type = "checkbox";
    c.id = "configAlertes";
    c.checked = alertesAffichees();
    c.addEventListener("change", () => afficherAlertes(c.checked));
    etiquette.append(c, ` ${t("config.afficherAlertes")}`);
    return etiquette;
  }

  private remplirClemence(clemence: number): void {
    this.clemence.curseur.value = String(clemence);
    this.clemence.valeur.textContent = t("commun.pourcent", { v: clemence });
  }

  private async choisirClemence(clemence: number): Promise<void> {
    const c = await connexion.invoke<Configuration>("ChoisirClemence", clemence).catch(() => null);
    if (!c) {
      if (this.config) this.remplirClemence(this.config.clemence);
      this.messageClemence.textContent = t("config.reglageInchange");
      return;
    }
    this.config = c;
    this.remplirClemence(c.clemence);
    this.messageClemence.textContent = c.clemence === 0 ? t("config.clemenceZero") : t("config.clemenceEcarte", { clemence: c.clemence });
  }

  private async choisirEsprits(facteur: number): Promise<void> {
    const c = await connexion.invoke<Configuration>("ChoisirEspritsFertiles", facteur).catch(() => null);
    if (!c) {
      if (this.config) this.esprits.choix.value = String(this.config.espritsFertiles);
      this.messageEsprits.textContent = t("config.reglageInchange");
      return;
    }
    this.config = c;
    this.esprits.choix.value = String(c.espritsFertiles);
  }

  private async lireSauvegardes(): Promise<void> {
    const liste = await connexion.invoke<Sauvegarde[]>("Sauvegardes").catch(() => null);
    this.afficherSauvegardes(liste);
  }

  private afficherSauvegardes(liste: Sauvegarde[] | null): void {
    if (!liste) {
      this.listeSauvegardes.replaceChildren(el("li", "discret", t("commun.pasDeReponse")));
      return;
    }
    if (liste.length === 0) {
      this.listeSauvegardes.replaceChildren(el("li", "discret", t("config.aucuneSauvegarde")));
      return;
    }
    this.listeSauvegardes.replaceChildren(...liste.map((s) => {
      const li = el("li");
      li.append(el("span", "date", s.faiteLe), ` ${t("config.sauvegarde", { date: s.dateMonde, graine: String(s.graine), vivants: s.vivants, mo: s.mo })} `);
      if (s.possible) {
        const b = el("button", "lien", t("config.restaurerMin"));
        b.type = "button";
        b.title = s.nom;
        b.addEventListener("click", () => {
          this.aRestaurer = s.nom;
          const texte = this.dialogueRestaurer.querySelector("#texteRestaurer");
          if (texte) {
            texte.textContent = t("config.restaurerTexte", { date: s.dateMonde, vivants: s.vivants });
          }
          this.dialogueRestaurer.returnValue = "";
          this.dialogueRestaurer.showModal();
        });
        li.append(b);
      } else {
        li.append(el("span", "discret", t("config.illisible")));
      }
      const j = el("button", "lien", "🗑");
      j.type = "button";
      j.title = t("config.corbeilleAide");
      j.setAttribute("aria-label", t("config.corbeille"));
      j.addEventListener("click", () => {
        this.aJeter = s.nom;
        const texte = this.dialogueCorbeille.querySelector("#texteCorbeille");
        if (texte) {
          texte.textContent = t("config.corbeilleTexte", { nom: s.nom, faite: s.faiteLe, date: s.dateMonde, vivants: s.vivants, mo: s.mo });
        }
        this.dialogueCorbeille.returnValue = "";
        this.dialogueCorbeille.showModal();
      });
      li.append(" ", j);
      li.title = s.nom;
      return li;
    }));
  }

  private async sauvegarder(): Promise<void> {
    this.messageSauvegardes.textContent = t("config.sauvegardeEnCours");
    const liste = await connexion.invoke<Sauvegarde[]>("Sauvegarder").catch(() => null);
    this.messageSauvegardes.textContent = liste ? t("config.sauvegarde.ok") : t("config.sauvegarde.echec");
    this.afficherSauvegardes(liste);
  }

  private async mettreALaCorbeille(nom: string): Promise<void> {
    this.messageSauvegardes.textContent = t("config.corbeilleEnCours");
    try {
      const liste = await connexion.invoke<Sauvegarde[]>("MettreALaCorbeille", nom);
      this.messageSauvegardes.textContent = t("config.corbeilleOk", { nom });
      this.afficherSauvegardes(liste);
    } catch (e) {
      const texte = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : "";
      this.messageSauvegardes.textContent = texte ? t("config.corbeilleEchecRaison", { raison: texte }) : t("config.corbeilleEchec");
      await this.lireSauvegardes();
    }
  }

  private async restaurer(nom: string): Promise<void> {
    this.messageSauvegardes.textContent = t("config.restauration");
    this.informer(t("config.restaurationInfo"));
    try {
      await connexion.invoke("Restaurer", nom);
      this.messageSauvegardes.textContent = t("config.restauree");
    } catch (e) {
      const raison = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : "";
      this.messageSauvegardes.textContent = t("commun.impossible", { raison: raison || t("config.sauvegardeIllisible") });
      this.informer(t("config.mondeInchange"));
    }
    await this.lireSauvegardes();
  }

  /** Choix du continent de départ, selon le nombre de continents saisi ; garde le choix s'il reste possible. */
  private remplirPlacement(voulu: number): void {
    const n = Number(this.continents.entree.value);
    const nombre = Number.isInteger(n) && n >= 1 && n <= 6 ? n : 1;
    const options: [number, string][] = [[1, nombre === 1 ? t("config.placement.seul") : t("config.placement.plusGrand")]];
    for (let k = 2; k <= nombre; k++) options.push([k, t("config.placement.rang", { rang: k })]);
    if (nombre > 1) options.push([0, t("config.placement.tous")]);
    this.placement.choix.replaceChildren(...options.map(([v, texte]) => {
      const o = el("option", undefined, texte);
      o.value = String(v);
      return o;
    }));
    this.placement.choix.value = String(options.some(([v]) => v === voulu) ? voulu : 1);
    // Un seul continent : pas de mer entre eux.
    this.ecart.choix.disabled = nombre === 1;
  }

  fermer(): void {
    this.racine.hidden = true;
    if (this.apercuAffiche) void this.revenir();
  }

  /** Le monde a été recréé (ici ou dans un autre navigateur) : la carte affichée est celle du monde courant. */
  mondeRecree(): void {
    this.apercuAffiche = false;
    if (this.ouvert) void this.ouvrir();
  }

  /** Réglages saisis, vérifiés ; null (avec un message) s'ils sont invalides. */
  private lire(): Reglages | null {
    const c = this.config;
    if (!c) return null;
    const entier = (e: HTMLInputElement, nom: string): number | null => {
      const v = Number(e.value);
      if (e.value.trim() === "" || !Number.isInteger(v) || v < Number(e.min) || v > Number(e.max)) {
        this.message.textContent = t("config.entier", { nom, min: e.min, max: e.max });
        return null;
      }
      return v;
    };
    const graine = entier(this.graine.entree, t("config.graine"));
    const couples = entier(this.couples.entree, t("config.champCouples"));
    const largeur = entier(this.largeur.entree, t("config.champLargeur"));
    const hauteur = entier(this.hauteur.entree, t("config.champHauteur"));
    const continents = entier(this.continents.entree, t("config.continents"));
    if (graine === null || couples === null || largeur === null || hauteur === null || continents === null) return null;
    const cases = (km: number) => Math.min(c.tailleMax, Math.max(c.tailleMin, Math.round(km / c.kilometresParCase)));
    const placement = Number(this.placement.choix.value);
    return { graine, couples, largeur: cases(largeur), hauteur: cases(hauteur), continents, placement: placement <= continents ? placement : 1,
      ecart: Number(this.ecart.choix.value) };
  }

  private async apercu(): Promise<boolean> {
    const r = this.lire();
    if (!r) return false;
    this.message.textContent = t("carte.generation");
    const a = await connexion.invoke<Apercu>("ApercuConfiguration", r).catch(() => null);
    if (!a) {
      this.message.textContent = t("commun.pasDeReponse");
      return false;
    }
    if (!a.possible) {
      this.message.textContent = t("commun.impossible", { raison: a.texte });
      return false;
    }
    const erreur = await this.montrer(r);
    this.apercuAffiche = erreur === null;
    this.message.textContent = erreur ?? t("config.apercuTexte", { texte: a.texte });
    return erreur === null;
  }

  private async revenir(): Promise<void> {
    this.apercuAffiche = false;
    await this.montrer(null);
  }

  private demanderCreation(): void {
    const r = this.lire();
    if (!r) return;
    const texte = this.dialogue.querySelector("#texteNouveauMonde");
    if (texte) {
      const km = this.config?.kilometresParCase ?? 0.8;
      texte.textContent = t("config.recommence", { couples: t("config.nCouples", { n: r.couples }), graine: String(r.graine),
        largeur: Math.round(r.largeur * km), hauteur: Math.round(r.hauteur * km), continents: t("config.nContinents", { n: r.continents }),
        placement: this.placement.choix.selectedOptions[0]?.textContent ?? "" });
    }
    // La copie de l'ancien monde est proposée à chaque fois (cochée par défaut).
    const garder = this.dialogue.querySelector<HTMLInputElement>("#garderAncienMonde");
    if (garder) garder.checked = true;
    this.dialogue.returnValue = "";
    this.dialogue.showModal();
  }

  private async creer(): Promise<void> {
    const r = this.lire();
    if (!r) return;
    const garder = this.dialogue.querySelector<HTMLInputElement>("#garderAncienMonde")?.checked ?? true;
    this.message.textContent = t("config.creation");
    this.informer(t("config.creationInfo"));
    try {
      await connexion.invoke("CreerMonde", { ...r, garderAncien: garder });
      this.message.textContent = t("config.cree");
    } catch (e) {
      // Le serveur renvoie la raison (réglages impossibles) ; rien n'a changé.
      const raison = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : "";
      this.message.textContent = t("config.refuse", { raison: raison || t("config.reglagesImpossibles") });
      this.informer(t("config.mondeInchange"));
    }
  }
}
