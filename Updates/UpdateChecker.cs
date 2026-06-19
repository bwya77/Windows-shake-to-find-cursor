using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ShakeToBigCursor.Updates;

internal sealed class UpdateChecker
{
    private const string Owner = "bwya77";
    private const string Repo = "Windows-shake-to-find-cursor";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public sealed record UpdateInfo(Version Latest, string Tag, string HtmlUrl, string? InstallerUrl);

    public static Version CurrentVersion
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return ParseVersion(info) ?? Normalize(asm.GetName().Version ?? new Version(0, 1, 0));
        }
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("WindowsShakeToFindCursor-UpdateChecker");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
            var htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(htmlUrl))
            {
                htmlUrl = $"https://github.com/{Owner}/{Repo}/releases/latest";
            }

            var latest = ParseVersion(tag);
            if (latest == null || latest <= CurrentVersion)
            {
                return null;
            }

            string? installerUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                var arch = ArchToken();
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                    if (name.StartsWith("WindowsShakeToFindCursorSetup", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith($"win-{arch}.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() : null;
                        break;
                    }
                }
            }

            return new UpdateInfo(latest, tag, htmlUrl, installerUrl);
        }
        catch
        {
            return null;
        }
    }

    private static string ArchToken() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64",
    };

    internal static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var s = value.Trim();
        var start = 0;
        while (start < s.Length && !char.IsDigit(s[start]))
        {
            start++;
        }

        s = s[start..];
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.'))
        {
            end++;
        }

        s = s[..end];
        if (s.Length == 0)
        {
            return null;
        }

        if (!s.Contains('.'))
        {
            s += ".0";
        }

        return Version.TryParse(s, out var version) ? Normalize(version) : null;
    }

    private static Version Normalize(Version version)
    {
        return new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
    }
}
