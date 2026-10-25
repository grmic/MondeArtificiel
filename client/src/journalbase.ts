import { connexion } from "./connexion";
import { el } from "./fiche";
import { t } from "./langue";

/** Réglages et mesures de la rubrique « Journal et taille de la base » (voir JournalBaseDto côté serveur). */
interface JournalBase {
  conserverAns: number;
  quotidienAns: number;
  effacerMarquants: boolean;
  effacerSuivis: boolean;
  compresser: boolean;
  compresserAns: number;
  ansMax: number;
  octets: number | null;
  octetsParAn: number | null;
  table: number | null;
  archivees: number | null;
  suivis: number;
  anActuel: number;
  dossierAnalyses: string;
}

const MO = 1024 * 1024;

/** Taille lisible : Mo jusqu'à 10 Go, puis Go. */
function taille(octets: number): string {
  return octets >= 10 * 1024 * MO
    ? t("journalBase.go", { v: (octets / 1024 / MO).toFixed(1) })
    : t("journalBase.mo", { v: Math.max(1, Math.round(octets / MO)) });
}

/** Une durée en années : « Illimitée » ou un nombre d'années. */
function duree(libelle: string, aide: string) {
  const ligne = el("div", "reglage");
  const choix = el("select");
  for (const [v, texte] of [["0", t("journalBase.illimitee")], ["1", t("journalBase.nAns")]] as const) {
    const o = el("option", undefined, texte);
    o.value = v;
    choix.append(o);
  }
  const ans = el("input");
  ans.type = "number";
  ans.min = "1";
  ans.step = "1";
  ans.className = "ans";
  const unite = el("span", undefined, t("journalBase.ans"));
  const peindre = () => {
    const limite = choix.value !== "0";
    ans.hidden = !limite;
    unite.hidden = !limite;
  };
  choix.addEventListener("change", peindre);
  ligne.append(el("span", "libelle", libelle), choix, ans, unite, el("span", "aide", aide));
  return {
    ligne,
    remplir(v: number, max: number, defaut: number) {
      choix.value = v > 0 ? "1" : "0";
      ans.value = String(v > 0 ? v : defaut);
      ans.max = String(max);
      peindre();
    },
    /** Années (0 = illimitée), ou null si la saisie est invalide. */
    valeur(): number | null {
      if (choix.value === "0") return 0;
      const n = Number(ans.value);
      return Number.isInteger(n) && n >= 1 && n <= Number(ans.max) ? n : null;
    },
    surChangement(f: () => void) {
      choix.addEventListener("change", f);
      ans.addEventListener("change", f);
    },
  };
}

function caseACocher(libelle: string) {
  const etiquette = el("label", "case-config");
  const entree = el("input");
  entree.type = "checkbox";
  etiquette.append(entree, ` ${libelle}`);
  return { etiquette, entree };
}

/**
 * Rubrique « Journal et taille de la base » de la Configuration : combien de temps garder le journal et la vie quotidienne,
 * ce que l'on garde quand même (faits marquants, humains suivis), la compression du journal ancien, la taille du monde
 * et la copie pour analyse.
 */
export class RubriqueJournalBase {
  readonly element = el("div", "journal-base");
  private readonly mesure = el("p");
  private readonly conserver = duree(t("journalBase.conserver"), t("journalBase.conserverAide"));
  private readonly quotidien = duree(t("journalBase.quotidien"), t("journalBase.quotidienAide"));
  private readonly marquants = caseACocher(t("journalBase.effacerMarquants"));
  private readonly suivis = caseACocher(t("journalBase.effacerSuivis"));
  private readonly compresser = caseACocher(t("journalBase.compresser"));
  private readonly compresserAns = (() => {
    const ligne = el("label", "reglage");
    const entree = el("input");
    entree.type = "number";
    entree.min = "1";
    entree.step = "1";
    entree.className = "ans";
    ligne.append(el("span", "libelle", t("journalBase.compresserAu")), entree, ` ${t("journalBase.ans")}`);
    return { ligne, entree };
  })();
  private readonly message = el("p", "message-config");
  private readonly debut = el("input");
  private readonly fin = el("input");
  private readonly copier = el("button", undefined, t("journalBase.copier"));
  private readonly messageCopie = el("p", "message-config");
  private etat: JournalBase | null = null;

  constructor() {
    const periode = el("div", "reglage");
    for (const e of [this.debut, this.fin]) {
      e.type = "number";
      e.min = "1";
      e.step = "1";
      e.className = "ans";
    }
    this.debut.placeholder = t("journalBase.debut");
    this.fin.placeholder = t("journalBase.fin");
    periode.append(el("span", "libelle", t("journalBase.periode")), this.debut, " – ", this.fin);
    this.copier.type = "button";
    this.copier.addEventListener("click", () => void this.copierPourAnalyse());
    const boutons = el("div", "boutons");
    boutons.append(this.copier);

    for (const d of [this.conserver, this.quotidien]) d.surChangement(() => void this.choisir());
    for (const c of [this.marquants, this.suivis, this.compresser]) c.entree.addEventListener("change", () => void this.choisir());
    this.compresserAns.entree.addEventListener("change", () => void this.choisir());

    this.element.append(
      this.mesure,
      el("p", "avertissement", t("journalBase.avertissement")),
      this.conserver.ligne,
      this.quotidien.ligne,
      this.marquants.etiquette,
      this.suivis.etiquette,
      el("p", "discret", t("journalBase.gardesAide")),
      this.compresser.etiquette,
      this.compresserAns.ligne,
      el("p", "discret", t("journalBase.compresserAide")),
      this.message,
      el("h4", undefined, t("journalBase.analyse")),
      el("p", "discret", t("journalBase.analyseAide")),
      periode,
      boutons,
      this.messageCopie,
    );
  }

  async remplir(): Promise<void> {
    const j = await connexion.invoke<JournalBase>("JournalBase").catch(() => null);
    this.message.textContent = "";
    this.messageCopie.textContent = "";
    if (!j) {
      this.mesure.textContent = t("commun.pasDeReponse");
      return;
    }
    this.peindre(j);
  }

  private peindre(j: JournalBase): void {
    this.etat = j;
    this.conserver.remplir(j.conserverAns, j.ansMax, 100);
    this.quotidien.remplir(j.quotidienAns, j.ansMax, 10);
    this.marquants.entree.checked = j.effacerMarquants;
    this.suivis.entree.checked = j.effacerSuivis;
    this.compresser.entree.checked = j.compresser;
    this.compresserAns.entree.value = String(j.compresserAns);
    this.compresserAns.entree.max = String(j.ansMax);
    this.compresserAns.entree.disabled = !j.compresser;
    for (const e of [this.debut, this.fin]) e.max = String(j.anActuel);
    this.mesure.textContent = j.octets === null
      ? t("journalBase.mesureEnCours")
      : t("journalBase.mesure", {
          taille: taille(j.octets),
          parAn: taille(j.octetsParAn ?? 0),
          table: j.table ?? 0,
          archivees: j.archivees ?? 0,
        });
  }

  private async choisir(): Promise<void> {
    const avant = this.etat;
    const conserverAns = this.conserver.valeur();
    const quotidienAns = this.quotidien.valeur();
    const compresserAns = Number(this.compresserAns.entree.value);
    if (!avant || conserverAns === null || quotidienAns === null || !Number.isInteger(compresserAns) || compresserAns < 1) {
      this.message.textContent = t("journalBase.erreurAns", { max: avant?.ansMax ?? 0 });
      return;
    }
    // Avant le premier effacement (ou s'il en efface davantage) : rien ne revient, une sauvegarde le permet.
    const effaceAvant = avant.conserverAns > 0 || avant.quotidienAns > 0;
    const effacePlus = (conserverAns > 0 && (avant.conserverAns === 0 || conserverAns < avant.conserverAns))
      || (quotidienAns > 0 && (avant.quotidienAns === 0 || quotidienAns < avant.quotidienAns))
      || ((conserverAns > 0 || quotidienAns > 0)
        && ((this.marquants.entree.checked && !avant.effacerMarquants) || (this.suivis.entree.checked && !avant.effacerSuivis)));
    if (effacePlus && !window.confirm(t(effaceAvant ? "journalBase.confirmerPlus" : "journalBase.confirmerPremier"))) {
      this.peindre(avant);
      return;
    }
    const j = await connexion
      .invoke<JournalBase>("ChoisirJournalBase", conserverAns, quotidienAns, this.marquants.entree.checked, this.suivis.entree.checked,
        this.compresser.entree.checked, compresserAns)
      .catch((e: unknown) => {
        this.message.textContent = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : t("config.reglageInchange");
        return null;
      });
    if (!j) {
      this.peindre(avant);
      return;
    }
    this.peindre(j);
    this.message.textContent = t("journalBase.enregistre");
  }

  private async copierPourAnalyse(): Promise<void> {
    const lire = (e: HTMLInputElement) => (e.value.trim() === "" ? null : Number(e.value));
    const debut = lire(this.debut);
    const fin = lire(this.fin);
    if ((debut !== null && (!Number.isInteger(debut) || debut < 1)) || (fin !== null && (!Number.isInteger(fin) || fin < 1))
      || (debut !== null && fin !== null && debut > fin)) {
      this.messageCopie.textContent = t("journalBase.periodeInvalide");
      return;
    }
    this.copier.disabled = true;
    this.messageCopie.textContent = t("journalBase.copieEnCours");
    try {
      this.messageCopie.textContent = await connexion.invoke<string>("CopierPourAnalyse", debut, fin);
    } catch (e) {
      this.messageCopie.textContent = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : t("commun.pasDeReponse");
    } finally {
      this.copier.disabled = false;
    }
  }
}
