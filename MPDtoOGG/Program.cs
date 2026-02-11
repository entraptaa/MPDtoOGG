using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
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
        try
        {
            mpdContent = mpdContent.Trim();
            mpdContent = mpdContent.Replace("xmlns:xsi=\"\"", "");
            mpdContent = mpdContent.Replace("xsi:schemaLocation=\"\"", "");
            
            var doc = XDocument.Parse(mpdContent);
            var ns = doc.Root.GetDefaultNamespace();

            var baseUrl = doc.Descendants(ns + "BaseURL").First().Value;
            var representation = doc.Descendants(ns + "Representation").First();
            var initFile = representation.Element(ns + "BaseURL").Value;

            Directory.CreateDirectory(_outDir);

            var httpClient = new HttpClient();

            var initPath = Path.Combine(_outDir, initFile);
            var initUrl = baseUrl + initFile;
            var initBytes = await httpClient.GetByteArrayAsync(initUrl);
            await File.WriteAllBytesAsync(initPath, initBytes);

            var masterFile = Path.Combine(_outDir, "master_audio.mp4");
            var initData = await File.ReadAllBytesAsync(initPath);

            await File.WriteAllBytesAsync(masterFile, initData);
            File.Delete(initPath);

            return masterFile;
        }
        catch (Exception ex)
        {
            var debugPath = Path.Combine(_outDir, "debug_mpd.xml");
            Directory.CreateDirectory(_outDir);
            await File.WriteAllTextAsync(debugPath, mpdContent);
            throw new Exception($"XML parsing failed. MPD content saved to: {debugPath}. Error: {ex.Message}", ex);
        }
    }

    public async Task<string> ConvertToOggAsync(string masterAudioPath)
    {
        var oggPath = Path.Combine(_outDir, "preview.ogg");

        var ffmpegArgs = $"-i \"{masterAudioPath}\" -acodec libopus -ar 48000 \"{oggPath}\"";

        var ffmpegProc = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        ffmpegProc.Start();
        var errorOutput = await ffmpegProc.StandardError.ReadToEndAsync();
        var standardOutput = await ffmpegProc.StandardOutput.ReadToEndAsync();
        await ffmpegProc.WaitForExitAsync();

        if (ffmpegProc.ExitCode != 0)
        {
            throw new Exception($"FFmpeg failed with exit code: {ffmpegProc.ExitCode}\n\nError output:\n{errorOutput}\n\nStandard output:\n{standardOutput}");
        }

        File.Delete(masterAudioPath);

        return oggPath;
    }

    public static bool IsFfmpegInPath()
    {
        var ffmpegPath = Environment.GetEnvironmentVariable("PATH")
            .Split(Path.PathSeparator)
            .Select(p => Path.Combine(p, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"))
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
            try
            {
                var outDir = Path.Combine(@out, $"AUD_STREAMID_{pid}");
                var api = RestService.For<ICdnApi>("https://cdn.qstv.on.epicgames.com/");
                var handler = new MPEGDashHandler(pid, outDir, api);

                if (!MPEGDashHandler.IsFfmpegInPath())
                {
                    Console.WriteLine("FFmpeg not found in PATH. Please install FFmpeg and add it to your PATH.");
                    return;
                }

                Console.WriteLine("Acquiring MPEG-DASH playlist...");
                var mpd = await handler.AcquireMPEGDashPlaylistAsync();
                Console.WriteLine($"MPD Content length: {mpd.Length} characters");

                Console.WriteLine("Downloading MPD playlist...");
                var mp4 = await handler.DownloadMPDPlaylistAsync(mpd);
                Console.WriteLine($"MP4 file created at: {mp4}");
                
                if (!File.Exists(mp4))
                {
                    Console.WriteLine($"ERROR: MP4 file does not exist at {mp4}");
                    return;
                }
                
                var fileInfo = new FileInfo(mp4);
                Console.WriteLine($"MP4 file size: {fileInfo.Length} bytes");

                Console.WriteLine("Converting to OGG...");
                var oggPath = await handler.ConvertToOggAsync(mp4);

                Console.WriteLine($"Done! OGG file saved to: {oggPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return;
            }
        });

        return await rootCommand.InvokeAsync(args);
    }
}