using System.Diagnostics;

namespace SubtitleAligner;

internal static class DonationService
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config", "donation.txt");

    public static bool TryGetDonationUri(out Uri? uri)
    {
        uri = null;
        try
        {
            if (!File.Exists(ConfigPath)) return false;
            string? value = File.ReadLines(ConfigPath)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#'));
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate)) return false;
            if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            string host = candidate.Host.TrimEnd('.');
            if (!string.Equals(host, "paypal.me", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(host, "www.paypal.me", StringComparison.OrdinalIgnoreCase)) return false;
            uri = candidate;
            return true;
        }
        catch { return false; }
    }

    public static void Open(Form owner)
    {
        if (!TryGetDonationUri(out Uri? uri) || uri is null)
        {
            MessageBox.Show(owner,
                Localization.T("PayPal.Me link još nije podešen. Dodajte ga u config\\donation.txt pre konačnog izdanja."),
                Localization.T("Podrži razvoj"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, Localization.T("Stranica za donaciju nije mogla da se otvori.") + "\n\n" + ex.Message,
                Localization.T("Podrži razvoj"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
