// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2017  TS3AudioBot contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class QQMusicResolver : IResourceResolver, ISearchResolver
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

            // Try to fetch media_mid for better filename coverage
            var mediaMid = await TryFetchMediaMid(songMid, cookie, referer);

            // Build filename candidates based on preferred quality with fallbacks
            var filenames = BuildFilenameCandidates(conf.DefaultQuality.Value, songMid, mediaMid);
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
            string gtk = CalculateGTK(cookie);
            string url = $"https://u.y.qq.com/cgi-bin/musics.fcg?format=json&inCharset=utf8&outCharset=utf-8&needNewCode=0&platform=yqq.json&g_tk={gtk}&data=" + Uri.EscapeDataString(dataJson);
            
            // 详细调试信息
            Log.Info("QQMusic: Requesting vkey with g_tk={0}, uin={1}, guid={2}, songMid={3}", 
                gtk, ExtractUinOrZero(cookie), GenerateGuidFromCookie(cookie), songMid);

            try
            {
                var req = WebWrapper
                    .Request(url)
                    .WithHeader("Referer", string.IsNullOrWhiteSpace(referer) ? "https://y.qq.com/" : referer)
                    .WithHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")
                    .WithHeader("Accept", "application/json, text/plain, */*")
                    .WithHeader("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                    
                if (!string.IsNullOrWhiteSpace(cookie))
                    req = req.WithHeader("Cookie", cookie);
                    
                // Some QQ endpoints require Origin to be set as well
                req = req.WithHeader("Origin", "https://y.qq.com");

                string json = await req.AsString();
                // Debug: log truncated response to help diagnose
                try { var dbg = json.Length > 1200 ? json.Substring(0, 1200) + "..." : json; Log.Debug("QQMusic vkey response: {0}", dbg); } catch { }
                var playUrl = ParsePlayUrlFromVkey(json);

                if (string.IsNullOrWhiteSpace(playUrl))
                {
                    var reason = TryExtractVkeyError(json) ?? "This track may require login/VIP or is region-restricted.";
                    var cookieInfo = AnalyzeCookieForVip(cookie);
                    
                    Log.Warn("QQMusic: Empty purl. songMid={0} mediaMid={1} uin={2} guid={3} filenames=[{4}] reason={5} cookieStatus={6}",
                        songMid,
                        mediaMid ?? "-",
                        ExtractUinOrZero(cookie),
                        GenerateGuidFromCookie(cookie),
                        string.Join(",", filenames),
                        reason,
                        cookieInfo);
                        
                    var errorMessage = $"Unable to fetch playable URL. {reason}";
                    if (!string.IsNullOrWhiteSpace(cookieInfo))
                        errorMessage += $" Cookie状态: {cookieInfo}";
                        
                    throw Error.LocalStr(errorMessage);
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

        // 实现ISearchResolver接口
        public async Task<IList<AudioResource>> Search(ResolveContext ctx, string keyword)
        {
            if (!conf.Enabled)
                throw Error.LocalStr("QQ Music resolver is disabled in config.");

            if (string.IsNullOrWhiteSpace(keyword))
                return new List<AudioResource>();

            try
            {
                var cookie = conf.Cookie.Value;
                var referer = conf.Referer.Value;

                // 构建搜索请求
                string gtk = CalculateGTK(cookie);
                var searchUrl = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?ct=24&qqmusic_ver=1298&new_json=1&remoteplace=txt.yqq.song&searchid=60997426243446155&t=0&aggr=1&cr=1&catZhida=1&lossless=0&flag_qc=0&p=1&n=20&w={Uri.EscapeDataString(keyword)}&g_tk={gtk}&loginUin=0&hostUin=0&format=json&inCharset=utf8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=0";

                var req = WebWrapper.Request(searchUrl)
                    .WithHeader("Referer", string.IsNullOrWhiteSpace(referer) ? "https://y.qq.com/" : referer)
                    .WithHeader("Origin", "https://y.qq.com");
                
                if (!string.IsNullOrWhiteSpace(cookie))
                    req = req.WithHeader("Cookie", cookie);

                string json = await req.AsString();
                Log.Debug("QQMusic search response: {0}", json.Length > 500 ? json.Substring(0, 500) + "..." : json);

                return ParseSearchResults(json);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "QQMusic search failed for keyword: {0}", keyword);
                throw Error.LocalStr("Failed to search QQ Music.");
            }
        }

        private static IList<AudioResource> ParseSearchResults(string json)
        {
            var results = new List<AudioResource>();
            
            try
            {
                using var doc = JsonDocument.Parse(json);
                
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("song", out var song) ||
                    !song.TryGetProperty("list", out var list) ||
                    list.ValueKind != JsonValueKind.Array)
                {
                    Log.Warn("QQMusic search: Invalid response format");
                    return results;
                }

                foreach (var item in list.EnumerateArray())
                {
                    if (item.TryGetProperty("songmid", out var songmid) &&
                        item.TryGetProperty("songname", out var songname) &&
                        item.TryGetProperty("singer", out var singer))
                    {
                        var songName = songname.GetString() ?? "Unknown";
                        var singerName = singer.GetString() ?? "Unknown";
                        var songId = songmid.GetString();
                        
                        if (!string.IsNullOrWhiteSpace(songId))
                        {
                            var title = $"{songName} - {singerName}";
                            results.Add(new AudioResource(songId, title, "qqmusic"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to parse QQMusic search results");
            }

            return results;
        }

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

        /// <summary>
        /// 从Cookie中提取skey并计算g_tk值
        /// QQ音乐VIP验证需要正确的g_tk值
        /// </summary>
        /// <param name="cookie">Cookie字符串</param>
        /// <returns>计算得到的g_tk值</returns>
        private static string CalculateGTK(string? cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie))
            {
                Log.Debug("QQMusic: No cookie provided, using default g_tk=5381");
                return "5381";
            }

            // 尝试从cookie中提取skey
            var skeyMatch = Regex.Match(cookie, @"(?:^|;\s*)skey=([^;]+)");
            if (!skeyMatch.Success)
            {
                Log.Debug("QQMusic: No skey found in cookie, using default g_tk=5381");
                return "5381";
            }

            var skey = skeyMatch.Groups[1].Value;
            if (string.IsNullOrEmpty(skey))
            {
                Log.Debug("QQMusic: Empty skey, using default g_tk=5381");
                return "5381";
            }

            // 计算g_tk：QQ音乐标准算法
            long hash = 5381;
            for (int i = 0; i < skey.Length; i++)
            {
                hash = hash * 33 + (int)skey[i];
            }
            var gtk = (hash & 0x7fffffff).ToString();
            
            Log.Debug("QQMusic: Calculated g_tk={0} from skey", gtk);
            return gtk;
        }

        /// <summary>
        /// 分析Cookie中的VIP相关字段，帮助诊断VIP音乐播放问题
        /// </summary>
        /// <param name="cookie">Cookie字符串</param>
        /// <returns>Cookie状态分析结果</returns>
        private static string AnalyzeCookieForVip(string? cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie))
            {
                return "未设置Cookie";
            }

            var issues = new List<string>();
            var hasRequiredFields = new List<string>();

            // 检查关键字段
            if (Regex.IsMatch(cookie, @"(?:^|;\s*)uin=o?\d+"))
                hasRequiredFields.Add("uin");
            else
                issues.Add("缺少uin字段");

            if (Regex.IsMatch(cookie, @"(?:^|;\s*)skey=[^;]+"))
                hasRequiredFields.Add("skey");
            else
                issues.Add("缺少skey字段(VIP验证必需)");

            // p_skey和p_lskey主要用于JS环境，HTTP请求中不一定需要
            if (Regex.IsMatch(cookie, @"(?:^|;\s*)p_skey=[^;]+"))
                hasRequiredFields.Add("p_skey");

            if (Regex.IsMatch(cookie, @"(?:^|;\s*)p_lskey=[^;]+"))
                hasRequiredFields.Add("p_lskey");

            // 检查会员相关字段
            var vipFields = new List<string>();
            if (Regex.IsMatch(cookie, @"(?:^|;\s*)vip_type=[^;]*[1-9]"))
                vipFields.Add("vip_type");
            if (Regex.IsMatch(cookie, @"(?:^|;\s*)login_type=[^;]+"))
                vipFields.Add("login_type");

            var result = new StringBuilder();
            
            if (hasRequiredFields.Any())
                result.Append($"已有字段[{string.Join(",", hasRequiredFields)}]");
            
            if (vipFields.Any())
                result.Append($" VIP字段[{string.Join(",", vipFields)}]");
            
            if (issues.Any())
            {
                if (result.Length > 0) result.Append(" ");
                result.Append($"问题[{string.Join(",", issues)}]");
            }

            return result.ToString();
        }

        private static string[] BuildFilenameCandidates(string? preferredQuality, string songMid, string? mediaMid)
        {
            // C400: aac 128k (m4a), M500: mp3 128k, M800: mp3 320k, F000: flac
            var list = new System.Collections.Generic.List<string>();
            string q = string.IsNullOrWhiteSpace(preferredQuality) ? "aac_128" : preferredQuality.ToLowerInvariant();
            void AddSong(string prefix, string ext) => list.Add(prefix + songMid + ext);
            void AddMedia(string prefix, string ext)
            {
                if (!string.IsNullOrWhiteSpace(mediaMid))
                    list.Add(prefix + mediaMid + ext);
            }
            switch (q)
            {
                case "flac":
                    AddMedia("F000", ".flac");
                    AddMedia("M800", ".mp3");
                    AddSong("C400", ".m4a");
                    AddMedia("M500", ".mp3");
                    break;
                case "mp3_320":
                    AddMedia("M800", ".mp3");
                    AddMedia("M500", ".mp3");
                    AddSong("C400", ".m4a");
                    break;
                case "aac_128":
                default:
                    AddSong("C400", ".m4a");
                    AddMedia("M500", ".mp3");
                    AddMedia("M800", ".mp3");
                    break;
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

        private static async Task<string?> TryFetchMediaMid(string songMid, string? cookie, string? referer)
        {
            try
            {
                var dataObj = new
                {
                    req = new
                    {
                        module = "music.pf_song_detail_svr",
                        method = "get_song_detail_yqq",
                        param = new { song_mid = songMid, song_type = 0 }
                    },
                    comm = new { ct = 24, cv = 0 }
                };
                string dataJson = JsonSerializer.Serialize(dataObj);
                string url = "https://u.y.qq.com/cgi-bin/musics.fcg?format=json&inCharset=utf8&outCharset=utf-8&needNewCode=0&platform=yqq.json&data=" + Uri.EscapeDataString(dataJson);
                var req = WebWrapper.Request(url)
                    .WithHeader("Referer", string.IsNullOrWhiteSpace(referer) ? "https://y.qq.com/" : referer)
                    .WithHeader("Origin", "https://y.qq.com");
                if (!string.IsNullOrWhiteSpace(cookie)) req = req.WithHeader("Cookie", cookie);
                string json = await req.AsString();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("req", out var r) || !r.TryGetProperty("data", out var d))
                    return null;
                if (!d.TryGetProperty("track_info", out var ti)) return null;
                // fields can be file.media_mid or strMediaMid
                if (ti.TryGetProperty("file", out var file) && file.TryGetProperty("media_mid", out var mm) && mm.ValueKind == JsonValueKind.String)
                    return mm.GetString();
                if (ti.TryGetProperty("strMediaMid", out var sm) && sm.ValueKind == JsonValueKind.String)
                    return sm.GetString();
                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "QQMusic: Failed to fetch media_mid for {0}", songMid);
                return null;
            }
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
