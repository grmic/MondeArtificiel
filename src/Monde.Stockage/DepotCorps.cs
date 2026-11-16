using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Le corps (0.32.8) : blessure grave en train de guérir et séquelle de chacun ; pousses des camps.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Ce qu'on garde du corps d'un humain, en JSON dans la colonne humains.corps (null : rien à garder).</summary>
    /// <remarks>
    /// Depuis la 0.45.0 : où dort le tout-petit cette nuit et la nuit d'avant, la peur des bêtes et le lieu redouté.
    /// Depuis la 0.42.6 : corne de la plante (−1 : pas encore estimée), pieds meurtris, coup de soleil, piqûres, enduit de la peau
    /// (matière, jusqu'à quand, parure ou non) et chaussures portées (qualité, usure, recette).
    /// </remarks>
    private sealed record Corps(double Grave, OrigineBlessure Origine, Membre Membre, double Debut, double Fixe, long Le, OrigineBlessure Cause,
        double Champs = 0, double Cal = -1, double Pieds = 0, double Soleil = 0, double Piqures = 0, int Enduit = 0, long EnduitJusqua = 0,
        bool EnduitPare = false, double ChQualite = 0, double ChUsure = 0, string? ChPieces = null,
        int Couche = 0, int CoucheAvec = 0, int CouchePrec = 0, double PeurBetes = 0, long PeurBetesEvt = 0, int RedX = -1, int RedY = -1, long RedJusqua = 0, long CoucheNote = 0);

    /// <summary>Migration vers la version 45 : séquelles des blessures graves, pousses des camps. Les mondes existants n'en ont pas.</summary>
    private static void MigrerVersion45(SqliteConnection connexion, SqliteTransaction transaction)
    {
        if (!Colonnes(connexion, transaction, "humains").Contains("corps")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN corps TEXT;");
        if (!Colonnes(connexion, transaction, "ressources").Contains("rejets")) Executer(connexion, transaction, "ALTER TABLE ressources ADD COLUMN rejets BLOB;");
    }

    private static void LireCorps(SqliteCommand commande, List<Humain> humains)
    {
        commande.CommandText = "SELECT id, corps FROM humains WHERE corps IS NOT NULL ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var id = l.GetInt64(0);
            if (id < 1 || id > humains.Count || LireCorps(l.GetString(1)) is not { } c) continue;
            var h = humains[(int)id - 1];
            (h.BlessureGrave, h.BlessureOrigine) = (c.Grave, c.Origine);
            h.Sequelle = c.Membre == Membre.Aucun ? default : new Sequelle(c.Membre, c.Debut, c.Fixe, c.Le, c.Cause);
            h.PartDesChamps = Math.Clamp(c.Champs, 0, 1);
            h.Cal = c.Cal < 0 ? -1 : Math.Clamp(c.Cal, 0, 1);
            h.Pieds = Math.Clamp(c.Pieds, 0, 100);
            h.CoupDeSoleil = Math.Clamp(c.Soleil, 0, 100);
            h.Piqures = Math.Clamp(c.Piqures, 0, 100);
            h.Enduit = Enum.IsDefined((Matiere)c.Enduit) ? (Matiere)c.Enduit : default;
            h.EnduitJusqua = c.EnduitJusqua;
            h.EnduitPare = c.EnduitPare;
            h.Chaussures = c.ChPieces is { Length: > 0 } && Facons.LireObjet(c.ChPieces) is not null
                ? new Objet(Matiere.Facture, Math.Clamp(c.ChQualite, 0, 1), Math.Clamp(c.ChUsure, 0, 1), Pieces: c.ChPieces) : null;
            h.Coucher = Enum.IsDefined((Coucher)c.Couche) ? (Coucher)c.Couche : Coucher.Aucune;
            h.CoucheAvecId = c.CoucheAvec;
            h.CoucherPrecedent = Enum.IsDefined((Coucher)c.CouchePrec) ? (Coucher)c.CouchePrec : Coucher.Aucune;
            h.PeurDesBetes = double.IsFinite(c.PeurBetes) ? Math.Clamp(c.PeurBetes, 0, 100) : 0;
            h.PeurDesBetesEvenementId = c.PeurBetesEvt;
            (h.RedouteX, h.RedouteY, h.RedouteJusqua) = (c.RedX, c.RedY, c.RedJusqua);
            h.CoucherNoteLe = c.CoucheNote;
        }
    }

    /// <summary>Relit le corps enregistré ; null s'il est illisible ou hors des valeurs connues.</summary>
    private static Corps? LireCorps(string json)
    {
        try
        {
            var c = JsonSerializer.Deserialize<Corps>(json);
            return c is not null && Enum.IsDefined(c.Origine) && Enum.IsDefined(c.Membre) && Enum.IsDefined(c.Cause)
                   && double.IsFinite(c.Grave) && double.IsFinite(c.Debut) && double.IsFinite(c.Fixe) && double.IsFinite(c.Champs)
                   && double.IsFinite(c.Cal) && double.IsFinite(c.Pieds) && double.IsFinite(c.Soleil) && double.IsFinite(c.Piqures)
                   && double.IsFinite(c.ChQualite) && double.IsFinite(c.ChUsure) ? c : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void EnregistrerCorps(SqliteConnection connexion, SqliteTransaction transaction, Ecritures ecritures, IReadOnlyList<Humain> humains)
    {
        using var c = new ParHumain(connexion, transaction, ecritures, "humains.corps",
            Preparer(connexion, transaction, "UPDATE humains SET corps = $c WHERE id = $id;", "$c", "$id"), suitLaLigne: true);
        foreach (var h in humains)
        {
            // Rien à dire du corps : la colonne reste vide (une ligne remplacée l'a déjà vidée ; sinon on la vide, si elle a changé).
            c.Debut(h.Id);
            var s = h.Sequelle;
            var ch = h.Chaussures;
            var rien = h.BlessureGrave <= 0 && s.Membre == Membre.Aucun && h.PartDesChamps <= 0 && h.Cal < 0 && h.Pieds <= 0 && h.CoupDeSoleil <= 0
                       && h.Piqures <= 0 && h.Enduit == Matiere.Aucune && ch is null && h.Coucher == Coucher.Aucune && h.CoucherPrecedent == Coucher.Aucune
                       && h.PeurDesBetes <= 0 && h.PeurDesBetesEvenementId == 0 && h.RedouteJusqua == 0 && h.CoucherNoteLe == 0;
            c.Ligne(rien ? DBNull.Value : JsonSerializer.Serialize(new Corps(h.BlessureGrave, h.BlessureOrigine, s.Membre, s.Debut, s.Fixe, s.Le, s.Origine,
                h.PartDesChamps, h.Cal, h.Pieds, h.CoupDeSoleil, h.Piqures, (int)h.Enduit, h.EnduitJusqua, h.EnduitPare, ch?.Qualite ?? 0, ch?.Usure ?? 0,
                ch?.Pieces, (int)h.Coucher, h.CoucheAvecId, (int)h.CoucherPrecedent, h.PeurDesBetes, h.PeurDesBetesEvenementId, h.RedouteX, h.RedouteY,
                h.RedouteJusqua, h.CoucherNoteLe)), h.Id);
            c.Fin();
        }
    }
}
