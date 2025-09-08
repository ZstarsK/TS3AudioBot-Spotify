// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2017  TS3AudioBot contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TS3AudioBot.Config;
using TS3AudioBot.Helper;

namespace TS3AudioBot.ResourceFactories
{
    // Minimal QQ Music resolver (PoC): resolves a single songmid to a playable 128k m4a link
    public sealed class QQMusicResolver : IResourceResolver
    {
        private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

        private static readonly Regex QqSongMidFromUrl = new Regex(
            @"(?:(?:^|/)([0-9A-Za-z]{14,})\b)|(?:song(?:detail)?(?:/|\?id=)([0-9A-Za-z]{14,}))",
            Util.DefaultRegexConfig);

        private readonly ConfResolverQqMusic conf;

        public string ResolverFor => "qqmusic";

        public QQMusicResolver(ConfResolverQqMusic conf)
        {
            this.conf = conf;
        }

        public MatchCertainty MatchResource(ResolveContext ctx, string uri)
        {
            if (!conf.Enabled)
                return MatchCertainty.Never;

            if (uri.StartsWith("qqmusic:", StringComparison.OrdinalIgnoreCase))
                return MatchCertainty.Always;

            if (uri.Contains("y.qq.com", StringComparison.OrdinalIgnoreCase) || uri.Contains("qq.com", StringComparison.OrdinalIgnoreCase))
                return MatchCertainty.OnlyIfLast;

            if (IsLikelySongMid(uri))
                return MatchCertainty.OnlyIfLast;

            return MatchCertainty.Never;
        }

        public async Task<PlayResource> GetResource(ResolveContext ctx, string uri)
        {
            string mid = ExtractSongMid(uri);
            return await GetResourceById(ctx, new AudioResource(mid, null, ResolverFor));
        }

        public async Task<PlayResource> GetResourceById(ResolveContext ctx, AudioResource resource)
        {
            if (!conf.Enabled)
                throw Error.LocalStr("QQ Music resolver is disabled in config.");

            var songMid = resource.ResourceId;
            if (string.IsNullOrWhiteSpace(songMid))
                throw Error.LocalStr("Invalid QQ Music id.");

            var cookie = conf.Cookie.Value;
            var referer = conf.Referer.Value;

            // Build vkey request URL (GET with encoded JSON data parameter)
            var dataObj = new
            {
                req_0 = new
                {
                    module = "vkey.GetVkeyServer",
                    method = "CgiGetVkey",
                    param = new
                    {
                        guid = GenerateGuid(),
                        uin = ExtractUinOrZero(cookie),
                        songmid = new[] { songMid },
                        filename = new[] { $"C400{songMid}.m4a" }
                    }
                },
                comm = new { uin = ExtractUinOrZero(cookie), format = "json", ct = 24, cv = 0 }
            };

            string dataJson = JsonSerializer.Serialize(dataObj);
            string url = "https://u.y.qq.com/cgi-bin/musics.fcg?format=json&data=" + Uri.EscapeDataString(dataJson);

            try
            {
                var req = WebWrapper
                    .Request(url)
                    .WithHeader("Referer", string.IsNullOrWhiteSpace(referer) ? "https://y.qq.com/" : referer);
                if (!string.IsNullOrWhiteSpace(cookie))
                    req = req.WithHeader("Cookie", cookie);

                string json = await req.AsString();
                var playUrl = ParsePlayUrlFromVkey(json);

                if (string.IsNullOrWhiteSpace(playUrl))
                {
                    // Distinguish cookie-related error vs. other errors best-effort
                    if (!string.IsNullOrWhiteSpace(cookie))
                        throw Error.LocalStr("QQ Music cookie seems invalid or expired. Use '!qq setcookie <cookie>' to update.");
                    else
                        throw Error.LocalStr("Unable to fetch playable URL. This track may require login/VIP or is region-restricted.");
                }

                return new PlayResource(playUrl, new AudioResource(songMid, resource.ResourceTitle ?? songMid, ResolverFor));
            }
            catch (AudioBotException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "QQMusic resolve failed");
                throw Error.LocalStr("Failed to resolve QQ Music resource.");
            }
        }

        public string RestoreLink(ResolveContext ctx, AudioResource resource)
            => resource.ResourceId.StartsWith("qqmusic:", StringComparison.OrdinalIgnoreCase)
                ? resource.ResourceId
                : $"qqmusic:{resource.ResourceId}";

        public void Dispose() { }

        private static bool IsLikelySongMid(string text)
        {
            // Song mid are base62-like strings around 14-16 chars typically
            return text.Length >= 12 && text.Length <= 24 && Regex.IsMatch(text, "^[0-9A-Za-z]+$");
        }

        private static string ExtractSongMid(string input)
        {
            if (input.StartsWith("qqmusic:", StringComparison.OrdinalIgnoreCase))
                return input.Substring("qqmusic:".Length);

            var match = QqSongMidFromUrl.Match(input);
            if (match.Success)
            {
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    var val = match.Groups[i].Value;
                    if (!string.IsNullOrEmpty(val))
                        return val;
                }
            }
            if (IsLikelySongMid(input))
                return input;
            throw Error.LocalStr("Could not extract QQ Music song id.");
        }

        private static string GenerateGuid()
        {
            // 10-digit number used by QQ API, any random int is typically fine
            var rnd = new Random();
            return rnd.Next(100000000, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string ExtractUinOrZero(string? cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return "0";
            // Try extracting uin numeric from cookie e.g. uin=o1234567890 or uin=1234567890
            var m = Regex.Match(cookie, @"(?:^|;\s*)uin=o?(\d+)");
            if (m.Success) return m.Groups[1].Value;
            return "0";
        }

        private static string? ParsePlayUrlFromVkey(string json)
        {
            using var doc = JsonDocument.Parse(json);

            static (JsonElement data, bool ok) TryGetData(JsonElement root)
            {
                if (root.TryGetProperty("req_0", out var r0) && r0.TryGetProperty("data", out var d0))
                    return (d0, true);
                if (root.TryGetProperty("req", out var r) && r.TryGetProperty("data", out var d))
                    return (d, true);
                return (default, false);
            }

            var (data, ok) = TryGetData(doc.RootElement);
            if (!ok) return null;

            string? sip0 = null;
            if (data.TryGetProperty("sip", out var sip) && sip.ValueKind == JsonValueKind.Array && sip.GetArrayLength() > 0)
                sip0 = sip[0].GetString();

            if (!data.TryGetProperty("midurlinfo", out var midurl) || midurl.ValueKind != JsonValueKind.Array || midurl.GetArrayLength() == 0)
                return null;

            var purl = midurl[0].TryGetProperty("purl", out var purlEl) ? purlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(purl))
                return null;

            // If purl is absolute, return as is; else prefix with sip
            if (purl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return purl;

            if (string.IsNullOrWhiteSpace(sip0))
                // Common default CDN prefix when sip is missing
                return "https://isure.stream.qqmusic.qq.com/" + purl;

            if (!sip0.EndsWith("/")) sip0 += "/";
            return sip0 + purl;
        }
    }
}

