import { chargerCarte, VueCarte, type HumainVue, type InfoTuile } from "./carte";
import { connexion, demarrerConnexion } from "./connexion";
import { PanneauHorloge } from "./horloge";
import "./style.css";

const element = <T extends HTMLElement>(selecteur: string) => document.querySelector<T>(selecteur)!;
const infos = element("#infos");
const formulaire = element<HTMLFormElement>("#formGraine");
const champGraine = element<HTMLInputElement>("#graine");

const population = element("#population");
const GRAINE_MAX = 4_294_967_295;
let nomsParId = new Map<number, string>();

function decrireHumain(h: HumainVue): string {
  const compagnon = nomsParId.get(h.partenaire) ?? "?";
  const qui = `${h.nom} (${h.sexe}, ${Math.floor(h.age)} ans, couple n° ${h.emplacement} avec ${compagnon})`;
  if (!h.vivant) return `✝ ${qui}, mort ${h.causeDeMort ?? ""}`;
  const malade = h.malade ? " · malade" : "";
  return `${qui} ${h.activite}${malade}\n   faim ${h.faim} · soif ${h.soif} · fatigue ${h.fatigue} · froid ${h.froid} · santé ${h.sante}`;
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
  if (info.humains.length > 0) lignes.unshift(...info.humains.slice(0, 4).map(decrireHumain));
  infos.textContent = lignes.join("\n");
}

async function demarrer(): Promise<void> {
  const vue = await VueCarte.creer(element("#scene"), decrire);
  const horloge = new PanneauHorloge(
    element("#date"),
    element("#vitesses"),
    (h) => vue.eclairer(h.lumiere),
    (message) => (infos.textContent = message),
  );
  const montrerHumains = (humains: HumainVue[]) => {
    nomsParId = new Map(humains.map((h) => [h.id, h.nom]));
    vue.afficherHumains(humains);
    population.textContent = `Vivants : ${humains.filter((h) => h.vivant).length} / ${humains.length}`;
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

  window.addEventListener("resize", () => vue.cadrer());
  await charger();
}

void demarrer();
