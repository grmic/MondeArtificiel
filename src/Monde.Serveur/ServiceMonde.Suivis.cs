using Monde.Simulation;
using Monde.Simulation.Recits;
using Monde.Stockage;

namespace Monde.Serveur;

/// <summary>Suivis ★ et alertes (0.40.0) : gardés dans la base du monde, communs à tous les navigateurs.</summary>
public sealed partial class ServiceMonde
{
    private const string CleAlertes = "alertes_regles";

    // Alertes reçues du monde, gardées dans la base à la prochaine sauvegarde (après celle des humains qu'elles citent).
    private readonly List<AlerteSuivi> _alertesAEnregistrer = [];

    /// <summary>Pour chaque sorte d'alerte : prévenir, et mettre en pause. Gardé dans la base du monde.</summary>
    public ReglesAlertes ReglesAlertes
    {
        get => LireRegles(Depot.LireReglage(CleAlertes));
        set
        {
            Depot.EcrireReglage(CleAlertes, string.Concat(value.Prevenir.Select(b => b ? '1' : '0')) + ";" + string.Concat(value.Pause.Select(b => b ? '1' : '0')));
            AppliquerVeille(_monde);
        }
    }

    /// <summary>Lit « 1101…;0100… » ; une sorte ajoutée depuis (texte plus court) prend la valeur par défaut.</summary>
    private static ReglesAlertes LireRegles(string? texte)
    {
        var regles = ReglesAlertes.ParDefaut();
        if (string.IsNullOrEmpty(texte)) return regles;
        var parties = texte.Split(';');
        for (var i = 0; i < ReglesAlertes.Nombre; i++)
        {
            if (parties.Length > 0 && i < parties[0].Length) regles.Prevenir[i] = parties[0][i] == '1';
            if (parties.Length > 1 && i < parties[1].Length) regles.Pause[i] = parties[1][i] == '1';
        }
        return regles;
    }

    public List<Suivi> Suivis() => Depot.LireSuivisComplets();

    /// <summary>Ajoute ou met à jour des suivis, et en retire ; le monde veille désormais sur eux.</summary>
    public void ChangerSuivis(IEnumerable<Suivi> ajoutes, IEnumerable<(bool Communaute, int Id)> retires)
    {
        lock (_verrou) Depot.ChangerSuivisComplets(ajoutes, retires);
        SuivisChanges();
    }

    /// <summary>Dit au monde sur qui veiller, et avec quelles règles.</summary>
    private void AppliquerVeille(MondeSimule monde)
    {
        var suivis = Depot.LireSuivisComplets();
        monde.DefinirVeille(suivis.Where(s => !s.Communaute).Select(s => s.Id), suivis.Where(s => s.Communaute).Select(s => s.Id), ReglesAlertes);
    }

    /// <summary>
    /// Reçoit les alertes du monde : elles seront gardées dans la base ; la mort d'un suivi dont on suit l'héritier fait passer le
    /// suivi à cet héritier (une alerte de plus le dit). Rend les alertes à montrer, dans l'ordre.
    /// </summary>
    public List<AlerteSuivi> RecevoirAlertes(List<AlerteSuivi> alertes)
    {
        var rendues = new List<AlerteSuivi>(alertes.Count);
        var suivis = alertes.Any(a => a.Sorte == SorteAlerte.Mort) ? Depot.LireSuivisComplets() : [];
        var change = false;
        foreach (var a in alertes)
        {
            rendues.Add(a);
            if (a.Sorte != SorteAlerte.Mort || suivis.FirstOrDefault(s => !s.Communaute && s.Id == a.HumainId) is not { Heritier: true } suivi) continue;
            var (_, heritier) = _monde.Proches(a.HumainId);
            if (heritier == 0 || suivis.Any(s => !s.Communaute && s.Id == heritier)) continue;
            var nom = _monde.NomHumain(heritier);
            Depot.ChangerSuivisComplets([new Suivi(false, heritier, nom, true)], [(false, a.HumainId)]);
            suivis.Remove(suivi);
            suivis.Add(new Suivi(false, heritier, nom, true));
            change = true;
            rendues.Add(new AlerteSuivi(a.Secondes, SorteAlerte.Mort, heritier, 0, 0,
                new Recit("alerte.heritier", ("h", suivi.Nom), ("e", nom)), false));
        }
        lock (_alertesAEnregistrer)
        {
            _alertesAEnregistrer.AddRange(rendues);
        }
        if (change) SuivisChanges();
        return rendues;
    }

    /// <summary>Garde dans la base les alertes reçues (appelé après la sauvegarde du monde).</summary>
    private void EnregistrerAlertesEnAttente()
    {
        List<AlerteSuivi> a;
        lock (_alertesAEnregistrer)
        {
            if (_alertesAEnregistrer.Count == 0) return;
            a = [.. _alertesAEnregistrer];
            _alertesAEnregistrer.Clear();
        }
        Depot.EnregistrerAlertes(a);
    }

    private bool AlertesEnAttente()
    {
        lock (_alertesAEnregistrer) return _alertesAEnregistrer.Count > 0;
    }

    /// <summary>Alertes non lues (celles pas encore gardées dans la base comprises).</summary>
    public int AlertesNonLues()
    {
        int enAttente;
        lock (_alertesAEnregistrer) enAttente = _alertesAEnregistrer.Count;
        return Depot.AlertesNonLues() + enAttente;
    }

    /// <summary>Les dernières alertes gardées (la base est d'abord mise à jour).</summary>
    public List<AlerteEnregistree> LireAlertes(int nombre, long avant)
    {
        if (AlertesEnAttente()) Sauvegarder();
        return Depot.LireAlertes(nombre, avant);
    }

    public void MarquerAlertesLues()
    {
        lock (_verrou)
        {
            if (AlertesEnAttente()) Sauvegarder();
            Depot.MarquerAlertesLues();
        }
    }
}
