import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";

interface ReponsePing {
  heureServeur: string;
  monde: string;
}

/** Connexion temps réel au serveur, avec affichage de son état dans l'élément fourni. */
export function connecter(statut: HTMLElement): void {
  // textContent (et non innerHTML) : aucune donnée reçue n'est interprétée comme du HTML.
  const afficher = (texte: string, classe: "ok" | "erreur" | "") => {
    statut.textContent = texte;
    statut.className = `statut ${classe}`.trim();
  };

  const connexion = new HubConnectionBuilder()
    .withUrl("/hub/monde")
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  const verifier = async () => {
    const reponse = await connexion.invoke<ReponsePing>("Ping");
    const heure = new Date(reponse.heureServeur).toLocaleTimeString("fr-FR");
    afficher(`Connecté au serveur · ${heure}`, "ok");
  };

  connexion.onreconnecting(() => afficher("Connexion perdue, reconnexion…", "erreur"));
  connexion.onreconnected(() => void verifier());
  connexion.onclose(() => afficher("Déconnecté du serveur", "erreur"));

  const demarrer = async (): Promise<void> => {
    try {
      await connexion.start();
      await verifier();
    } catch {
      afficher("Serveur injoignable, nouvelle tentative…", "erreur");
      setTimeout(() => {
        if (connexion.state === HubConnectionState.Disconnected) void demarrer();
      }, 5000);
    }
  };

  void demarrer();
}
