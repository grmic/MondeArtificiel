import { chargerCarte, VueCarte, type InfoTuile } from "./carte";
import { demarrerConnexion } from "./connexion";
import { PanneauHorloge } from "./horloge";
import "./style.css";

const element = <T extends HTMLElement>(selecteur: string) => document.querySelector<T>(selecteur)!;
const infos = element("#infos");
const formulaire = element<HTMLFormElement>("#formGraine");
const champGraine = element<HTMLInputElement>("#graine");

const GRAINE_MAX = 4_294_967_295;

function decrire(info: InfoTuile): void {
  if (!info) return;
  const { x, y, carte, emplacement } = info;
  const i = y * carte.largeur + x;
  const altitude = carte.altitudes[i] - carte.niveauMer;
  const lignes = [
    `Tuile ${x}, ${y} · ${carte.nomsBiomes[carte.biomes[i]] ?? "?"}`,
    `Altitude ${altitude} · gibier ${carte.gibier[i]} · plantes ${carte.plantes[i]}`,
  ];
  if (emplacement) lignes.unshift(`Emplacement n° ${emplacement.numero} (un couple)`);
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
  demarrerConnexion(element("#statut"), () => horloge.synchroniser());

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
