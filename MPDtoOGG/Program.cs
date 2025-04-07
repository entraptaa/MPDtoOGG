using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Refit;

public interface ICdnApi
{
    [Get("/{pid}")]
    Task<ApiResponse<CdnResponse>> GetPlaylistAsync(string pid);
}

public class CdnResponse
{
    public required string Playlist { get; set; }
}

public class MPEGDashHandler
{
    private readonly string _pid;
    private readonly string _outDir;
    private readonly ICdnApi _api;
    private const int Threads = 15;

    public MPEGDashHandler(string pid, string outDir, ICdnApi api)
    {
        _pid = pid;
        _outDir = outDir;
        _api = api;
    }

    public async Task<string> AcquireMPEGDashPlaylistAsync()
    {
        var response = await _api.GetPlaylistAsync(_pid);
        var playlistData = Convert.FromBase64String(response.Content.Playlist);
        return Encoding.UTF8.GetString(playlistData);
    }

    public async Task<string> DownloadMPDPlaylistAsync(string mpdContent)
    {
        var doc = XDocument.Parse(mpdContent);
        var ns = doc.Root.GetDefaultNamespace();

        var baseUrl = doc.Descendants(ns + "BaseURL").First().Value;
        var durationStr = doc.Root.Attribute("mediaPresentationDuration").Value;
        var segmentDurationStr = doc.Root.Attribute("maxSegmentDuration").Value;

        var audioDuration = double.Parse(durationStr.Replace("PT", "").Replace("S", ""));
        var segmentDuration = double.Parse(segmentDurationStr.Replace("PT", "").Replace("S", ""));
        var numSegments = (int)Math.Ceiling(audioDuration / segmentDuration);

        var representation = doc.Descendants(ns + "Representation").First();
        var reprId = representation.Attribute("id").Value;
        var initTemplate = representation.Element(ns + "SegmentTemplate").Attribute("initialization").Value;
        var mediaTemplate = representation.Element(ns + "SegmentTemplate").Attribute("media").Value;
        var startNumber = int.Parse(representation.Element(ns + "SegmentTemplate").Attribute("startNumber").Value);

        var outputPrefix = Path.Combine(_outDir, _pid + "_");
        Directory.CreateDirectory(_outDir);

        var httpClient = new HttpClient();

        var initFile = initTemplate.Replace("$RepresentationID$", reprId);
        var initPath = outputPrefix + initFile;
        var initUrl = baseUrl + initFile;
        var initBytes = await httpClient.GetByteArrayAsync(initUrl);
        await File.WriteAllBytesAsync(initPath, initBytes);

        var segmentPaths = new List<string>();

        async Task DownloadSegment(int id)
        {
            var file = mediaTemplate.Replace("$RepresentationID$", reprId).Replace("$Number$", id.ToString());
            var filePath = outputPrefix + file;
            var url = baseUrl + file;
            var data = await httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(filePath, data);
            segmentPaths.Add(filePath);
        }

        var tasks = new List<Task>();
        for (int i = startNumber; i <= numSegments; i++)
        {
            tasks.Add(DownloadSegment(i));
            if (tasks.Count == Threads)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);

        segmentPaths.Sort((a, b) =>
        {
            var aMatch = Regex.Match(a, @"_(\d+)\.");
            var bMatch = Regex.Match(b, @"_(\d+)\.");

            if (!aMatch.Success || !bMatch.Success)
                return string.Compare(a, b, StringComparison.Ordinal);

            int aNum = int.Parse(aMatch.Groups[1].Value);
            int bNum = int.Parse(bMatch.Groups[1].Value);
            return aNum.CompareTo(bNum);
        });


        var masterFile = outputPrefix + "master_audio.mp4";
        var allBytes = new List<byte>(await File.ReadAllBytesAsync(initPath));
        foreach (var segPath in segmentPaths)
        {
            allBytes.AddRange(await File.ReadAllBytesAsync(segPath));
        }

        await File.WriteAllBytesAsync(masterFile, allBytes.ToArray());

        File.Delete(initPath);
        segmentPaths.ForEach(File.Delete);

        return masterFile;
    }

    public async Task<string> ConvertToOggAsync(string masterAudioPath)
    {
        var outputPath = Path.Combine(_outDir, $"AUD_STREAMID_{_pid}", "preview.ogg");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        var args = $"-i \"{masterAudioPath}\" -acodec libopus -ar 48000 \"{outputPath}\"";

        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });

        await proc.WaitForExitAsync();

        File.Delete(masterAudioPath);

        return outputPath;
    }

    public static bool IsFfmpegInPath()
    {
        var ffmpegPath = Environment.GetEnvironmentVariable("PATH")
            .Split(Path.PathSeparator)
            .Select(p => Path.Combine(p, "ffmpeg.exe"))
            .FirstOrDefault(File.Exists);
        return ffmpegPath != null;
    }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("MPEG-DASH Audio Downloader")
        {
            new Option<string>("--pid", "The playlist PID") { IsRequired = true },
            new Option<string>("--out", () => "out", "The output directory")
        };

        rootCommand.Handler = CommandHandler.Create<string, string>(async (pid, @out) =>
        {
            var api = RestService.For<ICdnApi>("https://cdn.qstv.on.epicgames.com/");
            var handler = new MPEGDashHandler(pid, @out, api);

            var mpd = await handler.AcquireMPEGDashPlaylistAsync();
            var mp4 = await handler.DownloadMPDPlaylistAsync(mpd);

            if (!MPEGDashHandler.IsFfmpegInPath())
            {
                Console.WriteLine("FFmpeg not found in PATH. Please install FFmpeg and add it to your PATH.");
                return;
            }

            var ogg = await handler.ConvertToOggAsync(mp4);

            Console.WriteLine($"Done! OGG file saved to: {ogg}");
        });

        return await rootCommand.InvokeAsync(args);
    }
}