namespace OpenXRChanger;

// Exact damaged strings shipped in the local 0.1.8 test package only.
internal static class LegacyTranslations
{
    public static bool IsDamaged(string language, string key, string value) => (language, key, value) switch
    {
        ("de", "helperExplanation", "Optional: einen kleinen Windows-Dienst einmal mit Administrator-Best?tigung einrichten. Er erlaubt diesem Windows-Benutzer, lokale x64-OpenXR-Runtimes ohne weitere UAC-Abfrage auszuw?hlen. Auch bei der portablen App wird der Helper separat im System installiert. Vor dem L?schen des App-Ordners den Helper hier oder ?ber Windows Apps deinstallieren.") => true,
        ("de", "helperUninstall", "Helper deinstallieren?") => true,
        ("de", "helperRemoved", "Helper entfernt. Die aktive Runtime bleibt erhalten. Ohne Administrator-Modus ben?tigen Runtime-Wechsel wieder eine UAC-Best?tigung.") => true,
        ("fr", "helperExplanation", "Facultatif : installer un petit service Windows avec une confirmation administrateur. Il permet ? cet utilisateur Windows de s?lectionner des runtimes OpenXR x64 locaux sans nouvelle demande UAC. M?me avec l?application portable, le service est install? s?par?ment sur le syst?me. Avant de supprimer le dossier de l?application, d?sinstallez le service ici ou via les applications Windows.") => true,
        ("fr", "helperUninstall", "D?sinstaller le service?") => true,
        ("fr", "helperRemoved", "Service supprim?. Le runtime actif est conserv?. Les changements n?cessitent ? nouveau une confirmation UAC, sauf si l?application est ex?cut?e en tant qu?administrateur.") => true,
        ("fr", "helperRemoveFailed", "D?sinstallation annul?e ou ?chou?e. Si n?cessaire, r?parez l?installation et r?essayez.") => true,
        _ => false
    };
}
