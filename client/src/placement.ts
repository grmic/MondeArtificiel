import { t } from "./langue";
/**
 * Place la fenêtre du temps juste sous la barre de boutons, quelle que soit sa hauteur (elle passe sur plusieurs lignes
 * sur un écran étroit), et les panneaux de droite sous la fenêtre du temps. La fenêtre du temps peut se réduire à la
 * seule vitesse libre ; le navigateur retient ce choix.
 */
const CLE_REDUIT = "monde.tempsReduit";
const ECART = 8;

function lireReduit(): boolean {
  try {
    return localStorage.getItem(CLE_REDUIT) === "oui";
  } catch {
    return false;
  }
}

function ecrireReduit(oui: boolean): void {
  try {
    localStorage.setItem(CLE_REDUIT, oui ? "oui" : "non");
  } catch {
    // Stockage refusé : le choix vaut pour cette page seulement.
  }
}

export function placerSousLaBarre(barre: HTMLElement, temps: HTMLElement, reduire: HTMLButtonElement): void {
  const racine = document.documentElement;
  const placer = () => {
    const sousBarre = Math.ceil(barre.getBoundingClientRect().bottom) + ECART;
    racine.style.setProperty("--sous-barre", `${sousBarre}px`);
    const basTemps = temps.hidden ? sousBarre - ECART : Math.ceil(temps.getBoundingClientRect().bottom);
    racine.style.setProperty("--sous-temps", `${basTemps + ECART}px`);
  };
  const appliquer = (reduit: boolean) => {
    temps.classList.toggle("reduit", reduit);
    reduire.textContent = reduit ? "▾" : "▴";
    reduire.title = reduit ? t("temps.agrandirAide") : t("temps.reduireAide");
    reduire.setAttribute("aria-expanded", String(!reduit));
    placer();
  };
  reduire.addEventListener("click", () => {
    const reduit = !temps.classList.contains("reduit");
    ecrireReduit(reduit);
    appliquer(reduit);
  });
  appliquer(lireReduit());
  const observateur = new ResizeObserver(placer);
  observateur.observe(barre);
  observateur.observe(temps);
  window.addEventListener("resize", placer);
}
