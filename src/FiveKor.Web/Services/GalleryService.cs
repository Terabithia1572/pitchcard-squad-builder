using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace FiveKor.Web.Services;

public sealed record TemplateItem(string id, string name, string author, string? image, string color,
    int year, int page, string keyword, string layout, string source);
public sealed record GalleryPage(List<TemplateItem> items, int total, int page, int pages, int year);

public sealed class GalleryService(HttpClient http)
{
    static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase) {
        "www.fifarosters.com", "fifarosters.com", "i.imgur.com", "i.ibb.co", "i.postimg.cc", "images2.imgbox.com", "images.imgbox.com"
    };
    static Uri CheckedUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 ||
            !Hosts.Contains(uri.Host) || uri.UserInfo.Length != 0) throw new ArgumentException("Şablon kaynağı desteklenmiyor.");
        return uri;
    }
    public async Task<byte[]> DownloadAsync(string url, int maxBytes, CancellationToken ct)
    {
        var uri = CheckedUri(url);
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (redirects == 3 || response.Headers.Location is null) throw new ArgumentException("Yönlendirme sınırı aşıldı.");
                uri = CheckedUri(new Uri(uri, response.Headers.Location).AbsoluteUri);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maxBytes) throw new ArgumentException("Kaynak dosya çok büyük.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[32768];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, ct);
                if (read == 0) break;
                if (buffer.Length + read > maxBytes) throw new ArgumentException("Kaynak dosya çok büyük.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        throw new ArgumentException("Yönlendirme sınırı aşıldı.");
    }
    static string Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
    static string Clean(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")).Trim();
    public async Task<GalleryPage> GetAsync(int year, int page, string keyword, CancellationToken ct)
    {
        if (year is < 18 or > 27 || page is < 1 or > 10000 || keyword.Length > 60) throw new ArgumentException("Galeri filtresi geçersiz.");
        var url = $"https://www.fifarosters.com/card-designer-gallery?year={year}&pageNum={page}&keyword={Uri.EscapeDataString(keyword)}";
        var html = System.Text.Encoding.UTF8.GetString(await DownloadAsync(url, 2_000_000, ct));
        var totalText = Match(html, @"Showing[\s\S]{0,100}?of\s+([\d,]+)\s+cards").Replace(",", "");
        var total = int.TryParse(totalText, out var n) ? n : 0;
        var items = new List<TemplateItem>();
        foreach (var block in html.Split(new[] { "<div class=\"user_card_design \"" }, StringSplitOptions.None).Skip(1))
        {
            var id = Match(block, "design-id=\"(\\d+)\"");
            if (id == "") continue;
            var rule = Match(html, @"\.design-" + id + @" \.playercard \{([^}]+)\}");
            var candidate = Match(rule, "background-image:\\s*url\\(['\"\\s]*([^)'\"\\s]+)");
            string? image = null;
            try { image = CheckedUri(candidate).AbsoluteUri; } catch (ArgumentException) { }
            var color = Match(html, @"\.design-" + id + @" \.playercard-name \{[^}]*color:\s*(#[a-fA-F0-9]{6})");
            if (color == "") color = "#fff0ba";
            var title = Clean(Match(block, "class=\"design-title\">([\\s\\S]*?)</a>"));
            var author = Clean(Match(block, "<div>by <a[^>]*>([\\s\\S]*?)</a>"));
            items.Add(new(id, title.Length > 100 ? title[..100] : title, author.Length > 80 ? author[..80] : author,
                image, color, year, page, keyword, year >= 24 ? "modern" : "classic",
                "https://www.fifarosters.com/card-designer?design_id=" + id));
        }
        return new(items, total, page, Math.Max(1, (int)Math.Ceiling(total / 25.0)), year);
    }
    public static string DetectType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 137, 80, 78, 71 })) return "image/png";
        if (bytes.Length >= 3 && bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (bytes.Length >= 8 && bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8)) return "video/mp4";
        if (bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 26, 69, 223, 163 })) return "video/webm";
        return "";
    }
}
