using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using Monde.Simulation;

namespace Monde.Stockage;

/// <summary>La neige au sol (0.45.26) : équivalent en eau et épaisseur de chaque case, en flottants petit-boutistes.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Migration vers la version 64 : neige au sol. Les mondes existants n'en ont pas encore.</summary>
    private static void MigrerVersion64(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS neige (
                id          INTEGER PRIMARY KEY CHECK (id = 1),
                eau         BLOB NOT NULL,
                epaisseur   BLOB NOT NULL
            );
            """);
    }

    private static EtatMonde LireNeige(SqliteCommand commande, EtatMonde etat)
    {
        commande.CommandText = "SELECT eau, epaisseur FROM neige WHERE id = 1;";
        using var lecteur = commande.ExecuteReader();
        if (!lecteur.Read()) return etat;
        var eau = VersFloat((byte[])lecteur[0]);
        var epaisseur = VersFloat((byte[])lecteur[1]);
        return eau.Length == epaisseur.Length ? etat with { NeigeEau = eau, NeigeEpaisseur = epaisseur } : etat;
    }

    private static void EnregistrerNeige(SqliteConnection connexion, SqliteTransaction transaction, EtatMonde etat)
    {
        Executer(connexion, transaction, "DELETE FROM neige;");
        if (etat.NeigeEau is not { } eau || etat.NeigeEpaisseur is not { } epaisseur || eau.Length != epaisseur.Length) return;
        using var c = Preparer(connexion, transaction, "INSERT INTO neige (id, eau, epaisseur) VALUES (1, $e, $p);", "$e", "$p");
        Lancer(c, VersOctets(eau), VersOctets(epaisseur));
    }

    private static byte[] VersOctets(float[] valeurs)
    {
        var octets = new byte[valeurs.Length * 4];
        for (var i = 0; i < valeurs.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(octets.AsSpan(i * 4), valeurs[i]);
        return octets;
    }

    private static float[] VersFloat(byte[] octets)
    {
        if (octets.Length % 4 != 0) throw new InvalidDataException("Neige corrompue dans la sauvegarde.");
        var valeurs = new float[octets.Length / 4];
        for (var i = 0; i < valeurs.Length; i++) valeurs[i] = BinaryPrimitives.ReadSingleLittleEndian(octets.AsSpan(i * 4));
        return valeurs;
    }
}
