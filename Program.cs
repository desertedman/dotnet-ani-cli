using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;

public class AnimeResult
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
public class Track
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
    public string Player { get; set; }
    public string Path { get; set; }
}

public enum PlayerID
{
    PLAYER_MPV,
    PLAYER_VLC,
}

public abstract class Player
{
    public int Program { get; set; }
    protected List<String> LaunchArgs { get; } = new();
    private string _path = "";

    protected virtual string Path
    {
        get => _path;
        set => _path = value;
    }

    public abstract void BuildReferrer(string source);

    public abstract void BuildSubtitles(Track track);

    public abstract void BuildTitle(AnimeResult anime, string episodeName, int episodeNr);

    public abstract void BuildSource(string path);

    public abstract ProcessStartInfo GetProcessStartInfo();
}

public class MpvPlayer : Player
{
    public MpvPlayer()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            base.Path = "mpv";
        }
        else if (OperatingSystem.IsWindows())
        {
            base.Path = @"C:\Program Files\MPV Player\mpv.exe";
        }
    }

    public override void BuildReferrer(string source)
    {
        LaunchArgs.Add($"--referrer=\"{source}\"");
    }

    public override void BuildSubtitles(Track track)
    {
        if (track != null)
        {
            LaunchArgs.Add($"--sub-file=\"{track.File}\"");
        }
    }

    public override void BuildTitle(AnimeResult anime, string episodeName, int episodeNr)
    {
        LaunchArgs.Add($"--title=\"{anime.Name} - {episodeNr}) {episodeName}\"");
    }

    public override void BuildSource(string path)
    {
        LaunchArgs.Add($"\"{path}\"");
    }

    public override ProcessStartInfo GetProcessStartInfo()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo { FileName = Path };
        string arglist = "";
        foreach (var arg in LaunchArgs)
        {
            arglist = $"{arglist} {arg}";
        }
        startInfo.Arguments = arglist;

        return startInfo;
    }
}

public class VlcPlayer : Player
{
    protected override string Path
    {
        get => base.Path;
        set
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                base.Path = "vlc";
            }
            else if (OperatingSystem.IsWindows())
            {
                base.Path = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
            }
        }
    }

    public override void BuildReferrer(string source)
    {
        LaunchArgs.Add($"--http-referrer={source}");
    }

    public override void BuildSubtitles(Track track)
    {
        throw new NotImplementedException();
        //     if (track != null)
        //     {
        //         LaunchArgs.Add($"--sub-file=\"{track.File}\"");
        //     }
    }

    public override void BuildTitle(AnimeResult anime, string episodeName, int episodeNr)
    {
        LaunchArgs.Add($"--meta-title=\"{anime.Name} - {episodeNr}) {episodeName}");
    }

    public override void BuildSource(string path)
    {
        LaunchArgs.Add(path);
    }

    public override ProcessStartInfo GetProcessStartInfo()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo { FileName = Path };
        foreach (var arg in LaunchArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }
}

public class PlayerFactory
{
    public static Player CreatePlayer(PlayerID program)
    {
        switch (program)
        {
            case PlayerID.PLAYER_MPV:
                return new MpvPlayer();
            case PlayerID.PLAYER_VLC:
                return new VlcPlayer();

            default:
                return null!;
        }
    }
}

public class Program
{
    private const string megaplaySource = "https://megaplay.buzz/";
    private const string malAPI = "https://api.myanimelist.net/v2/";
    private const string malBase = "https://myanimelist.net/";
    private const string clientKey = "6114d00ca681b7701d1e15fe11a4987e";
    private static HttpClient sharedClient = new();
    private static Info Info;
    private static List<String> Players = new List<String> { "mpv", "vlc" };
    private static Player? _player;

    private static void Configure()
    {
        bool programFound = false;
        Console.WriteLine("Detecting player...");

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            foreach (var player in Players)
            {
                // Test for program
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = player,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                process.Start();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Info.Player = player;
                    Info.Path = player;
                    programFound = true;
                    break;
                }
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            if (File.Exists(@"C:\Program Files\MPV Player\mpv.exe"))
            {
                Info.Player = "mpv";
                Info.Path = @"C:\Program Files\MPV Player\mpv.exe";
                programFound = true;
            }
            else if (File.Exists(@"C:\Program Files\VideoLAN\VLC\vlc.exe"))
            {
                Info.Player = "vlc";
                Info.Path = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
                programFound = true;
            }
        }
        if (!programFound)
        {
            Console.WriteLine("No supported player detected. Exiting.");
            Environment.Exit(1);
        }
        else
        {
            Console.WriteLine($"{Info.Player} detected. Using {Info.Player}.");
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

        Console.WriteLine($"Request: {request.RequestUri!.AbsoluteUri}");
        // Console.WriteLine($"Request: {request.ToString()}");
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
            try
            {
                foreach (var node in nodes)
                {
                    episodeList.Add(WebUtility.HtmlDecode(node.InnerText));
                }
            }
            catch (NullReferenceException ex)
            {
                // TODO:: Add fix for episodes not listed in MAL page. Example: Blue Lock Neo Egoist League
                Console.WriteLine($"EXCEPTION CAUGHT: {ex.Message}");
                Console.WriteLine("Failed to get HTML element; probably 404?");
                Environment.Exit(1);
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

    private static String BuildLaunchArgs(
        string path,
        Track track,
        string episodeName,
        AnimeResult anime
    )
    {
        string launchArgs = "";
        if (Info.Player == "mpv")
        {
            launchArgs = $"--referrer={megaplaySource} \"{path}\"";

            if (track != null)
            {
                launchArgs +=
                    $" --sub-file=\"{track.File}\" --title=\"{anime.Name} - {episodeName} ({track.Label})\"";
            }
        }
        else if (Info.Player == "vlc")
        {
            launchArgs = $"--http-referrer={megaplaySource} \"{path}\" ";
            if (track != null)
            {
                launchArgs +=
                    $" --sub-file=\"{track.File}\" --meta-title=\"{anime.Name} - {episodeName} ({track.Label})\"";
            }
        }

        return launchArgs;
    }

    private static async Task PlayEpisode(
        string path,
        Track track,
        string episodeName,
        AnimeResult anime
    )
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = Info.Path;

        string launchArgs = BuildLaunchArgs(path, track, episodeName, anime);
        startInfo.Arguments = launchArgs;

        Console.WriteLine($"EXECUTING: {startInfo.FileName} {startInfo.Arguments}");
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
            // Returns actual episode number; BEWARE OF OUT OF BOUNDS INDEX INTO episodeList
            episode = MakeSelection(episodeList, false, episode - 1);
            Console.WriteLine($"Playing episode {episode}");
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

            for (int i = 0; i < episodeList.Count; i++)
            {
                Console.WriteLine($"{i}) {episodeList[i]}");
            }

            int actualEpisodeIndex = episode - 1;
            Console.WriteLine($"Accessing episode {episode}: {episodeList[actualEpisodeIndex]}");

            if (args.Contains("--v"))
            {
                _player = PlayerFactory.CreatePlayer(PlayerID.PLAYER_VLC);
            }
            else
            {
                _player = PlayerFactory.CreatePlayer(PlayerID.PLAYER_MPV);
            }

            _player.BuildReferrer(megaplaySource);
            _player.BuildSource(fileSource.Source!.File);
            _player.BuildSubtitles(chosenTrack);
            _player.BuildTitle(animeList[animeIndex], episodeList[actualEpisodeIndex], episode);
            ProcessStartInfo startInfo = _player.GetProcessStartInfo();
            // Console.Write($"EXECUTING: {startInfo.FileName}");
            // foreach (var arg in startInfo.ArgumentList)
            // {
            //     Console.WriteLine($"[{arg}]");
            // }
            Process process = Process.Start(startInfo)!;
            process.WaitForExit();
        }
    }
}
