import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";

/** Connexion temps réel au serveur, partagée par toute la page. */
export const connexion: HubConnection = new HubConnectionBuilder()
  .withUrl("/hub/monde")
  .withAutomaticReconnect()
  .configureLogging(LogLevel.Warning)
  .build();

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
    afficher("Connecté", "ok");
    await aChaqueConnexion();
  };

  connexion.onreconnecting(() => afficher("Connexion perdue, reconnexion…", "erreur"));
  connexion.onreconnected(() => void connecte());
  connexion.onclose(() => afficher("Déconnecté du serveur", "erreur"));

  const demarrer = async (): Promise<void> => {
    try {
      await connexion.start();
      await connecte();
    } catch {
      afficher("Serveur injoignable, nouvelle tentative…", "erreur");
      setTimeout(() => {
        if (connexion.state === HubConnectionState.Disconnected) void demarrer();
      }, 5000);
    }
  };

  void demarrer();
}
