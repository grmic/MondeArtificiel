import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import "./style.css";

interface ReponsePing {
  heureServeur: string;
  monde: string;
}

const statut = document.querySelector<HTMLParagraphElement>("#statut")!;
const details = document.querySelector<HTMLParagraphElement>("#details")!;

// textContent (et non innerHTML) : aucune donnée reçue n'est interprétée comme du HTML.
function afficher(texte: string, classe: "ok" | "erreur" | "", detail = ""): void {
  statut.textContent = texte;
  statut.className = `statut ${classe}`.trim();
  details.textContent = detail;
}

const connexion = new HubConnectionBuilder()
  .withUrl("/hub/monde")
  .withAutomaticReconnect()
  .configureLogging(LogLevel.Warning)
  .build();

connexion.onreconnecting(() => afficher("Connexion perdue, reconnexion…", "erreur"));
connexion.onreconnected(() => void verifier());
connexion.onclose(() => afficher("Déconnecté du serveur", "erreur"));

async function verifier(): Promise<void> {
  const reponse = await connexion.invoke<ReponsePing>("Ping");
  const heure = new Date(reponse.heureServeur).toLocaleTimeString("fr-FR");
  afficher("Connecté au serveur", "ok", `${reponse.monde} · heure du serveur : ${heure}`);
}

async function demarrer(): Promise<void> {
  try {
    await connexion.start();
    await verifier();
  } catch {
    afficher("Serveur injoignable", "erreur", "Nouvelle tentative dans 5 secondes.");
    setTimeout(() => {
      if (connexion.state === HubConnectionState.Disconnected) void demarrer();
    }, 5000);
  }
}

void demarrer();
