using System.Diagnostics;
using System.Text.Json.Nodes;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;

// curl 'https://api.myanimelist.net/v2/anime?q=dandadan' -H 'X-MAL-Client-ID: 6114d00ca681b7701d1e15fe11a4987e'

class AnimeResult
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int ID { get; set; }
    public int NumEpisodes { get; set; }
}

public class Program
{
    private const string megaplaySource = "https://megaplay.buzz/";
    private const string malAPI = "https://api.myanimelist.net/v2/";
    private const string clientKey = "6114d00ca681b7701d1e15fe11a4987e";
    private static HttpClient sharedClient = new();

    private static async Task<String> BuildAndSendRequest(
        string path,
        Dictionary<string, string?>? headers
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                request.Headers.Add(key, value);
            }
        }

        using var response = await sharedClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync();

        return content;
    }

    private static async Task<HtmlDocument> GetHtml(string path)
    {
        using HttpResponseMessage response = await sharedClient.GetAsync(path);
        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync();

        HtmlDocument doc = new();
        doc.LoadHtml(content);

        return doc;
    }

    private static async Task<List<AnimeResult>> MakeSearchQuery(string title)
    {
        List<AnimeResult> animeList = new();
        var queryParams = new Dictionary<string, string?> { { "q", title } };
        string fullUrl = QueryHelpers.AddQueryString($"{malAPI}anime", queryParams);

        var headers = new Dictionary<string, string?> { { "X-MAL-Client-ID", clientKey } };
        var jsonString = await BuildAndSendRequest(fullUrl, headers);
        JsonNode rootNode = JsonNode.Parse(jsonString)!;

        JsonArray dataArray = rootNode["data"]!.AsArray();

        foreach (JsonNode? item in dataArray)
        {
            string animeTitle = item?["node"]?["title"]?.GetValue<string>()!;
            int animeID = item!["node"]!["id"]!.GetValue<int>();
            animeList.Add(new AnimeResult { Name = animeTitle.Trim(), ID = animeID });
        }

        return animeList;
    }

    private static async Task SetNumEpisodes(AnimeResult anime)
    {
        var queryParams = new Dictionary<string, string?> { { "fields", "num_episodes" } };
        string fullUrl = QueryHelpers.AddQueryString($"{malAPI}anime/{anime.ID}", queryParams);
        // Console.WriteLine($"Request: {fullUrl}");

        var headers = new Dictionary<string, string?> { { "X-MAL-Client-ID", clientKey } };
        var jsonString = await BuildAndSendRequest(fullUrl, headers);
        JsonNode rootNode = JsonNode.Parse(jsonString)!;

        anime.NumEpisodes = rootNode["num_episodes"]!.GetValue<int>();
    }

    private static async Task<String> GetSources(AnimeResult anime, int episode)
    {
        var fullUrl = $"{megaplaySource}stream/mal/{anime.ID}/{episode}/sub";

        // request.Headers.Add("user_agent", megaplaySource);
        var headers = new Dictionary<string, string?> { { "Referer", megaplaySource } };
        var htmlString = await BuildAndSendRequest(fullUrl, headers);

        HtmlDocument doc = new HtmlDocument();
        doc.LoadHtml(htmlString);

        // Scrape Megaplay html for data-id
        var id = doc
            .DocumentNode.SelectSingleNode("//div[@id='megaplay-player']")
            .GetAttributeValue("data-id", "");

        // Download sources
        fullUrl = $"{megaplaySource}stream/getSources?id={id}";
        string jsonString = await BuildAndSendRequest(fullUrl, headers);
        JsonNode rootNode = JsonNode.Parse(jsonString)!;
        // Console.WriteLine(rootNode);

        string fileSource = rootNode["sources"]!["file"]!.ToString();
        return fileSource;
    }

    private static async Task PlayEpisode(string path)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = "vlc";
        startInfo.Arguments = $"--http-referrer={megaplaySource} \"{path}\"";

        Process.Start(startInfo);
    }

    public static async Task Main(string[] args)
    {
        List<AnimeResult> animeList = new();
        bool valid = false;

        while (animeList.Count == 0)
        {
            string? title = "";

            while (!valid)
            {
                Console.Write("Please enter a title: ");
                title = Console.ReadLine()?.Trim();

                if (title == "")
                    Console.WriteLine("Invalid title. Please try again.");
                else
                    valid = true;
            }

            animeList = await MakeSearchQuery(title!);

            if (animeList.Count == 0)
            {
                Console.WriteLine("No results found. Searching again...");
                valid = false;
            }
        }

        for (int i = 0; i < animeList.Count; i++)
        {
            var anime = animeList[i];
            Console.WriteLine($"{i}) {anime.Name}");
        }

        Console.WriteLine("Select an anime: ");
        int index = -100;
        valid = false;
        while (!valid)
        {
            string? input = Console.ReadLine()?.Trim();
            int.TryParse(input, out index);

            if (index < 0 || index > animeList.Count - 1)
            {
                Console.WriteLine("Invalid input. Please try again.");
            }
            else
                valid = true;
        }

        await SetNumEpisodes(animeList[index]);

        valid = false;
        int episode = -1;
        while (!valid)
        {
            Console.WriteLine($"Please select an episode ({animeList[index].NumEpisodes}): ");
            string ep = Console.ReadLine()!;

            int.TryParse(ep, out episode);

            if (episode < 1 || episode > animeList[index].NumEpisodes)
            {
                Console.WriteLine("Invalid episode number. Try again.");
            }
            else
                valid = true;
        }

        string source = await GetSources(animeList[index], episode);
        Console.WriteLine(source);

        // Launch app
        await PlayEpisode(source);
    }
}
