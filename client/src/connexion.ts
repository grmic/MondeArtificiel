import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { langue, t } from "./langue";

/** Connexion temps réel au serveur, partagée par toute la page. */
export const connexion: HubConnection = new HubConnectionBuilder()
  .withUrl(`/hub/monde?langue=${encodeURIComponent(langue())}`)
  .withAutomaticReconnect()
  .configureLogging(LogLevel.Warning)
  .build();

/**
 * 0.45.24 : une demande identique (même méthode, mêmes arguments) déjà partie et sans réponse n'est pas renvoyée : on attend la
 * même réponse. Les lectures répétées (feux, météo, scène… toutes les quelques secondes) ne s'empilent plus derrière une réponse
 * lente, et le flux des bêtes de la vue 3D ne reste plus des dizaines de secondes en file.
 */
const enCours = new Map<string, Promise<unknown>>();
const invokeDirect = connexion.invoke.bind(connexion);
connexion.invoke = (<T>(methode: string, ...args: unknown[]): Promise<T> => {
  let cle: string;
  try {
    cle = methode + "\u0000" + JSON.stringify(args);
  } catch {
    return invokeDirect<T>(methode, ...args);
  }
  const deja = enCours.get(cle);
  if (deja) return deja as Promise<T>;
  const p = invokeDirect<T>(methode, ...args).finally(() => enCours.delete(cle));
  enCours.set(cle, p);
  return p;
}) as typeof connexion.invoke;

/**
 * Démarre la connexion et affiche son état. `aChaqueConnexion` est rappelée
 * après la première connexion et après chaque reconnexion.
 */
export function demarrerConnexion(statut: HTMLElement, aChaqueConnexion: () => Promise<void>): void {
  // textContent (et non innerHTML) : aucune donnée reçue n'est interprétée comme du HTML.
  const afficher = (texte: string, classe: "ok" | "erreur" | "") => {
    statut.textContent = texte;
    statut.className = `statut ${classe}`.trim();
  };

  const connecte = async () => {
    afficher(t("connexion.connecte"), "ok");
    await aChaqueConnexion();
  };

  connexion.onreconnecting(() => afficher(t("connexion.perdue"), "erreur"));
  connexion.onreconnected(() => void connecte());
  connexion.onclose(() => afficher(t("connexion.deconnecte"), "erreur"));

  const demarrer = async (): Promise<void> => {
    try {
      await connexion.start();
      await connecte();
    } catch {
      afficher(t("connexion.injoignable"), "erreur");
      setTimeout(() => {
        if (connexion.state === HubConnectionState.Disconnected) void demarrer();
      }, 5000);
    }
  };

  void demarrer();
}
