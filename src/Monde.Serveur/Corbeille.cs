using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace Monde.Serveur;

/// <summary>
/// Met un fichier à la corbeille du système plutôt que de l'effacer : on peut l'y reprendre tant qu'elle n'est pas vidée.
/// macOS : la Corbeille de l'utilisateur (~/.Trash), par le Finder si le déplacement direct est refusé ;
/// Windows : la Corbeille ; Linux et autres : la corbeille freedesktop (~/.local/share/Trash), celle de GNOME, KDE, etc.
/// </summary>
public static class Corbeille
{
    /// <summary>Met <paramref name="chemin"/> à la corbeille ; rend le nom de la corbeille. Lève une exception si c'est impossible (rien n'est effacé).</summary>
    /// <exception cref="IOException">Le fichier n'a pas pu être mis à la corbeille ; il est toujours là.</exception>
    public static string Jeter(string chemin)
    {
        if (!File.Exists(chemin)) throw new FileNotFoundException("Ce fichier n'existe plus.", chemin);
        if (OperatingSystem.IsWindows())
        {
            JeterWindows(chemin);
            return "la Corbeille";
        }
        if (OperatingSystem.IsMacOS())
        {
            JeterMac(chemin);
            return "la Corbeille";
        }
        JeterFreedesktop(chemin);
        return "la corbeille";
    }

    [SupportedOSPlatform("windows")]
    private static void JeterWindows(string chemin)
    {
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(chemin,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        if (File.Exists(chemin)) throw new CorbeilleRefusee(L.T("corbeille.windows"));
    }

    private static void JeterMac(string chemin)
    {
        var corbeille = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        try
        {
            File.Move(chemin, Libre(corbeille, Path.GetFileName(chemin)));
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Déplacement direct refusé : on demande au Finder (macOS peut alors demander d'autoriser le serveur à piloter le Finder).
        }
        var info = new ProcessStartInfo("osascript") { RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-e");
        info.ArgumentList.Add($"tell application \"Finder\" to delete (POSIX file \"{chemin.Replace("\\", "\\\\").Replace("\"", "\\\"")}\")");
        using var p = Process.Start(info) ?? throw new CorbeilleRefusee(L.T("corbeille.finder"));
        if (!p.WaitForExit(30_000)) p.Kill();
        if (File.Exists(chemin))
        {
            throw new CorbeilleRefusee(L.T("corbeille.macos"));
        }
    }

    private static void JeterFreedesktop(string chemin)
    {
        var donnees = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(donnees)) donnees = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var fichiers = Path.Combine(donnees, "Trash", "files");
        var infos = Path.Combine(donnees, "Trash", "info");
        Directory.CreateDirectory(fichiers);
        Directory.CreateDirectory(infos);
        var cible = Libre(fichiers, Path.GetFileName(chemin), infos);
        var nom = Path.GetFileName(cible);
        // Le fichier .trashinfo d'abord, comme le veut la spécification : il réserve le nom.
        var absolu = Path.GetFullPath(chemin);
        File.WriteAllText(Path.Combine(infos, nom + ".trashinfo"),
            "[Trash Info]\nPath=" + string.Join("/", absolu.Split('/').Select(Uri.EscapeDataString))
            + "\nDeletionDate=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "\n");
        try
        {
            File.Move(chemin, cible);
        }
        catch
        {
            File.Delete(Path.Combine(infos, nom + ".trashinfo"));
            throw;
        }
    }

    /// <summary>Chemin libre dans <paramref name="dossier"/> pour <paramref name="nom"/> (« nom 2.db », « nom 3.db »… s'il est pris).</summary>
    private static string Libre(string dossier, string nom, string? infos = null)
    {
        bool Pris(string n) => File.Exists(Path.Combine(dossier, n)) || Directory.Exists(Path.Combine(dossier, n))
                               || (infos is not null && File.Exists(Path.Combine(infos, n + ".trashinfo")));
        var candidat = nom;
        for (var k = 2; Pris(candidat); k++) candidat = $"{Path.GetFileNameWithoutExtension(nom)} {k}{Path.GetExtension(nom)}";
        return Path.Combine(dossier, candidat);
    }
}

/// <summary>Le système a refusé de mettre le fichier à la corbeille ; le message, sans chemin, peut être montré tel quel.</summary>
public sealed class CorbeilleRefusee(string message) : IOException(message);
