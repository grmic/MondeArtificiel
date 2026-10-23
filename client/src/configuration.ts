import { afficherAlertes, alertesAffichees } from "./catastrophes";
import { connexion } from "./connexion";
import { el } from "./fiche";

/** Réglages du monde courant et limites, renvoyés par le serveur. Largeur et hauteur en cases. */
interface Configuration {
  graine: number;
  couples: number;
  largeur: number;
  hauteur: number;
  continents: number;
  /** Où partent les couples : 0 sur tous les continents, n sur le n-ième par la taille (1 : le plus grand). */
  placement: number;
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
}

/** Réglages d'un nouveau monde (graine null : au hasard). */
export interface Reglages {
  graine: number | null;
  couples: number;
  largeur: number;
  hauteur: number;
  continents: number;
  placement: number;
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
  private readonly graine = champ("Graine", "même graine et mêmes réglages : même monde de départ", { min: "0", max: String(GRAINE_MAX), step: "1" });
  private readonly couples = champ("Couples de départ", "", { step: "1" });
  private readonly largeur = champ("Largeur de la carte (km)", "", { step: "1" });
  private readonly hauteur = champ("Hauteur de la carte (km)", "", { step: "1" });
  private readonly continents = champ("Continents", "séparés par la mer : on ne passe pas de l'un à l'autre à pied", { step: "1" });
  private readonly placement = (() => {
    const ligne = el("label", "reglage");
    const choix = el("select");
    ligne.append(el("span", "libelle", "Départ des couples"), choix, el("span", "aide", "sur tous les continents : en proportion de leurs terres habitables"));
    return { ligne, choix };
  })();
  private readonly actuel = el("p");
  private readonly apropos = el("p", "discret apropos");
  private readonly message = el("p", "message-config");
  private readonly hasard = el("button", undefined, "🎲");
  private apercuAffiche = false;
  private readonly listeSauvegardes = el("ul", "liste sauvegardes");
  private readonly journalQuotidien = el("input");
  private readonly messageJournal = el("p", "message-config");
  private readonly coeurs = (() => {
    const ligne = el("label", "reglage");
    const choix = el("select");
    ligne.append(el("span", "libelle", "Cœurs du processeur"), choix);
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
    const valeur = el("span", "valeur", "0 %");
    ligne.append(el("span", "libelle", "Clémence du monde"), curseur, valeur);
    return { ligne, curseur, valeur };
  })();
  private readonly messageClemence = el("p", "message-config");
  private readonly messageSauvegardes = el("p", "message-config");
  private readonly dialogueRestaurer = el("dialog", "dialogue");
  private aRestaurer: string | null = null;
  private readonly dialogueCorbeille = el("dialog", "dialogue");
  private aJeter: string | null = null;
  private readonly titreSauvegardes = el("h3", undefined, "Sauvegardes");
  private readonly titreNouveau = el("h3", undefined, "Nouveau monde");

  constructor(
    private readonly racine: HTMLElement,
    private readonly dialogue: HTMLDialogElement,
    /** Affiche sur la carte l'aperçu de ces réglages (null : revient au monde courant). Rend un message d'erreur ou null. */
    private readonly montrer: (reglages: Reglages | null) => Promise<string | null>,
    private readonly informer: (texte: string) => void,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, "Configuration"));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);

    this.hasard.type = "button";
    this.hasard.title = "Graine au hasard";
    this.hasard.addEventListener("click", () => {
      this.graine.entree.value = String(Math.floor(Math.random() * (GRAINE_MAX + 1)));
    });
    this.graine.ligne.insertBefore(this.hasard, this.graine.ligne.querySelector(".aide"));

    const formulaire = el("form", "config");
    const boutons = el("div", "boutons");
    const apercu = el("button", undefined, "Aperçu de la carte");
    apercu.type = "submit";
    const revenir = el("button", undefined, "Revenir au monde actuel");
    revenir.type = "button";
    revenir.addEventListener("click", () => void this.revenir());
    const creer = el("button", "danger", "Créer ce monde…");
    creer.type = "button";
    creer.addEventListener("click", () => this.demanderCreation());
    boutons.append(apercu, revenir, creer);
    this.continents.entree.addEventListener("input", () => this.remplirPlacement(Number(this.placement.choix.value)));
    formulaire.append(this.graine.ligne, this.couples.ligne, this.largeur.ligne, this.hauteur.ligne, this.continents.ligne, this.placement.ligne,
      boutons, this.message);
    formulaire.addEventListener("submit", (ev) => {
      ev.preventDefault();
      void this.apercu();
    });

    const sauver = el("button", undefined, "Sauvegarder maintenant");
    sauver.type = "button";
    sauver.addEventListener("click", () => void this.sauvegarder());
    const boutonsSauvegarde = el("div", "boutons");
    boutonsSauvegarde.append(sauver);
    this.construireDialogue();

    const etiquetteQuotidien = el("label", "case-config");
    etiquetteQuotidien.append(this.journalQuotidien, " Journaliser la vie quotidienne");
    this.journalQuotidien.type = "checkbox";
    this.journalQuotidien.addEventListener("change", () => void this.choisirJournalQuotidien(this.journalQuotidien.checked));
    this.coeurs.choix.addEventListener("change", () => void this.choisirCoeurs(Number(this.coeurs.choix.value)));
    this.clemence.curseur.addEventListener("input", () => (this.clemence.valeur.textContent = `${this.clemence.curseur.value} %`));
    this.clemence.curseur.addEventListener("change", () => void this.choisirClemence(Number(this.clemence.curseur.value)));

    this.corps.append(
      this.apropos,
      el("h3", undefined, "Monde actuel"),
      this.actuel,
      el("h3", undefined, "Journal"),
      etiquetteQuotidien,
      el("p", "discret", "Boire, manger, dormir, se reposer, chercher un abri, aller et venir : décoché, ces gestes ne sont plus notés (ni résumé de la journée, ni pensées qui s'y rapportent). Ce qui touche aux autres reste toujours noté : veiller sur quelqu'un, partager, soigner, consoler, se disputer… Les entrées déjà écrites sont gardées."),
      this.messageJournal,
      el("h3", undefined, "Alertes"),
      this.caseAlertes(),
      el("p", "discret", "Les catastrophes qui menacent des humains et les phénomènes du ciel s'annoncent en bas de l'écran. Décoché, elles ne s'affichent plus ; la pastille du bouton « Catastrophes » compte toujours les alertes graves, et la pause sur alerte se règle dans le panneau « Temps ». Ce réglage vaut pour ce navigateur."),
      el("h3", undefined, "Calcul"),
      this.coeurs.ligne,
      el("p", "discret", "Un cœur : les humains vivent chaque pas l'un après l'autre, comme jusqu'à la version 0.22.0. Plusieurs cœurs : ils vivent chaque pas en même temps, répartis sur les cœurs ; l'histoire n'est pas la même qu'avec un cœur, mais la même graine redonne le même monde quel que soit leur nombre. Le plus rapide dépend de la machine et du nombre d'humains : comparez la vitesse atteinte. Le changement s'applique au pas suivant."),
      this.messageCoeurs,
      el("h3", undefined, "Clémence du monde"),
      this.clemence.ligne,
      el("p", "discret", "Main de l'Entité : la part des risques de maladie (épidémies comprises), d'infection, de mort en couches, de mort des enfants de moins de 5 ans et de mort dans les catastrophes qu'elle écarte. À 0 %, le monde tel quel ; à 100 %, plus personne ne meurt de ces causes. La faim, la soif, le froid, la vieillesse, les guerres et les meurtres ne changent pas. Chaque changement est noté au journal comme un acte de l'Entité ; il s'applique au pas suivant."),
      this.messageClemence,
      this.titreSauvegardes,
      el("p", "discret", "Une sauvegarde garde tout le monde à cet instant. La restaurer remplace le monde actuel, qui est d'abord sauvegardé lui aussi."),
      boutonsSauvegarde,
      this.messageSauvegardes,
      this.listeSauvegardes,
      this.titreNouveau,
      el("p", "discret", "Ces réglages sont enregistrés dans la base du monde créé. Une case de la carte mesure 800 m : cette finesse reste fixe, la carte grandit en nombre de cases. Au-delà de 400 km de côté, la simulation ralentit."),
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
    this.actuel.textContent = "Lecture des réglages…";
    const c = await connexion.invoke<Configuration>("Configuration").catch(() => null);
    if (!c) {
      this.actuel.textContent = "Le serveur n'a pas répondu.";
      return;
    }
    this.config = c;
    this.apropos.textContent = `Monde artificiel, version ${c.version} · auteur : ${c.auteur}`;
    this.journalQuotidien.checked = c.journalQuotidien;
    this.messageJournal.textContent = "";
    this.remplirCoeurs(c);
    this.messageCoeurs.textContent = "";
    this.remplirClemence(c.clemence);
    this.messageClemence.textContent = "";
    this.actuel.textContent = `Graine ${c.graine} · ${c.couples} couples · ${c.continents} continent${c.continents > 1 ? "s" : ""}${c.origine ? " (réglages d'origine)" : ""}. ${c.resume}`;
    const km = (cases: number) => Math.round(cases * c.kilometresParCase);
    const bornes = (e: HTMLInputElement, min: number, max: number) => {
      e.min = String(min);
      e.max = String(max);
    };
    bornes(this.couples.entree, c.couplesMin, c.couplesMax);
    bornes(this.largeur.entree, Math.ceil(c.tailleMin * c.kilometresParCase), Math.floor(c.tailleMax * c.kilometresParCase));
    bornes(this.hauteur.entree, Math.ceil(c.tailleMin * c.kilometresParCase), Math.floor(c.tailleMax * c.kilometresParCase));
    bornes(this.continents.entree, c.continentsMin, c.continentsMax);
    this.couples.ligne.querySelector(".aide")!.textContent = `${c.couplesMin} à ${c.couplesMax} ; un couple par emplacement, deux humains chacun`;
    const tailles = `${this.largeur.entree.min} à ${this.largeur.entree.max} km`;
    this.largeur.ligne.querySelector(".aide")!.textContent = tailles;
    this.hauteur.ligne.querySelector(".aide")!.textContent = tailles;
    this.graine.entree.value = String(c.graine);
    this.couples.entree.value = String(c.couples);
    this.largeur.entree.value = String(km(c.largeur));
    this.hauteur.entree.value = String(km(c.hauteur));
    this.continents.entree.value = String(c.continents);
    this.remplirPlacement(c.placement);
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
    const annuler = el("button", undefined, "Annuler");
    annuler.value = "annuler";
    annuler.autofocus = true;
    const confirmer = el("button", "danger", "Restaurer");
    confirmer.value = "confirmer";
    menu.append(annuler, confirmer);
    formulaire.append(el("h2", undefined, "Restaurer cette sauvegarde ?"), texte, menu);
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
    const garder = el("button", undefined, "Garder");
    garder.value = "annuler";
    garder.autofocus = true;
    const jeter = el("button", "danger", "Mettre à la corbeille");
    jeter.value = "confirmer";
    menuCorbeille.append(garder, jeter);
    formCorbeille.append(el("h2", undefined, "Mettre cette sauvegarde à la corbeille ?"), texteCorbeille, menuCorbeille);
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
      this.messageJournal.textContent = "Le serveur n'a pas répondu : réglage inchangé.";
      return;
    }
    this.journalQuotidien.checked = c.journalQuotidien;
    this.messageJournal.textContent = c.journalQuotidien ? "La vie quotidienne est maintenant notée." : "La vie quotidienne n'est plus notée.";
  }

  private remplirCoeurs(c: Configuration): void {
    const options = [el("option", undefined, "Un cœur (l'un après l'autre)")];
    options[0].value = "1";
    for (let n = 2; n < c.coeursMachine; n++) {
      const o = el("option", undefined, `${n} cœurs en même temps`);
      o.value = String(n);
      options.push(o);
    }
    const tous = el("option", undefined, `Tous les cœurs (${c.coeursMachine}) en même temps`);
    tous.value = "0";
    options.push(tous);
    this.coeurs.choix.replaceChildren(...options);
    this.coeurs.choix.value = String(c.coeurs === c.coeursMachine && c.coeursMachine > 1 ? 0 : c.coeurs);
  }

  private async choisirCoeurs(coeurs: number): Promise<void> {
    const c = await connexion.invoke<Configuration>("ChoisirCoeurs", coeurs).catch(() => null);
    if (!c) {
      if (this.config) this.remplirCoeurs(this.config);
      this.messageCoeurs.textContent = "Le serveur n'a pas répondu : réglage inchangé.";
      return;
    }
    this.config = c;
    this.remplirCoeurs(c);
    this.messageCoeurs.textContent = c.coeurs === 1 ? "Le calcul se fait maintenant sur un cœur." : "Le calcul se fait maintenant sur plusieurs cœurs.";
  }

  private caseAlertes(): HTMLLabelElement {
    const etiquette = el("label", "case-config");
    const c = el("input");
    c.type = "checkbox";
    c.id = "configAlertes";
    c.checked = alertesAffichees();
    c.addEventListener("change", () => afficherAlertes(c.checked));
    etiquette.append(c, " Afficher les alertes de catastrophe");
    return etiquette;
  }

  private remplirClemence(clemence: number): void {
    this.clemence.curseur.value = String(clemence);
    this.clemence.valeur.textContent = `${clemence} %`;
  }

  private async choisirClemence(clemence: number): Promise<void> {
    const c = await connexion.invoke<Configuration>("ChoisirClemence", clemence).catch(() => null);
    if (!c) {
      if (this.config) this.remplirClemence(this.config.clemence);
      this.messageClemence.textContent = "Le serveur n'a pas répondu : réglage inchangé.";
      return;
    }
    this.config = c;
    this.remplirClemence(c.clemence);
    this.messageClemence.textContent = c.clemence === 0 ? "Le monde reprend toute sa dureté." : `L'Entité écarte maintenant ${c.clemence} % de ces risques.`;
  }

  private async lireSauvegardes(): Promise<void> {
    const liste = await connexion.invoke<Sauvegarde[]>("Sauvegardes").catch(() => null);
    this.afficherSauvegardes(liste);
  }

  private afficherSauvegardes(liste: Sauvegarde[] | null): void {
    if (!liste) {
      this.listeSauvegardes.replaceChildren(el("li", "discret", "Le serveur n'a pas répondu."));
      return;
    }
    if (liste.length === 0) {
      this.listeSauvegardes.replaceChildren(el("li", "discret", "Aucune sauvegarde pour l'instant."));
      return;
    }
    this.listeSauvegardes.replaceChildren(...liste.map((s) => {
      const li = el("li");
      li.append(el("span", "date", s.faiteLe), ` ${s.dateMonde} · graine ${s.graine} · ${s.vivants} vivants · ${s.mo} Mo `);
      if (s.possible) {
        const b = el("button", "lien", "restaurer");
        b.type = "button";
        b.title = s.nom;
        b.addEventListener("click", () => {
          this.aRestaurer = s.nom;
          const texte = this.dialogueRestaurer.querySelector("#texteRestaurer");
          if (texte) {
            texte.textContent = `Le monde reprend tel qu'il était (${s.dateMonde}, ${s.vivants} humains vivants), en pause. `
              + "Le monde actuel est d'abord sauvegardé : vous pourrez y revenir depuis cette liste.";
          }
          this.dialogueRestaurer.returnValue = "";
          this.dialogueRestaurer.showModal();
        });
        li.append(b);
      } else {
        li.append(el("span", "discret", "(version plus récente ou illisible)"));
      }
      const j = el("button", "lien", "🗑");
      j.type = "button";
      j.title = "Mettre cette sauvegarde à la corbeille (on peut l'y reprendre tant qu'elle n'est pas vidée)";
      j.setAttribute("aria-label", "Mettre à la corbeille");
      j.addEventListener("click", () => {
        this.aJeter = s.nom;
        const texte = this.dialogueCorbeille.querySelector("#texteCorbeille");
        if (texte) {
          texte.textContent = `« ${s.nom} », faite le ${s.faiteLe} (${s.dateMonde}, ${s.vivants} humains vivants, ${s.mo} Mo). `
            + "Elle part dans la corbeille de l'ordinateur : vous pourrez l'y reprendre tant qu'elle n'est pas vidée. Le monde en cours ne change pas.";
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
    this.messageSauvegardes.textContent = "Sauvegarde…";
    const liste = await connexion.invoke<Sauvegarde[]>("Sauvegarder").catch(() => null);
    this.messageSauvegardes.textContent = liste ? "Monde sauvegardé." : "La sauvegarde a échoué.";
    this.afficherSauvegardes(liste);
  }

  private async mettreALaCorbeille(nom: string): Promise<void> {
    this.messageSauvegardes.textContent = "Mise à la corbeille…";
    try {
      const liste = await connexion.invoke<Sauvegarde[]>("MettreALaCorbeille", nom);
      this.messageSauvegardes.textContent = `« ${nom} » est dans la corbeille.`;
      this.afficherSauvegardes(liste);
    } catch (e) {
      const texte = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : "";
      this.messageSauvegardes.textContent = `Impossible de la mettre à la corbeille${texte ? " : " + texte : "."} Elle est toujours là.`;
      await this.lireSauvegardes();
    }
  }

  private async restaurer(nom: string): Promise<void> {
    this.messageSauvegardes.textContent = "Restauration…";
    this.informer("Restauration d'une sauvegarde…");
    try {
      await connexion.invoke("Restaurer", nom);
      this.messageSauvegardes.textContent = "Sauvegarde restaurée.";
    } catch (e) {
      const raison = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : "";
      this.messageSauvegardes.textContent = `Impossible : ${raison || "sauvegarde illisible"}`;
      this.informer("Le monde n'a pas changé.");
    }
    await this.lireSauvegardes();
  }

  /** Choix du continent de départ, selon le nombre de continents saisi ; garde le choix s'il reste possible. */
  private remplirPlacement(voulu: number): void {
    const n = Number(this.continents.entree.value);
    const nombre = Number.isInteger(n) && n >= 1 && n <= 6 ? n : 1;
    const options: [number, string][] = [[1, nombre === 1 ? "sur le continent" : "sur le plus grand continent"]];
    for (let k = 2; k <= nombre; k++) options.push([k, `sur le ${k}e continent par la taille`]);
    if (nombre > 1) options.push([0, "répartis sur tous les continents"]);
    this.placement.choix.replaceChildren(...options.map(([v, t]) => {
      const o = el("option", undefined, t);
      o.value = String(v);
      return o;
    }));
    this.placement.choix.value = String(options.some(([v]) => v === voulu) ? voulu : 1);
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
        this.message.textContent = `${nom} : un entier entre ${e.min} et ${e.max}.`;
        return null;
      }
      return v;
    };
    const graine = entier(this.graine.entree, "Graine");
    const couples = entier(this.couples.entree, "Couples");
    const largeur = entier(this.largeur.entree, "Largeur");
    const hauteur = entier(this.hauteur.entree, "Hauteur");
    const continents = entier(this.continents.entree, "Continents");
    if (graine === null || couples === null || largeur === null || hauteur === null || continents === null) return null;
    const cases = (km: number) => Math.min(c.tailleMax, Math.max(c.tailleMin, Math.round(km / c.kilometresParCase)));
    const placement = Number(this.placement.choix.value);
    return { graine, couples, largeur: cases(largeur), hauteur: cases(hauteur), continents, placement: placement <= continents ? placement : 1 };
  }

  private async apercu(): Promise<boolean> {
    const r = this.lire();
    if (!r) return false;
    this.message.textContent = "Génération de la carte…";
    const a = await connexion.invoke<Apercu>("ApercuConfiguration", r).catch(() => null);
    if (!a) {
      this.message.textContent = "Le serveur n'a pas répondu.";
      return false;
    }
    if (!a.possible) {
      this.message.textContent = `Impossible : ${a.texte}`;
      return false;
    }
    const erreur = await this.montrer(r);
    this.apercuAffiche = erreur === null;
    this.message.textContent = erreur ?? `Aperçu : ${a.texte}`;
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
      texte.textContent = `Le monde recommence à l'an 1 avec ${r.couples} couple${r.couples > 1 ? "s" : ""} (graine ${r.graine}, ` +
        `carte de ${Math.round(r.largeur * (this.config?.kilometresParCase ?? 0.8))} × ${Math.round(r.hauteur * (this.config?.kilometresParCase ?? 0.8))} km, ` +
        `${r.continents} continent${r.continents > 1 ? "s" : ""}, couples ${this.placement.choix.selectedOptions[0]?.textContent ?? ""}). Toute son histoire est effacée ; une copie de l'ancien monde est gardée dans monde-avant-reinitialisation.db (seule la dernière).`;
    }
    this.dialogue.returnValue = "";
    this.dialogue.showModal();
  }

  private async creer(): Promise<void> {
    const r = this.lire();
    if (!r) return;
    this.message.textContent = "Création du monde…";
    this.informer("Création du nouveau monde…");
    try {
      await connexion.invoke("CreerMonde", r);
      this.message.textContent = "Monde créé.";
    } catch (e) {
      // Le serveur renvoie la raison (réglages impossibles) ; rien n'a changé.
      const raison = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : "";
      this.message.textContent = `Le serveur a refusé : ${raison || "réglages impossibles"}`;
      this.informer("Le monde n'a pas changé.");
    }
  }
}
