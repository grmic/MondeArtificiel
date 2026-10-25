using Monde.Simulation.Monde;
using Monde.Simulation.Nature;

namespace Monde.Serveur;

/// <summary>
/// Réglages demandés par le navigateur pour un nouveau monde (taille en cases ; graine absente : au hasard ; écart : mer entre
/// les continents, 0 proches, 1 éloignés, 2 mélangés). Un monde neuf a toujours la carte de la dernière génération.
/// </summary>
/// <param name="GarderAncien">À la création d'un monde : garder une copie de l'ancien (vrai par défaut).</param>
public sealed record DemandeConfiguration(long? Graine, int Couples, int Largeur, int Hauteur, int Continents, int Placement = 1, int Ecart = 0,
    bool GarderAncien = true)
{
    /// <summary>Réglages validés ; la graine absente est tirée au hasard.</summary>
    /// <exception cref="ArgumentException">Graine ou réglages hors limites (message lisible).</exception>
    public ConfigurationMonde Valider()
    {
        if (Graine is < 0 or > uint.MaxValue) throw new ArgumentException(L.T("erreur.graine"));
        var graine = Graine is { } g ? (uint)g : (uint)Random.Shared.NextInt64(0, (long)uint.MaxValue + 1);
        var configuration = new ConfigurationMonde(graine, Couples, Largeur, Hauteur, Continents, Placement,
            ConfigurationMonde.GenerationActuelle, (EcartContinents)Ecart);
        return Lib.Erreur(configuration) is { } erreur ? throw new ArgumentException(erreur) : configuration;
    }
}

/// <summary>Réglages du monde courant et limites acceptées, pour la fenêtre « Configuration ».</summary>
public sealed record ConfigurationDto(
    uint Graine, int Couples, int Largeur, int Hauteur, int Continents, int Placement, int Ecart, int Generation, bool Origine, double KilometresParCase,
    int CouplesMin, int CouplesMax, int TailleMin, int TailleMax, int ContinentsMin, int ContinentsMax, string Resume,
    string Version, string Auteur, bool JournalQuotidien, int Coeurs, int CoeursMachine, int Clemence)
{
    private static readonly System.Reflection.Assembly Application = typeof(ConfigurationDto).Assembly;

    /// <summary>Version de l'application (sans l'empreinte de commit ajoutée par le SDK).</summary>
    public static string VersionApplication { get; } =
        (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(Application)
            ?.InformationalVersion ?? "?").Split('+')[0];

    public static string AuteurApplication { get; } =
        System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyCompanyAttribute>(Application)?.Company ?? "";

    public static ConfigurationDto Depuis(ServiceMonde service)
    {
        var monde = service.Monde;
        var c = monde.Configuration;
        return new((uint)c.Graine, c.Couples, c.Largeur, c.Hauteur, c.Continents, c.Placement, (int)c.Ecart, c.Generation, c.EstOrigine,
            Echelle.KilometresParCase, ConfigurationMonde.CouplesMin, ConfigurationMonde.CouplesMax,
            ConfigurationMonde.TailleMin, ConfigurationMonde.TailleMax, ConfigurationMonde.ContinentsMin, ConfigurationMonde.ContinentsMax,
            Resumer(c, monde.Carte), VersionApplication, AuteurApplication, service.JournalQuotidien,
            service.Coeurs, ServiceMonde.CoeursMachine, service.Clemence);
    }

    /// <summary>Résumé d'une carte : dimensions, continents et îles, en mots.</summary>
    public static string Resumer(ConfigurationMonde c, Carte carte)
    {
        var (continents, iles) = Geographie.MassesDeTerre(carte);
        var surfaces = string.Join(", ", continents.Select(cases => L.Surface(Echelle.KilometresCarres(cases))));
        var ilesTexte = iles.Count == 0 ? "" : L.T("config.resume.iles", ("n", iles.Count));
        return L.T("config.resume", ("largeur", L.Distance(Echelle.Kilometres(c.Largeur))), ("hauteur", L.Distance(Echelle.Kilometres(c.Hauteur))),
            ("continents", L.T("config.nContinents", ("n", continents.Count))), ("surfaces", surfaces), ("iles", ilesTexte),
            ("couples", L.T("config.resume.couples", ("n", c.Couples))), ("humains", c.Couples * 2), ("placement", Lib.Placement(c)))
            + Passages(carte);
    }

    /// <summary>
    /// Mer la plus étroite entre deux continents, quand il y en a plusieurs : c'est elle qui décide si les peuples d'un
    /// continent verront un jour l'autre rive.
    /// </summary>
    private static string Passages(Carte carte)
    {
        var m = carte.Mer;
        var entre = m.Passages.Where(p => m.Continents.Contains(p.MasseA) && m.Continents.Contains(p.MasseB)).Select(p => p.Km).ToList();
        if (m.Continents.Count < 2) return "";
        return " " + (entre.Count == 0 ? L.T("config.resume.merLarge", ("km", L.Distance(Mer.DistanceMaxPassageKm)))
            : L.T("config.resume.passage", ("km", L.Distance(entre.Min()))));
    }
}

/// <summary>Résultat d'un aperçu : le résumé de la carte, ou la raison pour laquelle ces réglages sont impossibles.</summary>
public sealed record ApercuDto(bool Possible, string Texte);

/// <summary>
/// Une sauvegarde que l'on peut restaurer : son nom de fichier, la date du monde, quand elle a été faite, sa taille,
/// les humains vivants ; Possible : faux si elle vient d'une version plus récente ou est illisible.
/// </summary>
public sealed record SauvegardeDto(string Nom, string DateMonde, string FaiteLe, double Mo, int Vivants, uint Graine, bool Possible)
{
    public static List<SauvegardeDto> Toutes(ServiceMonde service) => service.FichiersRestaurables()
        .Select(f =>
        {
            var info = new FileInfo(f.Value);
            var resume = Stockage.DepotMonde.Decrire(f.Value);
            var possible = resume is not null && resume.Version <= Stockage.DepotMonde.VersionSchema;
            return (Ecrit: info.LastWriteTimeUtc, Dto: new SauvegardeDto(f.Key,
                resume is null ? L.T("config.illisibleMin") : L.Date(resume.Secondes),
                info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                Math.Round(info.Length / 1048576.0, 1), resume?.Vivants ?? 0, (uint)(resume?.Graine ?? 0), possible));
        })
        .OrderByDescending(x => x.Ecrit).Select(x => x.Dto).ToList();
}
