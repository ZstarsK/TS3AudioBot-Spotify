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

            // Build filename candidates based on preferred quality with fallbacks
            var filenames = BuildFilenameCandidates(conf.DefaultQuality.Value, songMid);
            var songmids = new string[filenames.Length];
            var songtypes = new int[filenames.Length];
            for (int i = 0; i < filenames.Length; i++) { songmids[i] = songMid; songtypes[i] = 0; }

            // Build vkey request URL (GET with encoded JSON data parameter)
            var dataObj = new
            {
                req = new
                {
                    module = "CDN.SrfCdnDispatchServer",
                    method = "GetCdnDispatch",
                    param = new { guid = GenerateGuidFromCookie(cookie), calltype = 0, userip = string.Empty },
                },
                req_0 = new
                {
                    module = "vkey.GetVkeyServer",
                    method = "CgiGetVkey",
                    param = new
                    {
                        guid = GenerateGuidFromCookie(cookie),
                        uin = ExtractUinOrZero(cookie),
                        songmid = songmids,
                        songtype = songtypes,
                        loginflag = 1,
                        platform = "20",
                        filename = filenames
                    }
                },
                comm = new { uin = ExtractUinOrZero(cookie), format = "json", ct = 24, cv = 0, platform = "yqq.json" }
            };

            string dataJson = JsonSerializer.Serialize(dataObj);
            string url = "https://u.y.qq.com/cgi-bin/musics.fcg?format=json&inCharset=utf8&outCharset=utf-8&needNewCode=0&platform=yqq.json&g_tk=5381&data=" + Uri.EscapeDataString(dataJson);

            try
            {
                var req = WebWrapper
                    .Request(url)
                    .WithHeader("Referer", string.IsNullOrWhiteSpace(referer) ? "https://y.qq.com/" : referer);
                if (!string.IsNullOrWhiteSpace(cookie))
                    req = req.WithHeader("Cookie", cookie);
                // Some QQ endpoints require Origin to be set as well
                req = req.WithHeader("Origin", "https://y.qq.com");

                string json = await req.AsString();
                var playUrl = ParsePlayUrlFromVkey(json);

                if (string.IsNullOrWhiteSpace(playUrl))
                {
                    var reason = TryExtractVkeyError(json) ?? "This track may require login/VIP or is region-restricted.";
                    if (!string.IsNullOrWhiteSpace(cookie))
                        throw Error.LocalStr($"Unable to fetch playable URL. {reason}");
                    else
                        throw Error.LocalStr($"Unable to fetch playable URL. {reason}");
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

        private static string GenerateGuidFromCookie(string? cookie)
        {
            // Prefer persistent ids from cookies if present (pgv_pvid or ts_uid), else use random 10+ digits
            try
            {
                if (!string.IsNullOrWhiteSpace(cookie))
                {
                    var m1 = Regex.Match(cookie, @"(?:^|;\s*)pgv_pvid=(\d{8,})");
                    if (m1.Success) return m1.Groups[1].Value;
                    var m2 = Regex.Match(cookie, @"(?:^|;\s*)ts_uid=(\d{6,})");
                    if (m2.Success) return m2.Groups[1].Value;
                }
            }
            catch { }
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

        private static string[] BuildFilenameCandidates(string? preferredQuality, string songMid)
        {
            // C400: aac 128k (m4a), M500: mp3 128k, M800: mp3 320k, F000: flac
            var list = new System.Collections.Generic.List<string>();
            string q = string.IsNullOrWhiteSpace(preferredQuality) ? "aac_128" : preferredQuality.ToLowerInvariant();
            void Add(string prefix, string ext) => list.Add(prefix + songMid + ext);
            switch (q)
            {
                case "flac": Add("F000", ".flac"); Add("M800", ".mp3"); Add("C400", ".m4a"); Add("M500", ".mp3"); break;
                case "mp3_320": Add("M800", ".mp3"); Add("M500", ".mp3"); Add("C400", ".m4a"); break;
                case "aac_128": default: Add("C400", ".m4a"); Add("M500", ".mp3"); Add("M800", ".mp3"); break;
            }
            return list.ToArray();
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

            for (int i = 0; i < midurl.GetArrayLength(); i++)
            {
                var item = midurl[i];
                var purl = item.TryGetProperty("purl", out var purlEl) ? purlEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(purl)) continue;
                if (purl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return purl;
                var baseUrl = sip0;
                if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://isure.stream.qqmusic.qq.com/";
                if (!baseUrl.EndsWith("/")) baseUrl += "/";
                return baseUrl + purl;
            }

            return null;
        }

        private static string? TryExtractVkeyError(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                JsonElement data;
                if (doc.RootElement.TryGetProperty("req_0", out var r0) && r0.TryGetProperty("data", out var d0))
                    data = d0;
                else if (doc.RootElement.TryGetProperty("req", out var r) && r.TryGetProperty("data", out var d))
                    data = d;
                else return null;

                if (data.TryGetProperty("midurlinfo", out var midurl) && midurl.ValueKind == JsonValueKind.Array && midurl.GetArrayLength() > 0)
                {
                    var item = midurl[0];
                    if (item.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String)
                    {
                        var s = msg.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
                if (data.TryGetProperty("testfile2g", out var _))
                {
                    // No explicit message, but presence of fields implies response is fine -> likely VIP or region lock
                    return "QQ Music returned no purl (likely VIP-only or region-restricted).";
                }
            }
            catch { }
            return null;
        }
    }
}
