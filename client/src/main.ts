import { chargerCarte, VueCarte, type HumainVue, type InfoTuile } from "./carte";
import { connexion, demarrerConnexion } from "./connexion";
import { PanneauHorloge } from "./horloge";
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
  const etat = [h.activite, h.enceinte ? "enceinte" : "", h.malade ? "malade" : ""].filter(Boolean).join(" · ");
  return `${qui} ${etat}\n   faim ${h.faim} · soif ${h.soif} · fatigue ${h.fatigue} · froid ${h.froid} · santé ${h.sante}`;
}

function decrire(info: InfoTuile): void {
  if (!info) return;
  const { x, y, carte, emplacement } = info;
  const i = y * carte.largeur + x;
  const altitude = carte.altitudes[i] - carte.niveauMer;
  const lignes = [
    `Tuile ${x}, ${y} · ${carte.nomsBiomes[carte.biomes[i]] ?? "?"}`,
    `Altitude ${altitude} · gibier ${carte.gibier[i]} · plantes ${carte.plantes[i]}`,
  ];
  if (emplacement) lignes.unshift(`Emplacement de départ n° ${emplacement.numero}`);
  if (info.humains.length > 0) lignes.unshift(...info.humains.slice(0, 5).map(decrireHumain));
  infos.textContent = lignes.join("\n");
}

async function demarrer(): Promise<void> {
  const vue = await VueCarte.creer(element("#scene"), decrire);
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
  connexion.on("Humains", montrerHumains);
  demarrerConnexion(element("#statut"), async () => {
    await horloge.synchroniser();
    montrerHumains(await connexion.invoke<HumainVue[]>("Humains"));
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
