import { connexion } from "./connexion";
import { el } from "./fiche";
import { nombre, t } from "./langue";

/** Où en est un humain (et sa communauté) avec les humains de fond (voir MondeHub.EtatFond). */
interface Fond {
  actif: boolean;
  deFond: boolean;
  garde: boolean;
  communauteGardee: boolean;
  nombre: number;
  seuil: number;
}

/**
 * Bloc « Vitesse » d'une fiche : dit si l'humain vit en ce moment de fond (au calme, simulé en groupe) et offre la case
 * « Garder en individu », pour un humain ou pour toute une communauté. Masqué tant que l'option est décochée.
 * « entite-bloc » : la fiche d'un humain le range hors des sections repliables.
 */
// Dernier état connu de l'option : une fiche qui se redessine ne fait pas clignoter le bloc.
let dernierActif = false;

export function blocFond(humain: () => number, communaute: () => number, pourCommunaute: boolean): HTMLElement {
  const bloc = el("div", "fond-bloc entite-bloc");
  bloc.hidden = !dernierActif;
  const etat = el("span", "discret");
  const caseGarder = el("input");
  caseGarder.type = "checkbox";
  const etiquette = el("label", "case-config");
  etiquette.append(caseGarder, ` ${t(pourCommunaute ? "fond.garderCommunaute" : "fond.garder")}`);
  etiquette.title = t("fond.garderAide");
  bloc.append(etiquette, etat);

  const afficher = (f: Fond) => {
    dernierActif = f.actif;
    bloc.hidden = !f.actif;
    caseGarder.checked = pourCommunaute ? f.communauteGardee : f.garde;
    if (pourCommunaute) etat.textContent = " " + t("fond.nombre", { n: nombre(f.nombre) });
    else etat.textContent = " " + (f.garde ? t("fond.etatGarde") : f.communauteGardee ? t("fond.etatCommunaute") : f.deFond ? t("fond.etatFond") : t("fond.etatIndividu"));
  };
  caseGarder.addEventListener("change", () => {
    const id = pourCommunaute ? communaute() : humain();
    connexion.invoke<Fond>("GarderEnIndividu", pourCommunaute, id, caseGarder.checked, humain(), communaute())
      .then(afficher)
      .catch(() => { caseGarder.checked = !caseGarder.checked; });
  });
  // Lu une fois la fiche remplie (le numéro de la communauté d'un humain n'est connu qu'après).
  queueMicrotask(() => {
    connexion.invoke<Fond>("EtatFond", humain(), communaute()).then(afficher).catch(() => { bloc.hidden = true; });
  });
  return bloc;
}
