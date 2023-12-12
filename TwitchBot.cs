using System.Net.Sockets;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace twitch_bot;

public partial class TwitchBot
{
    private const string TwitchIp = "irc.chat.twitch.tv";
    private const int TwitchPort = 6667;
    private const string OsuIp = "irc.ppy.sh";
    private const int OsuPort = 6667;

    private readonly string _nick;
    private readonly string _password;
    private readonly string _channel;
    private StreamReader? _twitchIncoming;
    private StreamWriter? _twitchOutgoing;
    private StreamWriter? _osuOutgoing;
    private static readonly HttpClient HttpClient = new HttpClient();
    private static readonly string ApiKey = Environment.GetEnvironmentVariable("OSU_API_KEY")!;
    private static readonly string OsuIrcPass = Environment.GetEnvironmentVariable("OSU_IRC_PASS")!;

    public TwitchBot(string nick, string password, string channel)
    {
        this._nick = nick;
        this._password = password;
        this._channel = channel;
    }

    public async Task Start()
    {
        using var twitchTcp = new TcpClient();
        using var osuTcp = new TcpClient();
        await twitchTcp.ConnectAsync(TwitchIp, TwitchPort);
        await osuTcp.ConnectAsync(OsuIp, OsuPort);
        _twitchIncoming = new StreamReader(twitchTcp.GetStream());
        _twitchOutgoing = new StreamWriter(twitchTcp.GetStream()) { NewLine = "\r\n", AutoFlush = true };
        _osuOutgoing = new StreamWriter(osuTcp.GetStream()) { NewLine = "\r\n", AutoFlush = true };

        // Twitch login
        await _twitchOutgoing.WriteLineAsync($"PASS {_password}");
        await _twitchOutgoing.WriteLineAsync($"NICK {_nick}");
        await _twitchOutgoing.WriteLineAsync($"JOIN #{_channel}");
        await _twitchOutgoing.WriteLineAsync($"PRIVMSG #{_channel} :Bot started.");
        
        // osu! login
        await _osuOutgoing.WriteLineAsync($"PASS {OsuIrcPass}");
        await _osuOutgoing.WriteLineAsync($"NICK salihefee");
        await _osuOutgoing.WriteLineAsync($"PRIVMSG {_channel} :Bot started");

        while (true)
        {
            var twitchLine = await _twitchIncoming.ReadLineAsync();
            Console.WriteLine(twitchLine);

            var split = twitchLine!.Split(" "); 

            if (twitchLine.StartsWith("PING"))
            {
                await _twitchOutgoing.WriteLineAsync($"PONG {split[1]}");
                Console.WriteLine($"PONG {split[1]}");
            }

            if (split.Length <= 1 || split[1] != "PRIVMSG") continue;
            var exclamationMarkPos = split[0].IndexOf("!", StringComparison.Ordinal);
            var username = split[0].Substring(1, exclamationMarkPos - 1);

            var secondColonPos = twitchLine.IndexOf(":", 1, StringComparison.Ordinal);
            var message = twitchLine[(secondColonPos + 1)..];

            foreach (var word in message.Split(" "))
            {
                if (!BeatmapRegex().IsMatch(word)) continue;
                var mapId = MapIdRegex().Match(word);

                HttpResponseMessage mapResponse;

                try
                {
                    mapResponse =
                        await HttpClient.GetAsync($"https://osu.ppy.sh/api/get_beatmaps?k={ApiKey}&b={mapId}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    continue;
                }

                var responseContent = mapResponse.Content.ReadAsStringAsync().Result;
                
                if (responseContent == "[]")
                {
                    await _twitchOutgoing.WriteLineAsync($"PRIVMSG #{_channel} :The specified beatmap was not found.");
                    Console.WriteLine($"PRIVMSG #{_channel} :The specified beatmap was not found.");
                }
                else
                {
                    dynamic responseObjects = JsonConvert.DeserializeObject(await mapResponse.Content.ReadAsStringAsync())!;
                    var responseObject = responseObjects[0];
                    await _twitchOutgoing.WriteLineAsync(
                        $"PRIVMSG #{_channel} :Received beatmap {responseObject.artist} - {responseObject.title} [{responseObject.version}] from user {username}");
                    Console.WriteLine($"PRIVMSG #{_channel} :Received beatmap {responseObject.artist} - {responseObject.title} [{responseObject.version}] from user {username}");
                    await _osuOutgoing.WriteLineAsync(
                        $"PRIVMSG salihefee :[{word} {responseObject.artist} - {responseObject.title} [{responseObject.version}]] sent by {username}");
                    Console.WriteLine($"PRIVMSG salihefee :[{word} {responseObject.artist} - {responseObject.title} [{responseObject.version}]] sent by {username}");
                }
            }
        }
    }

    [GeneratedRegex(@"(https?://osu\.ppy\.sh/(beatmaps|beatmapsets|b)/)[\w\d]+")]
    private static partial Regex BeatmapRegex();

    [GeneratedRegex(@"\d{2,}(?=[^\d]*\d{0,2}[^\d]*$)")]
    private static partial Regex MapIdRegex();
}