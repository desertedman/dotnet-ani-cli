using System.Diagnostics;
using System.Net;
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

    public override String ToString()
    {
        return Name;
    }
}

// Recreate JSON structure of megaplay
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

    public override String ToString()
    {
        string retStr = Label;

        if (Default)
        {
            retStr += " (Default)";
        }

        return retStr;
    }
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

struct Info
{
    public string OS { get; set; }
    public string Player { get; set; }
}

public class Program
{
    private const string megaplaySource = "https://megaplay.buzz/";
    private const string malAPI = "https://api.myanimelist.net/v2/";
    private const string malBase = "https://myanimelist.net/";
    private const string clientKey = "6114d00ca681b7701d1e15fe11a4987e";
    private static HttpClient sharedClient = new();
    private static Info Info;

    private static void Configure()
    {
        if (OperatingSystem.IsLinux())
        {
            Info.OS = "Linux";

            if (File.Exists("mpv"))
            {
                Info.Player = "mpv";
            }
            else if (File.Exists("vlc"))
            {
                Info.Player = "vlc";
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            Info.OS = "Windows";

            if (File.Exists("mpv"))
            {
                Info.Player = "mpv";
            }
            else if (File.Exists("vlc"))
            {
                Info.Player = "vlc";
            }
        }
    }

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

    private static async Task<List<String>> GetEpisodes(AnimeResult anime)
    {
        string fullUrl = $"{malBase}anime/{anime.ID}/";
        List<String> episodeList = new();
        await SetNumEpisodes(anime);
        episodeList.Capacity = anime.NumEpisodes;

        // Scrape MAL website for episode names
        try
        {
            // Parse out name to use in URL
            var htmlString = await BuildAndSendRequest(fullUrl, null);
            HtmlDocument doc = new();
            doc.LoadHtml(htmlString);
            var rawTitle = doc
                .DocumentNode.SelectSingleNode("//h1[@class='title-name h1_bold_none']")
                .InnerText;
            var encodedTitle = Uri.EscapeDataString(rawTitle);

            // Construct new URL to scrape episode list
            fullUrl = $"{malBase}anime/{anime.ID}/{encodedTitle}/episode";
            htmlString = await BuildAndSendRequest(fullUrl, null);
            doc.LoadHtml(htmlString);
            var nodes = doc.DocumentNode.SelectNodes(@"//a[@class='fl-l fw-b ']");
            foreach (var node in nodes)
            {
                episodeList.Add(WebUtility.HtmlDecode(node.InnerText));
            }
        }
        // Scraping MAL website fails
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"EXCEPTION CAUGHT: {ex.Message}");
            Console.WriteLine("Failed to get episode names. Generic episode names displayed.");

            episodeList.Clear();
            episodeList.Capacity = anime.NumEpisodes;
            for (int i = 1; i <= anime.NumEpisodes; i++)
            {
                episodeList.Add($"Episode {i}");
            }
        }

        return episodeList;
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

    private static async Task PlayEpisode(string path, Track track, string episodeName)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = "mpv";
        if (track != null)
        {
            startInfo.Arguments =
                $"--referrer={megaplaySource} \"{path}\" --sub-file=\"{track.File}\" --title=\"{episodeName} ({track.Label})\"";
        }
        else
        {
            startInfo.Arguments = $"--referrer={megaplaySource} \"{path}\"";
        }

        Process process = Process.Start(startInfo)!;
        process.WaitForExit();
    }

    // shouldOffset:
    // true: should returned value be offset by -1 (ex. if indexing into a 0-indexed array)?
    // false: should returned value be the raw displayed selection value?
    private static int MakeSelection<T>(List<T> list, bool shouldOffset, int lastChoice = 0)
    {
        if (lastChoice < 0)
            lastChoice = 0;
        int selectionIndex = lastChoice;

        while (true)
        {
            Console.Clear();

            for (int i = 0; i < list.Count; i++)
            {
                // Prepend selection with "> "
                Console.Write(i == selectionIndex ? "> " : " ");
                Console.WriteLine($"{i + 1}) {list[i]?.ToString()}");
            }
            Console.WriteLine("\n\n\n[Up]/[K] to go up, [Down]/[J] to go down, [Enter] to select");

            ConsoleKey key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.UpArrow || key == ConsoleKey.K)
            {
                if (selectionIndex > 0)
                {
                    selectionIndex--;
                }
            }
            else if (key == ConsoleKey.DownArrow || key == ConsoleKey.J)
            {
                if (selectionIndex < list.Count - 1)
                {
                    selectionIndex++;
                }
            }
            else if (key == ConsoleKey.Enter)
            {
                if (shouldOffset)
                    return selectionIndex;
                else
                    return ++selectionIndex;
            }
        }
    }

    public static async Task Main(string[] args)
    {
        List<AnimeResult> animeList = new();
        bool valid = false;

        while (animeList.Count == 0)
        {
            string title = "";

            while (!valid)
            {
                Console.Write("Please enter a title: ");
                title = Console.ReadLine()!.Trim();

                if (title == "")
                    Console.WriteLine("Invalid title. Please try again.");
                else
                    valid = true;
            }

            Console.Write("Searching...");
            animeList = await MakeSearchQuery(title!);
            Console.WriteLine("\r\nDone!");

            if (animeList.Count == 0)
            {
                Console.WriteLine("No results found. Searching again...");
                valid = false;
            }
        }

        Console.Write("Select an anime: ");
        int animeIndex = MakeSelection(animeList, true);
        var episodeList = await GetEpisodes(animeList[animeIndex]);
        foreach (var ep in episodeList)
        {
            Console.WriteLine(ep);
        }
        int episode = 0;

        while (true)
        {
            episode = MakeSelection(episodeList, false, episode - 1);
            Console.WriteLine(episode);
            int trackIndex = -1;

            var fileSource = await GetSources(animeList[animeIndex], episode);
            if (fileSource.trackList!.Count > 0)
            {
                if (args.Contains("--c"))
                {
                    Console.Write("Select a track: ");
                    trackIndex = MakeSelection(fileSource.trackList!, true);
                }
                // Select default track
                else
                {
                    if (fileSource.trackList!.Count > 0)
                    {
                        for (int i = 0; i < fileSource.trackList!.Count; i++)
                        {
                            var track = fileSource.trackList[i];

                            if (track.Default == true)
                            {
                                trackIndex = i;
                            }
                        }
                        Console.WriteLine();
                    }
                }
            }
            else
            {
                Console.WriteLine("No subtitle tracks found.");
            }

            Track chosenTrack = null!;
            if (trackIndex > -1)
            {
                chosenTrack = fileSource.trackList![trackIndex];
                Console.WriteLine($"Track chosen: {fileSource.trackList[trackIndex]}");
            }

            // Launch app
            await PlayEpisode(fileSource.Source!.File, chosenTrack, episodeList[episode]);
        }
    }
}
