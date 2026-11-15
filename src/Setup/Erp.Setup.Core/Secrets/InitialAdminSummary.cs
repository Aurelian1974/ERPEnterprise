using System.Text;

namespace Erp.Setup.Core.Secrets;

/// <summary>
/// Datele de acces afisate o singura data la finalul instalarii (8.8, I-24). Singura afisare permisa a unei parole (R6):
/// textul merge doar pe ecran sau in initial-admin.txt, niciodata in jurnal.
/// </summary>
public static class InitialAdminSummary
{
    private const string Rule = "==================================================================";

    public static string Render(string applicationUrl, string userName, string? temporaryPassword)
    {
        var text = new StringBuilder();
        text.AppendLine(Rule);
        text.AppendLine(" Instalarea s-a încheiat.");
        text.AppendLine($" Adresa aplicației:   {applicationUrl}");
        text.AppendLine($" Utilizator:          {userName}");
        if (temporaryPassword is null)
        {
            text.AppendLine(" Administratorul există deja; parola lui nu se mai afișează.");
            text.AppendLine(" Dacă nu mai aveți acces, folosiți ErpSetup.exe reset-admin.");
        }
        else
        {
            text.AppendLine($" Parolă temporară:    {temporaryPassword}   (afișată O SINGURĂ DATĂ)");
            text.AppendLine();
            text.AppendLine(" La prima autentificare vi se cere o parolă nouă și activarea 2FA,");
            text.AppendLine(" apoi porniți asistentul de configurare (firme, AD, SMTP, ANAF).");
        }

        text.AppendLine(Rule);
        return text.ToString();
    }
}
