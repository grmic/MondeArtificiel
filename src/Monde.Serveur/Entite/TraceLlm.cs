using System.Globalization;
using System.Text;

namespace Monde.Serveur.Entite;

/// <summary>
/// Trace en clair des échanges avec le LLM (0.37.2), pour les suivre depuis un terminal (<c>tail -f</c>) : chaque échange est ajouté
/// au fichier <c>entite-echanges.log</c> du dossier de l'application, et une ligne courte s'écrit dans la console du serveur.
/// Rien ne s'écrit tant que la case « Écrire les échanges dans un fichier texte » n'est pas cochée.
/// </summary>
public sealed class TraceLlm(ServiceMonde service, ILogger<TraceLlm> console)
{
    /// <summary>Réglage (dans la base du monde) : « oui » quand la trace est demandée.</summary>
    public const string Cle = "llm_trace";

    /// <summary>Nom du fichier, à côté de <c>entite-llm.json</c>.</summary>
    public const string NomFichier = "entite-echanges.log";

    private static readonly Lock Verrou = new();

    /// <summary>Chemin complet du fichier de trace.</summary>
    public string Chemin => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(service.Depot.CheminFichier)!)!, NomFichier);

    public bool Active => service.Depot.LireReglage(Cle) == "oui";

    /// <summary>Ajoute un bloc au fichier et une ligne à la console ; une panne d'écriture ne gêne jamais le monde.</summary>
    public void Ecrire(string resume, IEnumerable<(string Titre, string? Texte)> sections)
    {
        if (!Active) return;
        console.LogInformation("{Resume}", resume);
        var s = new StringBuilder();
        s.Append("════ ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" · ").AppendLine(resume);
        foreach (var (titre, texte) in sections)
        {
            if (string.IsNullOrWhiteSpace(texte)) continue;
            s.Append("── ").Append(titre).AppendLine(" ──");
            s.AppendLine(texte.TrimEnd());
        }
        s.AppendLine();
        try
        {
            lock (Verrou) File.AppendAllText(Chemin, s.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            console.LogWarning("Trace des échanges impossible : {Erreur}", e.Message);
        }
    }

    /// <summary>Un échange de l'Entité-LLM (regard, prière, demande du joueur), tel que la fenêtre Conversations le montre.</summary>
    public void Echange(EchangeDto e, bool complet)
    {
        var qui = string.Join(", ", new[] { e.NomHumain, e.NomCommunaute }.Where(x => !string.IsNullOrEmpty(x)));
        var resume = L.T("trace.echange", ("id", e.Id), ("occasion", e.Occasion), ("qui", qui.Length == 0 ? "" : " · " + qui), ("modele", e.Modele),
            ("s", e.Secondes.ToString("0.0", CultureInfo.CurrentCulture)), ("statut", e.Statut));
        Ecrire(resume, complet
            ? [(L.T("trace.lu"), e.Lu), (L.T("trace.reponse"), e.Reponse), (L.T("trace.mots"), e.Mots), (L.T("trace.pensee"), e.Pensee),
                (L.T("trace.actes"), string.Join("\n", e.Actes)), (L.T("trace.resultats"), string.Join("\n", e.Resultats)), (L.T("trace.erreur"), e.Erreur)]
            : [(L.T("trace.resultats"), string.Join("\n", e.Resultats)), (L.T("trace.erreur"), e.Erreur)]);
    }

    /// <summary>Une réplique d'un dialogue avec un humain.</summary>
    public void Replique(long dialogue, int rang, string nom, string entite, string mots, string sens, string pensee, IEnumerable<string> regles,
        string lu, string reponse, long dureeMs, string? erreur)
    {
        Ecrire(L.T("trace.replique", ("id", dialogue), ("rang", rang), ("nom", nom), ("s", (dureeMs / 1000.0).ToString("0.0", CultureInfo.CurrentCulture))),
            [(L.T("trace.entite"), entite), (L.T("trace.humain", ("nom", nom)), mots), (L.T("trace.sens"), sens), (L.T("trace.pensee"), pensee),
                (L.T("trace.regles"), string.Join("\n", regles)), (L.T("trace.lu"), lu), (L.T("trace.reponse"), reponse), (L.T("trace.erreur"), erreur)]);
    }
}
