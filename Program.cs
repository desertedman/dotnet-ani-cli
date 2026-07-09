using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;

class AnimeResult
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int ID { get; set; }
    public int NumEpisodes { get; set; }
}

class Track
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("default")]
    public bool Default { get; set; }
}

class Source
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";
}

class FileSource
{
    [JsonPropertyName("sources")]
    public Source? Source { get; set; }

    [JsonPropertyName("tracks")]
    public List<Track>? trackList { get; set; }
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

    private static async Task<FileSource> GetSources(AnimeResult anime, int episode)
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

        // Deserialize json
        FileSource fileSource = rootNode.Deserialize<FileSource>()!;
        // foreach (var track in fileSource.subTracks!)
        // {
        //     Console.WriteLine(track.Label);
        // }

        return fileSource;
    }

    private static async Task PlayEpisode(string path, Track track)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = "mpv";
        // startInfo.Arguments = $"--http-referrer={megaplaySource} \"{path}\"";
        if (track != null)
        {
            startInfo.Arguments =
                $"--referrer={megaplaySource} \"{path}\" --sub-file=\"{track.File}\"";
        }
        else
        {
            startInfo.Arguments = $"--referrer={megaplaySource} \"{path}\"";
        }

        Process process = Process.Start(startInfo)!;
        process.WaitForExit();
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
            // Offset index to make entries appear starting from 1
            Console.WriteLine($"{i + 1}) {anime.Name}");
        }

        Console.Write("Select an anime: ");
        int index = -100;
        valid = false;
        while (!valid)
        {
            string? input = Console.ReadLine()?.Trim();
            int.TryParse(input, out index);

            if (index < 1 || index > animeList.Count)
            {
                Console.WriteLine("Invalid input. Please try again.");
            }
            else
                valid = true;
        }
        // Must offset index to correct for ofsetted display
        index--;

        await SetNumEpisodes(animeList[index]);

        while (true)
        {
            valid = false;
            int episode = -1;
            while (!valid)
            {
                Console.Write($"Select an episode ({animeList[index].NumEpisodes}): ");
                string ep = Console.ReadLine()!;

                int.TryParse(ep, out episode);

                // if (episode < 1 || episode > animeList[index].NumEpisodes)
                // {
                //     Console.WriteLine("Invalid episode number. Try again.");
                // }
                // else
                valid = true;
            }

            var fileSource = await GetSources(animeList[index], episode);
            // for (int i = 0; i < fileSource.trackList!.Count; i++)
            // {
            //     var track = fileSource.trackList[i];
            //     Console.WriteLine($"{i + 1}) {track.Label}");
            // }

            // Console.Write("Select a track: ");

            // Select default track
            Track defaultTrack = null!;
            if (fileSource.trackList!.Count > 0)
            {
                foreach (var track in fileSource.trackList!)
                {
                    if (track.Default == true)
                    {
                        defaultTrack = track;
                    }
                }
                Console.WriteLine(defaultTrack.Label);
            }
            else
                Console.WriteLine("No subtitle tracks found.");

            // Launch app
            await PlayEpisode(fileSource.Source!.File, defaultTrack);
        }
    }
}
