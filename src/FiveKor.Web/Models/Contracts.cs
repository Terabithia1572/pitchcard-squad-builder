using System.Text.Json;

namespace FiveKor.Web.Models;

public sealed record SaveStateRequest(JsonElement State, int Revision);
public sealed record GalleryQuery(int Year = 27, int Page = 1, string Keyword = "");
public sealed record ImportTemplateRequest(string Id, int Year, int Page, string Keyword);
