using System.Text.Json;
using FiveKor.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FiveKor.Web.Services;

public sealed class StateService(AppDbContext db, IWebHostEnvironment environment)
{
    public async Task<(JsonElement state, int revision)> GetAsync(CancellationToken ct)
    {
        var row = await db.States.AsNoTracking().SingleAsync(x => x.Id == "main", ct);
        return (JsonDocument.Parse(row.Payload).RootElement.Clone(), row.Revision);
    }

    public async Task<bool> SaveAsync(JsonElement state, int revision, CancellationToken ct)
    {
        if (revision < 0) throw new ArgumentException("Geçersiz sürüm.");
        StateValidator.Validate(state, environment.WebRootPath);
        var count = await db.States.Where(x => x.Id == "main" && x.Revision == revision)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Payload, state.GetRawText())
                .SetProperty(x => x.Revision, x => x.Revision + 1), ct);
        return count == 1;
    }
}

public static class StateValidator
{
    static JsonElement Field(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    static string Text(JsonElement e, string name, int max, bool required = false)
    {
        var v = Field(e, name);
        if (v.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null && !required) return "";
        if (v.ValueKind != JsonValueKind.String || v.GetString()!.Length > max || required && string.IsNullOrWhiteSpace(v.GetString()))
            throw new ArgumentException($"{name} alanı geçersiz.");
        return v.GetString()!;
    }
    static int Int(JsonElement e, string name, int min, int max)
    {
        var v = Field(e, name);
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < min || n > max)
            throw new ArgumentException($"{name} aralığı geçersiz.");
        return n;
    }
    static bool Media(string s) => s == "" ||
        s.StartsWith("/assets/", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(s, @"^/assets/[a-zA-Z0-9_.-]+$") ||
        System.Text.RegularExpressions.Regex.IsMatch(s, @"^/media/[a-f0-9-]{36}$");
    static void CheckMedia(JsonElement e, string name)
    {
        if (!Media(Text(e, name, 100))) throw new ArgumentException($"{name} görseli geçersiz.");
    }
    static IEnumerable<JsonElement> Array(JsonElement e, string name, int min, int max)
    {
        var v = Field(e, name);
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < min || v.GetArrayLength() > max)
            throw new ArgumentException($"{name} listesi geçersiz.");
        return v.EnumerateArray();
    }
    static void OptionalNumber(JsonElement e, string name, double min, double max)
    {
        var v = Field(e, name);
        if (v.ValueKind == JsonValueKind.Undefined) return;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var n) || n < min || n > max)
            throw new ArgumentException($"{name} aralığı geçersiz.");
    }
    public static void Validate(JsonElement state, string webRoot)
    {
        if (state.ValueKind != JsonValueKind.Object || state.GetRawText().Length > 500_000)
            throw new ArgumentException("Site verisi geçersiz veya çok büyük.");
        var templates = new HashSet<string>();
        if (Field(state, "cardTemplates") is { ValueKind: JsonValueKind.Array })
            foreach (var t in Array(state, "cardTemplates", 0, 200))
            {
                var id = Text(t, "id", 80, true);
                Text(t, "name", 100, true); CheckMedia(t, "image");
                if (!templates.Add(id)) throw new ArgumentException("Şablon numarası tekrar ediyor.");
            }
        else if (Field(state, "cardTemplates").ValueKind != JsonValueKind.Undefined)
            throw new ArgumentException("Şablon listesi geçersiz.");

        var ids = new HashSet<string>();
        foreach (var p in Array(state, "players", 0, 100))
        {
            var id = Text(p, "id", 70, true);
            if (!ids.Add(id)) throw new ArgumentException("Tekrarlanan oyuncu numarası.");
            Text(p, "name", 60, true); Text(p, "position", 5, true);
            if (!new[] { "blue", "red", "bench" }.Contains(Text(p, "team", 10))) throw new ArgumentException("Takım geçersiz.");
            Int(p, "rating", 1, 99); Int(p, "change", -99, 99); Int(p, "order", 0, 99); CheckMedia(p, "photo");
            if (Field(p, "visible").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("Kart görünürlüğü geçersiz.");
            foreach (var s in Array(p, "stats", 6, 6)) if (s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out var n) || n < 0 || n > 99) throw new ArgumentException("Kart özellikleri geçersiz.");
            foreach (var tag in Array(p, "tags", 0, 4)) if (tag.ValueKind != JsonValueKind.String || tag.GetString()!.Length > 30) throw new ArgumentException("Etiket geçersiz.");
            var design = Text(p, "cardDesign", 20);
            if (design != "" && !new[] { "original", "blue", "red", "gold", "ice", "template" }.Contains(design)) throw new ArgumentException("Tasarım geçersiz.");
            if (design == "template")
            {
                var templateId = Text(p, "templateId", 80, true);
                if (!templates.Contains(templateId) && !System.IO.File.Exists(Path.Combine(webRoot, "assets", "template-" + templateId + ".webp")))
                    throw new ArgumentException("Şablon bulunamadı.");
            }
            foreach (var field in new[] { "clubImage", "leagueImage", "nationImage" }) CheckMedia(p, field);
            OptionalNumber(p, "photoScale", 50, 200); OptionalNumber(p, "photoX", -60, 60); OptionalNumber(p, "photoY", -60, 60);
            OptionalNumber(p, "nameY", 45, 75); OptionalNumber(p, "statsY", 60, 82); OptionalNumber(p, "badgesY", 78, 90);
        }
        var settings = Field(state, "settings");
        if (settings.ValueKind != JsonValueKind.Object) throw new ArgumentException("Maç ayarları eksik.");
        CheckMedia(settings, "logo");
        foreach (var field in new[] { "league", "title", "season", "matchday", "venue", "time", "surface", "format", "duration", "matchType", "blueName", "redName", "blueFormation", "redFormation", "result", "mvpNote", "footerName", "footerSlogan" }) Text(settings, field, 150, true);
        Text(settings, "quote", 400);
        foreach (var field in new[] { "bluePower", "redPower", "blueScore", "redScore", "mvpGoals", "mvpAssists" }) Int(settings, field, 0, 9999);
        foreach (var st in Array(state, "stars", 3, 3))
        {
            var id = Text(st, "id", 10, true);
            if (!new[] { "goal", "assist", "save" }.Contains(id)) throw new ArgumentException("Haftanın videosu geçersiz.");
            Text(st, "title", 60); Text(st, "label", 60);
            var player = Text(st, "playerId", 70);
            if (player != "" && !ids.Contains(player)) throw new ArgumentException("Yıldız oyuncu bulunamadı.");
            CheckMedia(st, "video"); CheckMedia(st, "poster");
        }
        var squads = Field(state, "squads");
        if (squads.ValueKind != JsonValueKind.Object) throw new ArgumentException("Kadrolar eksik.");
        var assigned = new HashSet<string>();
        foreach (var team in new[] { "blue", "red" })
        {
            var squad = Field(squads, team);
            var formation = Text(squad, "formation", 20, true);
            var count = new[] { "7-classic", "7-231", "7-321", "7-222", "5-211", "5-121", "6-221", "6-212", "8-331" }.Contains(formation)
                ? int.Parse(formation.Split('-')[0]) : 0;
            if (count == 0) throw new ArgumentException("Diziliş geçersiz.");
            foreach (var slot in Array(squad, "slots", count, count))
                if (slot.ValueKind != JsonValueKind.Null && (slot.ValueKind != JsonValueKind.String || !ids.Contains(slot.GetString()!) || !assigned.Add(slot.GetString()!)))
                    throw new ArgumentException("Kadroda geçersiz veya tekrarlanan oyuncu var.");
        }
    }
}
