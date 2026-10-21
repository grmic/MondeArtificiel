import { oublierSuivis, PanneauSuivis } from "./suivis";
import { chargerCarte, distanceEnMots, VueCarte, type Feux, type HumainVue, type InfoTuile, type Nature } from "./carte";
import { BandeauAlertes, PanneauCatastrophes } from "./catastrophes";
import { connexion, demarrerConnexion } from "./connexion";
import { PanneauCommunautes } from "./communautes";
import { PanneauDecouvertes } from "./decouvertes";
import { PanneauDemographie } from "./demographie";
import { PanneauRemarquables } from "./remarquables";
import { PanneauPrieres, PanneauReligions, definirOuvrirCommunaute } from "./cultes";
import { PanneauConfiguration, type Reglages } from "./configuration";
import { definirLiensNoms, PanneauFiche } from "./fiche";
import { PanneauFicheCommunaute } from "./fichecommunaute";
import { PanneauHorloge } from "./horloge";
import { PanneauJournal } from "./journal";
import { OutilMesure, PanneauMonde } from "./monde";
import { PanneauTerritoires } from "./territoires";
import "./style.css";

const element = <T extends HTMLElement>(selecteur: string) => document.querySelector<T>(selecteur)!;
const infos = element("#infos");

const population = element("#population");
const dialogueReinit = element<HTMLDialogElement>("#dialogueReinit");
let nomsParId = new Map<number, string>();
/** Humains du départ (deux par couple) : les identifiants suivants sont des naissances. */
let humainsDeDepart = 66;

function decrireHumain(h: HumainVue): string {
  const nom = (id: number) => nomsParId.get(id) ?? "?";
  const age = h.age < 2 ? `${Math.round(h.age * 12)} mois` : `${Math.floor(h.age)} ans`;
  const liens: string[] = [];
  if (h.mere) liens.push(`enfant de ${nom(h.mere)} et ${h.pere ? nom(h.pere) : "?"}`);
  else liens.push(`couple de départ n° ${h.emplacement}`);
  if (h.partenaire) liens.push(`avec ${nom(h.partenaire)}`);
  const qui = `${h.nom}${h.nomLangue ? ` « ${h.nomLangue} »` : ""} (${h.sexe}, ${age}, ${liens.join(", ")})`;
  if (!h.vivant) return `✝ ${qui}, mort ${h.causeDeMort ?? ""}`;
  const etat = [h.activite, h.humeur ?? "", h.enceinte ? "enceinte" : "", h.malade ? "malade" : "", h.contagieux ? "contagieux" : "",
    h.peur >= 20 ? "apeuré" : ""].filter(Boolean).join(" · ");
  const pensee = h.pensee ? `\n   « ${h.pensee} »` : "";
  return `${qui} ${etat}${pensee}\n   faim ${h.faim} · soif ${h.soif} · fatigue ${h.fatigue} · froid ${h.froid} · santé ${h.sante} · réserves ${h.reserves}`;
}

let natureEn: (x: number, y: number) => { abondance: number; pluie: number } | null = () => null;
let territoireEn: (x: number, y: number) => { nom: string } | null = () => null;
let catastrophesEn: (x: number, y: number) => { nom: string; intensite: string }[] = () => [];
let feuxEn: (x: number, y: number) => string[] = () => [];

function decrire(info: InfoTuile): void {
  if (!info) return;
  const { x, y, carte, emplacement } = info;
  const i = y * carte.largeur + x;
  const altitude = Math.round((carte.altitudes[i] - carte.niveauMer) * carte.metresParUnite);
  const km = (cases: number) => distanceEnMots((cases + 0.5) * carte.kilometresParCase);
  const lignes = [
    `Tuile ${x}, ${y} (${km(x)} du bord ouest, ${km(y)} du bord nord) · ${carte.nomsBiomes[carte.biomes[i]] ?? "?"}`,
    `Altitude ${altitude.toLocaleString("fr-FR").replace(/\u202f|\u00a0/g, " ")} m · gibier ${carte.gibier[i]} · plantes ${carte.plantes[i]}`,
  ];
  const nature = natureEn(x, y);
  if (nature && nature.abondance > 0) {
    lignes.push(`Ressources : ${Math.round(nature.abondance * 100)} % de ce que la saison permet · pluies de l'année : ${Math.round(nature.pluie * 100)} % de la normale`);
  }
  const terres = territoireEn(x, y);
  if (terres) lignes.push(`Terres de la ${terres.nom.charAt(0).toLowerCase()}${terres.nom.slice(1)}`);
  lignes.push(...feuxEn(x, y));
  for (const c of catastrophesEn(x, y)) lignes.push(`⚠ ${c.nom}${c.intensite ? ` (${c.intensite})` : ""}`);
  if (emplacement) lignes.unshift(`Emplacement de départ n° ${emplacement.numero}`);
  if (info.humains.length > 0) lignes.unshift(...info.humains.slice(0, 5).map(decrireHumain));
  infos.textContent = lignes.join("\n");
}

async function demarrer(): Promise<void> {
  const vue = await VueCarte.creer(element("#scene"), decrire);
  natureEn = (x, y) => vue.natureEn(x, y);
  territoireEn = (x, y) => vue.territoireEn(x, y);
  catastrophesEn = (x, y) => vue.catastrophesEn(x, y);
  feuxEn = (x, y) => vue.feuxEn(x, y);

  // Feux et réserves : relus toutes les 5 secondes ; les réserves peuvent être masquées (choix retenu par le navigateur).
  const caseReserves = element<HTMLInputElement>("#voirReserves");
  try {
    caseReserves.checked = localStorage.getItem("monde.reserves") !== "non";
  } catch {
    caseReserves.checked = true;
  }
  let derniersFeux: Feux | null = null;
  const lireFeux = async () => {
    const feux = await connexion.invoke<Feux>("Feux").catch(() => null);
    if (feux) derniersFeux = feux;
    vue.afficherFeux(derniersFeux, caseReserves.checked);
  };
  caseReserves.addEventListener("change", () => {
    try {
      localStorage.setItem("monde.reserves", caseReserves.checked ? "oui" : "non");
    } catch {
      // Stockage indisponible : le choix vaut pour cette page seulement.
    }
    vue.afficherFeux(derniersFeux, caseReserves.checked);
  });
  window.setInterval(() => void lireFeux(), 5000);
  void lireFeux();

  // Couche des ressources : relue toutes les 5 secondes tant qu'elle est affichée.
  const boutonRessources = element<HTMLButtonElement>("#boutonRessources");
  let minuteurNature: number | undefined;
  const lireNature = async () => {
    const nature = await connexion.invoke<Nature>("Nature").catch(() => null);
    if (nature && boutonRessources.classList.contains("actif")) vue.afficherNature(nature);
  };
  boutonRessources.addEventListener("click", () => {
    const actif = boutonRessources.classList.toggle("actif");
    window.clearInterval(minuteurNature);
    if (actif) {
      void lireNature();
      minuteurNature = window.setInterval(() => void lireNature(), 5000);
    } else {
      vue.afficherNature(null);
    }
  });
  // Extinction : le dernier humain est mort. Le monde s'est mis en pause ; la nature continue seule si on relance le temps.
  let eteint = false;
  let extinctionVue = false;
  const montrerExtinction = (texte: string) => {
    extinctionVue = true;
    element("#texteExtinction").textContent = texte;
    element("#extinction").hidden = false;
  };
  connexion.on("Extinction", (texte: string) => {
    montrerExtinction(`${texte} Le temps est en pause.`);
  });
  element("#extinctionFermer").addEventListener("click", () => (element("#extinction").hidden = true));
  const horloge = new PanneauHorloge(
    element("#panneauTemps"),
    element("#date"),
    element("#vitesses"),
    (h) => {
      vue.eclairer(h.lumiere);
      eteint = h.eteint;
      // Monde déjà éteint à l'ouverture de la page : le bandeau le rappelle une fois.
      if (h.eteint && !extinctionVue) montrerExtinction("Plus aucun humain ne vit dans ce monde.");
      if (!h.eteint) {
        extinctionVue = false;
        element("#extinction").hidden = true;
      }
    },
    (message) => (infos.textContent = message),
  );
  const montrerHumains = (humains: HumainVue[]) => {
    // Les noms restent connus après que les morts anciens ont quitté la liste : le journal les cite encore.
    // Un monde réinitialisé (le premier humain n'a plus le même nom) repart de zéro.
    const premier = humains.find((h) => h.id === 1);
    if (premier && nomsParId.get(1) !== premier.nom) nomsParId = new Map();
    for (const h of humains) nomsParId.set(h.id, h.nom);
    vue.afficherHumains(humains);
    const vivants = humains.filter((h) => h.vivant);
    const enfants = vivants.filter((h) => h.etape !== "adulte").length;
    const enceintes = vivants.filter((h) => h.enceinte).length;
    const naissances = Math.max(0, Math.max(...humains.map((h) => h.id)) - humainsDeDepart);
    if (vivants.length === 0 && (eteint || humains.length > 0)) {
      population.textContent = `Aucun humain · naissances : ${naissances}`;
      return;
    }
    population.textContent = `Vivants : ${vivants.length} (dont ${enfants} enfants) · enceintes : ${enceintes} · naissances : ${naissances}`;
  };
  connexion.on("Humains", (humains: HumainVue[]) => {
    montrerHumains(humains);
  });

  // Clic sur un humain : sa fiche. Le journal s'ouvre depuis la barre du haut.
  let communautes: PanneauCommunautes | undefined = undefined;
  let ficheCommunaute: PanneauFicheCommunaute | undefined = undefined;
  /** La fiche d'une communauté prend la place de celle de l'humain ; un bouton « retour » ramène à celle d'où l'on vient. */
  const ouvrirCommunaute = (id: number) => {
    const humain = fiche.courant;
    fiche.fermer();
    if (ficheCommunaute) {
      ficheCommunaute.retour = humain ? { libelle: `fiche de ${nomsParId.get(humain) ?? "?"}`, aller: () => void fiche.ouvrir(humain) } : null;
      void ficheCommunaute.ouvrir(id);
    }
  };
  const fiche = new PanneauFiche(element("#fiche"), (id) => nomsParId.get(id) ?? "?", (id) => {
    vue.selectionner(id);
    // Une fiche d'humain qui s'ouvre ferme celle de la communauté (elles occupent la même place).
    if (id) ficheCommunaute?.fermer();
  }, (id) => ouvrirCommunaute(id));
  const territoires = new PanneauTerritoires(element("#panneauTerritoires"), vue, (id) => ouvrirCommunaute(id));
  const suivreCommunaute = (id: number, nom: string) => {
    communautes?.fermer();
    journal.suivreCommunaute(id, nom);
  };
  const montrerTerres = (id: number) => {
    territoires.choisir(id);
    if (!territoires.ouvert) void territoires.ouvrir();
  };
  ficheCommunaute = new PanneauFicheCommunaute(element("#ficheCommunaute"), (id) => {
    const c = ficheCommunaute?.courant ?? 0;
    const nom = ficheCommunaute?.nom ?? "";
    fiche.retour = c ? { libelle: `fiche de la ${nom.charAt(0).toLowerCase()}${nom.slice(1)}`, aller: () => ouvrirCommunaute(c) } : null;
    void fiche.ouvrir(id);
  }, (ids) => vue.surligner(ids), suivreCommunaute, montrerTerres, (x, y) => vue.montrer(x, y));
  communautes = new PanneauCommunautes(element("#communautes"), (ids) => vue.surligner(ids), (id) => void fiche.ouvrir(id),
    suivreCommunaute, montrerTerres, (id) => ouvrirCommunaute(id));
  communautes.surChoix = (id) => territoires.choisir(id);
  element("#boutonTerritoires").addEventListener("click", () => territoires.basculer());

  const catastrophes = new PanneauCatastrophes(element("#catastrophes"), vue, (id) => {
    catastrophes.fermer();
    ouvrirCommunaute(id);
  }, (message) => (infos.textContent = message));
  const alertes = new BandeauAlertes(element("#alerte"), element("#boutonCatastrophes"), (id) => {
    journal.fermer();
    communautes?.fermer();
    alertes.vues();
    void catastrophes.ouvrir(id);
  });
  element("#boutonCatastrophes").addEventListener("click", () => {
    alertes.vues();
    journal.fermer();
    communautes?.fermer();
    panneauMonde?.fermer();
    configuration.fermer();
    demographie.fermer();
    decouvertes.fermer();
    remarquables.fermer();
    catastrophes.basculer();
  });

  const panneauMonde = new PanneauMonde(element("#caracteristiques"), vue);
  const mesure = new OutilMesure(element<HTMLButtonElement>("#boutonMesure"), vue, (message) => (infos.textContent = message));
  element("#boutonMonde").addEventListener("click", () => {
    journal.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    configuration.fermer();
    demographie.fermer();
    decouvertes.fermer();
    remarquables.fermer();
    panneauMonde.basculer();
  });

  const demographie = new PanneauDemographie(element("#demographie"), (id) => {
    demographie.fermer();
    decouvertes.fermer();
    remarquables.fermer();
    void catastrophes.ouvrir(id);
  }, (id) => {
    demographie.fermer();
    ouvrirCommunaute(id);
  });
  const decouvertes = new PanneauDecouvertes(element("#decouvertes"), (id) => void fiche.ouvrir(id));
  const remarquables = new PanneauRemarquables(element("#remarquables"), (id) => {
    vue.selectionner(id);
    void fiche.ouvrir(id);
  }, (ids) => vue.surligner(ids), (id) => {
    remarquables.fermer();
    ouvrirCommunaute(id);
  });
  element("#boutonRemarquables").addEventListener("click", () => {
    journal.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    configuration.fermer();
    demographie.fermer();
    decouvertes.fermer();
    remarquables.basculer();
  });
  const prieres = new PanneauPrieres(element("#prieres"), (id) => {
    vue.selectionner(id);
    void fiche.ouvrir(id);
  });
  const religions = new PanneauReligions(element("#religions"), (id) => {
    vue.selectionner(id);
    void fiche.ouvrir(id);
  }, (lieux) => vue.marquerLieuxSacres(lieux));
  definirOuvrirCommunaute((id) => {
    prieres.fermer();
    religions.fermer();
    ouvrirCommunaute(id);
  });
  // Prières et religions occupent la place des autres fenêtres latérales : en ouvrir une autre les ferme.
  for (const b of document.querySelectorAll<HTMLButtonElement>("#formGraine button")) {
    if (b.id !== "boutonPrieres") b.addEventListener("click", () => prieres.fermer());
    if (b.id !== "boutonReligions") b.addEventListener("click", () => religions.fermer());
  }
  const fermerAutres = () => {
    journal.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    configuration.fermer();
    demographie.fermer();
    decouvertes.fermer();
    remarquables.fermer();
  };
  const suivis = new PanneauSuivis(element("#suivis"), (id) => {
    vue.selectionner(id);
    void fiche.ouvrir(id);
  }, (id) => ouvrirCommunaute(id), (ids) => vue.surligner(ids));
  for (const b of document.querySelectorAll<HTMLButtonElement>("#formGraine button")) {
    if (b.id !== "boutonSuivis") b.addEventListener("click", () => suivis.fermer());
  }
  element("#boutonSuivis").addEventListener("click", () => {
    fermerAutres();
    prieres.fermer();
    religions.fermer();
    suivis.basculer();
  });
  element("#boutonPrieres").addEventListener("click", () => {
    fermerAutres();
    prieres.basculer();
  });
  element("#boutonReligions").addEventListener("click", () => {
    fermerAutres();
    religions.basculer();
  });
  element("#boutonDecouvertes").addEventListener("click", () => {
    journal.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    configuration.fermer();
    demographie.fermer();
    decouvertes.basculer();
  });
  element("#boutonDemographie").addEventListener("click", () => {
    journal.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    configuration.fermer();
    decouvertes.fermer();
    remarquables.fermer();
    demographie.basculer();
  });

  vue.quandClic((info) => {
    if (info && mesure.enCours) {
      void mesure.cliquer(info.x, info.y);
      return;
    }
    if (info && catastrophes.enVisee) {
      catastrophes.viserEn(info.x, info.y);
      return;
    }
    const h = vue.humainsSous(info)[0];
    if (h) void fiche.ouvrir(h.id);
  });
  const idDuNom = (nom: string) => {
    const cherche = nom.toLocaleLowerCase("fr");
    for (const [id, n] of nomsParId) if (n.toLocaleLowerCase("fr") === cherche) return id;
    return undefined;
  };
  definirLiensNoms(idDuNom, (id) => void fiche.ouvrir(id), (id) => nomsParId.get(id) ?? "?");
  const journal = new PanneauJournal(element("#journal"), idDuNom, () => [...new Set(nomsParId.values())].sort(), (id) => void fiche.ouvrir(id));
  // Journal et communautés occupent la même place : ouvrir l'un ferme l'autre.
  element("#boutonJournal").addEventListener("click", () => {
    communautes?.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    configuration.fermer();
    demographie.fermer();
    decouvertes.fermer();
    remarquables.fermer();
    journal.basculer();
  });
  element("#boutonCommunautes").addEventListener("click", () => {
    journal.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    configuration.fermer();
    demographie.fermer();
    decouvertes.fermer();
    remarquables.fermer();
    communautes?.basculer();
  });

  demarrerConnexion(element("#statut"), async () => {
    await horloge.synchroniser();
    montrerHumains(await connexion.invoke<HumainVue[]>("Humains"));
    await journal.recharger();
  });

  // Carte du monde courant (sans réglages) ou aperçu d'autres réglages : rend un message d'erreur, ou null.
  const charger = async (reglages?: Reglages): Promise<string | null> => {
    infos.textContent = "Génération de la carte…";
    try {
      const carte = await chargerCarte(reglages ? { ...reglages } : undefined);
      vue.afficher(carte, reglages !== undefined);
      if (!reglages) humainsDeDepart = carte.emplacements.length * 2;
      const apercu = reglages ? "Aperçu (le monde courant ne change pas)" : "Monde courant";
      infos.textContent = `${apercu} · graine ${carte.graine} · ${carte.emplacements.length} emplacements de départ.\nMolette : zoom · glisser : déplacer · double-clic : recadrer.`;
      return null;
    } catch (e) {
      const raison = e instanceof Error ? e.message : "";
      infos.textContent = `Impossible de charger la carte. ${raison}`;
      return raison || "Impossible de charger la carte.";
    }
  };

  const configuration = new PanneauConfiguration(element("#configuration"), dialogueReinit,
    (reglages) => charger(reglages ?? undefined), (texte) => (infos.textContent = texte));
  const ouvrirConfiguration = (section: "sauvegardes" | "nouveau") => {
    journal.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    demographie.fermer();
    decouvertes.fermer();
    remarquables.fermer();
    element("#extinction").hidden = true;
    void configuration.ouvrir(section);
  };
  element("#extinctionRestaurer").addEventListener("click", () => ouvrirConfiguration("sauvegardes"));
  element("#extinctionNouveau").addEventListener("click", () => ouvrirConfiguration("nouveau"));
  element("#boutonConfiguration").addEventListener("click", () => {
    journal.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    demographie.fermer();
    decouvertes.fermer();
    remarquables.fermer();
    configuration.basculer();
  });

  // Nouveau monde : demandé depuis la configuration, annoncé par le serveur à tous les navigateurs ouverts.
  connexion.on("MondeReinitialise", (graine: number) => {
    fiche.fermer();
    ficheCommunaute?.fermer();
    territoires.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    panneauMonde.fermer();
    if (mesure.enCours) mesure.arreter();
    alertes.vider();
    alertes.vues();
    oublierSuivis();
    void journal.recharger();
    void charger().then(() => {
      configuration.mondeRecree();
      infos.textContent = `Nouveau monde · graine ${graine}. Le temps est en pause.`;
    });
  });

  window.addEventListener("resize", () => vue.cadrer());
  await charger();
}

void demarrer();
