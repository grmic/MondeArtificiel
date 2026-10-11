import { chargerCarte, VueCarte, type HumainVue, type InfoTuile, type Nature } from "./carte";
import { BandeauAlertes, PanneauCatastrophes } from "./catastrophes";
import { connexion, demarrerConnexion } from "./connexion";
import { PanneauCommunautes } from "./communautes";
import { PanneauFiche } from "./fiche";
import { PanneauHorloge } from "./horloge";
import { PanneauJournal } from "./journal";
import { PanneauTerritoires } from "./territoires";
import "./style.css";

const element = <T extends HTMLElement>(selecteur: string) => document.querySelector<T>(selecteur)!;
const infos = element("#infos");
const formulaire = element<HTMLFormElement>("#formGraine");
const champGraine = element<HTMLInputElement>("#graine");

const population = element("#population");
const boutonReinit = element<HTMLButtonElement>("#boutonReinit");
const dialogueReinit = element<HTMLDialogElement>("#dialogueReinit");
const GRAINE_MAX = 4_294_967_295;
let nomsParId = new Map<number, string>();

function decrireHumain(h: HumainVue): string {
  const nom = (id: number) => nomsParId.get(id) ?? "?";
  const age = h.age < 2 ? `${Math.round(h.age * 12)} mois` : `${Math.floor(h.age)} ans`;
  const liens: string[] = [];
  if (h.mere) liens.push(`enfant de ${nom(h.mere)} et ${h.pere ? nom(h.pere) : "?"}`);
  else liens.push(`couple de départ n° ${h.emplacement}`);
  if (h.partenaire) liens.push(`avec ${nom(h.partenaire)}`);
  const qui = `${h.nom} (${h.sexe}, ${age}, ${liens.join(", ")})`;
  if (!h.vivant) return `✝ ${qui}, mort ${h.causeDeMort ?? ""}`;
  const etat = [h.activite, h.humeur ?? "", h.enceinte ? "enceinte" : "", h.malade ? "malade" : "", h.contagieux ? "contagieux" : "",
    h.peur >= 20 ? "apeuré" : ""].filter(Boolean).join(" · ");
  const pensee = h.pensee ? `\n   « ${h.pensee} »` : "";
  return `${qui} ${etat}${pensee}\n   faim ${h.faim} · soif ${h.soif} · fatigue ${h.fatigue} · froid ${h.froid} · santé ${h.sante} · réserves ${h.reserves}`;
}

let natureEn: (x: number, y: number) => { abondance: number; pluie: number } | null = () => null;
let territoireEn: (x: number, y: number) => { nom: string } | null = () => null;
let catastrophesEn: (x: number, y: number) => { nom: string; intensite: string }[] = () => [];

function decrire(info: InfoTuile): void {
  if (!info) return;
  const { x, y, carte, emplacement } = info;
  const i = y * carte.largeur + x;
  const altitude = carte.altitudes[i] - carte.niveauMer;
  const lignes = [
    `Tuile ${x}, ${y} · ${carte.nomsBiomes[carte.biomes[i]] ?? "?"}`,
    `Altitude ${altitude} · gibier ${carte.gibier[i]} · plantes ${carte.plantes[i]}`,
  ];
  const nature = natureEn(x, y);
  if (nature && nature.abondance > 0) {
    lignes.push(`Ressources : ${Math.round(nature.abondance * 100)} % de ce que la saison permet · pluies de l'année : ${Math.round(nature.pluie * 100)} % de la normale`);
  }
  const terres = territoireEn(x, y);
  if (terres) lignes.push(`Terres de la ${terres.nom.charAt(0).toLowerCase()}${terres.nom.slice(1)}`);
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
  const horloge = new PanneauHorloge(
    element("#panneauTemps"),
    element("#date"),
    element("#vitesses"),
    (h) => vue.eclairer(h.lumiere),
    (message) => (infos.textContent = message),
  );
  const montrerHumains = (humains: HumainVue[]) => {
    nomsParId = new Map(humains.map((h) => [h.id, h.nom]));
    vue.afficherHumains(humains);
    const vivants = humains.filter((h) => h.vivant);
    const enfants = vivants.filter((h) => h.etape !== "adulte").length;
    const enceintes = vivants.filter((h) => h.enceinte).length;
    const naissances = Math.max(0, Math.max(...humains.map((h) => h.id)) - 66);
    population.textContent = `Vivants : ${vivants.length} (dont ${enfants} enfants) · enceintes : ${enceintes} · naissances : ${naissances}`;
  };
  connexion.on("Humains", (humains: HumainVue[]) => {
    montrerHumains(humains);
  });

  // Clic sur un humain : sa fiche. Le journal s'ouvre depuis la barre du haut.
  let communautes: PanneauCommunautes | undefined = undefined;
  const fiche = new PanneauFiche(element("#fiche"), (id) => nomsParId.get(id) ?? "?", (id) => vue.selectionner(id),
    (id) => {
      journal.fermer();
      void communautes?.ouvrir(id);
    });
  const territoires = new PanneauTerritoires(element("#panneauTerritoires"), vue, (id) => {
    journal.fermer();
    void communautes?.ouvrir(id);
  });
  communautes = new PanneauCommunautes(element("#communautes"), (ids) => vue.surligner(ids), (id) => void fiche.ouvrir(id),
    (id, nom) => {
      communautes?.fermer();
      journal.suivreCommunaute(id, nom);
    },
    (id) => {
      territoires.choisir(id);
      if (!territoires.ouvert) void territoires.ouvrir();
    });
  communautes.surChoix = (id) => territoires.choisir(id);
  element("#boutonTerritoires").addEventListener("click", () => territoires.basculer());

  const catastrophes = new PanneauCatastrophes(element("#catastrophes"), vue, (id) => {
    catastrophes.fermer();
    journal.fermer();
    void communautes?.ouvrir(id);
  }, (message) => (infos.textContent = message));
  new BandeauAlertes(element("#alerte"), (id) => {
    journal.fermer();
    communautes?.fermer();
    void catastrophes.ouvrir(id);
  });
  element("#boutonCatastrophes").addEventListener("click", () => {
    journal.fermer();
    communautes?.fermer();
    catastrophes.basculer();
  });

  vue.quandClic((info) => {
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
  const journal = new PanneauJournal(element("#journal"), idDuNom, () => [...new Set(nomsParId.values())].sort(), (id) => void fiche.ouvrir(id));
  // Journal et communautés occupent la même place : ouvrir l'un ferme l'autre.
  element("#boutonJournal").addEventListener("click", () => {
    communautes?.fermer();
    catastrophes.fermer();
    journal.basculer();
  });
  element("#boutonCommunautes").addEventListener("click", () => {
    journal.fermer();
    catastrophes.fermer();
    communautes?.basculer();
  });

  demarrerConnexion(element("#statut"), async () => {
    await horloge.synchroniser();
    montrerHumains(await connexion.invoke<HumainVue[]>("Humains"));
    await journal.recharger();
  });

  const charger = async (graine?: number) => {
    infos.textContent = "Génération de la carte…";
    try {
      const carte = await chargerCarte(graine);
      champGraine.value = String(carte.graine);
      if (graine === undefined) graineMonde = carte.graine;
      vue.afficher(carte);
      const apercu = graine === undefined ? "Monde courant" : "Aperçu";
      infos.textContent = `${apercu} · graine ${carte.graine} · ${carte.emplacements.length} emplacements de départ.\nMolette : zoom · glisser : déplacer · double-clic : recadrer.`;
    } catch {
      infos.textContent = "Impossible de charger la carte.";
    }
  };

  formulaire.addEventListener("submit", (ev) => {
    ev.preventDefault();
    const graine = Number(champGraine.value);
    if (!Number.isInteger(graine) || graine < 0 || graine > GRAINE_MAX) {
      infos.textContent = `La graine doit être un entier entre 0 et ${GRAINE_MAX}.`;
      return;
    }
    void charger(graine);
  });

  // Réinitialisation : demandée ici, annoncée par le serveur à tous les navigateurs ouverts.
  let graineMonde: number | undefined;
  connexion.on("MondeReinitialise", (graine: number) => {
    fiche.fermer();
    territoires.fermer();
    communautes?.fermer();
    catastrophes.fermer();
    element("#alerte").hidden = true;
    void journal.recharger();
    void charger().then(() => (infos.textContent = `Monde réinitialisé · graine ${graine}. Le temps est en pause.`));
  });
  boutonReinit.addEventListener("click", () => {
    const champ = Number(champGraine.value);
    element("#graineCourante").textContent = String(graineMonde ?? "?");
    element("#graineChamp").textContent = Number.isInteger(champ) && champ >= 0 && champ <= GRAINE_MAX ? String(champ) : "invalide";
    dialogueReinit.returnValue = "";
    dialogueReinit.showModal();
  });
  dialogueReinit.addEventListener("close", () => {
    if (dialogueReinit.returnValue !== "confirmer") return;
    const choix = new FormData(dialogueReinit.querySelector("form")!).get("choixGraine");
    let graine: number | null = null;
    if (choix === "courante") graine = graineMonde ?? null;
    if (choix === "champ") {
      graine = Number(champGraine.value);
      if (!Number.isInteger(graine) || graine < 0 || graine > GRAINE_MAX) {
        infos.textContent = `La graine doit être un entier entre 0 et ${GRAINE_MAX}.`;
        return;
      }
    }
    infos.textContent = "Réinitialisation du monde…";
    connexion.invoke("Reinitialiser", graine).catch(() => (infos.textContent = "Le serveur a refusé la réinitialisation."));
  });

  window.addEventListener("resize", () => vue.cadrer());
  await charger();
}

void demarrer();
