using System.Text.RegularExpressions;

namespace Rbrowser.Core;

// Parses a conservative subset of uBO static rules. Unknown modifiers are never broadened.
public sealed class FilterEngine
{
    sealed class Rule(string pattern, bool matchCase, bool allow, bool important, string[] domains, bool? thirdParty, string[] types, string[] notTypes)
    {
        public readonly string Pattern=pattern;
        public readonly bool MatchCase=matchCase,Allow=allow,Important=important;
        public readonly string[] Domains=domains,Types=types,NotTypes=notTypes;
        public readonly bool? ThirdParty=thirdParty;
        Regex? regex;
        public bool Matches(string url)=>LazyInitializer.EnsureInitialized(ref regex,()=>new Regex(Pattern,MatchCase?RegexOptions.None:RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(15))).IsMatch(url);
    }
    sealed record Cosmetic(string[] Domains, string Selector, bool Exception);
    readonly Dictionary<string, List<Rule>> index = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Rule> fallback = [];
    readonly List<Cosmetic> cosmetics = [];
    readonly HashSet<string> bad = new(StringComparer.Ordinal);
    readonly PublicSuffix suffix;
    public int Supported { get; private set; }
    public int Skipped { get; private set; }
    readonly string publicSuffix;
    public FilterEngine(string publicSuffix = "") {this.publicSuffix=publicSuffix;suffix = new(publicSuffix);}
    static readonly Regex Words = new("[a-zA-Z0-9%]{3,}", RegexOptions.Compiled);
    static readonly HashSet<string> ResourceTypes = ["script", "image", "stylesheet", "font", "media", "xmlhttprequest", "subdocument", "document", "other"];
    public void Add(string text)
    {
        // Conditional branches require uBO capabilities; skip whole conditional sections safely.
        var depth = 0;
        foreach (var original in text.Split('\n'))
        {
            var line = original.Trim();
            if (line.StartsWith("!#if")) { depth++; continue; }
            if (line.StartsWith("!#endif")) { depth = Math.Max(0, depth - 1); continue; }
            if (line.Length == 0 || line.StartsWith('!') || line.StartsWith('[')) continue;
            if (depth > 0) { Skipped++; continue; }
            var cosmetic = line.IndexOf("#@#", StringComparison.Ordinal);
            var except = cosmetic >= 0;
            if (cosmetic < 0) cosmetic = line.IndexOf("##", StringComparison.Ordinal);
            if (cosmetic >= 0)
            {
                var selector = line[(cosmetic + (except ? 3 : 2))..];
                if (selector.Contains("+js(") || Regex.IsMatch(selector,@":(?:has-text|matches-[\w-]+|xpath|upward|remove|style|watch-[\w-]+|others|subject|min-text-length|if|if-not)\b") || selector.Contains('{') || selector.Contains('}') || selector.Contains('<') || selector.Contains('\\')) { Skipped++; continue; }
                cosmetics.Add(new(line[..cosmetic].Split(',', StringSplitOptions.RemoveEmptyEntries), selector, except)); Supported++; continue;
            }
            if (line.Contains('#') || line.Contains("!#")) { Skipped++; continue; }
            if (line.EndsWith(",badfilter") || line.EndsWith("$badfilter")) { bad.Add(line.Replace(",badfilter", "").Replace("$badfilter", "")); continue; }
            Parse(line);
        }
    }
    void Parse(string line)
    {
        var original = line;
        var allow = line.StartsWith("@@"); if (allow) line = line[2..];
        var split = line.LastIndexOf('$');
        var pattern = split >= 0 ? line[..split] : line;
        var options = split >= 0 ? line[(split + 1)..].Split(',') : [];
        string[] domains = []; bool? third = null; bool important = false, matchCase = false;
        var types = new List<string>(); var notTypes = new List<string>();
        foreach (var option in options)
        {
            var neg = option.StartsWith('~'); var key = neg ? option[1..] : option;
            key = key switch { "xhr" => "xmlhttprequest", "frame" => "subdocument", _ => key };
            if (key.StartsWith("domain=") || key.StartsWith("from=")) domains = key[(key.IndexOf('=') + 1)..].Split('|');
            else if (key is "third-party" or "3p") third = !neg;
            else if (key is "first-party" or "1p") third = neg;
            else if (key == "important" && !neg) important = true;
            else if (key == "match-case" && !neg) matchCase = true;
            else if (ResourceTypes.Contains(key)) (neg ? notTypes : types).Add(key);
            else { Skipped++; return; }
        }
        if (domains.Any(d => d.Contains('*') || d.Contains('/') || d.Contains('>')) || pattern.Length == 0 || (pattern.Length>1&&pattern.StartsWith('/')&&pattern.EndsWith('/'))) { Skipped++; return; }
        var hostAnchor = pattern.StartsWith("||"); if (hostAnchor) pattern = pattern[2..];
        var start = pattern.StartsWith('|'); if (start) pattern = pattern[1..];
        var end = pattern.EndsWith('|'); if (end) pattern = pattern[..^1];
        var rx = Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\^", "(?:[^a-zA-Z0-9_\\-.%]|$)");
        rx = (hostAnchor ? @"^[a-z]+://(?:[^/?#]*\.)?" : start ? "^" : "") + rx + (end ? "$" : "");
        try
        {
            var rule = new Rule(rx,matchCase,allow,important,domains,third,[.. types],[.. notTypes]);
            sources[rule] = original;
            var token = Words.Matches(pattern).Select(m => m.Value.ToLowerInvariant()).OrderByDescending(t => t.Length).FirstOrDefault();
            if(token!=null)token=token[..3];
            if (token == null) fallback.Add(rule);
            else { if (!index.TryGetValue(token, out var bucket)) index[token] = bucket = []; bucket.Add(rule); }
            Supported++;
        }
        catch (ArgumentException) { Skipped++; }
    }
    readonly Dictionary<Rule, string> sources = [];
    public bool Blocks(string url, string page, string type)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) || !Uri.TryCreate(page, UriKind.Absolute, out var source)) return false;
        var third = suffix.Site(target.IdnHost) != suffix.Site(source.IdnHost);
        var block = false; var allow = false;
        var tokens=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(Match word in Words.Matches(url))for(var i=0;i<=word.Length-3;i++)tokens.Add(word.Value.Substring(i,3));
        var candidates = tokens.Where(index.ContainsKey).SelectMany(k => index[k]).Concat(fallback);
        foreach (var r in candidates)
        {
            if (bad.Contains(sources[r]) || !DomainMatches(r.Domains, source.IdnHost) || (r.ThirdParty != null && r.ThirdParty != third) || (r.Types.Length > 0 && !r.Types.Contains(type)) || r.NotTypes.Contains(type)) continue;
            try { if (!r.Matches(url)) continue; } catch (RegexMatchTimeoutException) { continue; }
            if (r.Important && !r.Allow) return true;
            if (r.Allow) allow = true; else block = true;
        }
        return block && !allow;
    }
    public string Css(string host)
    {
        var exceptions = cosmetics.Where(c => c.Exception && DomainMatches(c.Domains, host)).Select(c => c.Selector).ToHashSet();
        return string.Join("\n", cosmetics.Where(c => !c.Exception && DomainMatches(c.Domains, host) && !exceptions.Contains(c.Selector)).Select(c => c.Selector + "{display:none!important}"));
    }
    public void Save(Stream stream)
    {
        using var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true);
        w.Write("RbrowserFilters/2");w.Write(publicSuffix);w.Write(Supported);w.Write(Skipped);
        void Strings(string[] values){w.Write(values.Length);foreach(var s in values)w.Write(s);}
        w.Write(sources.Count);
        foreach(var pair in sources){var r=pair.Key;w.Write(pair.Value);w.Write(r.Pattern);w.Write(r.MatchCase);w.Write(r.Allow);w.Write(r.Important);Strings(r.Domains);w.Write(r.ThirdParty==null?(sbyte)-1:r.ThirdParty.Value?(sbyte)1:(sbyte)0);Strings(r.Types);Strings(r.NotTypes);}
        w.Write(index.Count);var ids=sources.Keys.Select((r,i)=>(r,i)).ToDictionary(p=>p.r,p=>p.i);
        foreach(var pair in index){w.Write(pair.Key);w.Write(pair.Value.Count);foreach(var r in pair.Value)w.Write(ids[r]);}
        w.Write(fallback.Count);foreach(var r in fallback)w.Write(ids[r]);
        Strings([..bad]);w.Write(cosmetics.Count);foreach(var c in cosmetics){Strings(c.Domains);w.Write(c.Selector);w.Write(c.Exception);}
    }
    public static FilterEngine Load(Stream stream)
    {
        using var r=new BinaryReader(stream,System.Text.Encoding.UTF8,true);
        if(r.ReadString()!="RbrowserFilters/2")throw new InvalidDataException("Filter cache version");
        var engine=new FilterEngine(r.ReadString()){Supported=r.ReadInt32(),Skipped=r.ReadInt32()};
        int Count(){var n=r.ReadInt32();if(n<0||n>500000)throw new InvalidDataException("Filter cache size");return n;}
        string[] Strings(){var values=new string[Count()];for(var i=0;i<values.Length;i++)values[i]=r.ReadString();return values;}
        var rules=new Rule[Count()];
        for(var i=0;i<rules.Length;i++){var source=r.ReadString();var pattern=r.ReadString();var matchCase=r.ReadBoolean();var allow=r.ReadBoolean();var important=r.ReadBoolean();var domains=Strings();var third=r.ReadSByte();rules[i]=new(pattern,matchCase,allow,important,domains,third<0?null:third==1,Strings(),Strings());engine.sources[rules[i]]=source;}
        var buckets=Count();for(var i=0;i<buckets;i++){var token=r.ReadString();var list=new List<Rule>();var n=Count();for(var j=0;j<n;j++)list.Add(rules[r.ReadInt32()]);engine.index[token]=list;}
        var f=Count();for(var i=0;i<f;i++)engine.fallback.Add(rules[r.ReadInt32()]);
        engine.bad.UnionWith(Strings());var c=Count();for(var i=0;i<c;i++)engine.cosmetics.Add(new(Strings(),r.ReadString(),r.ReadBoolean()));
        return engine;
    }
    static bool DomainMatches(string[] domains, string host)
    {
        static bool Match(string host, string d) => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase);
        if (domains.Any(d => d.StartsWith('~') && Match(host, d[1..]))) return false;
        var positive = domains.Where(d => !d.StartsWith('~')).ToArray();
        return positive.Length == 0 || positive.Any(d => Match(host, d));
    }
}
public sealed class PublicSuffix
{
    readonly HashSet<string> rules = new(StringComparer.OrdinalIgnoreCase);
    public PublicSuffix(string data) { foreach (var s in data.Split('\n').Select(l => l.Trim())) if (s.Length > 0 && !s.StartsWith("//")) rules.Add(s); }
    public string Site(string host)
    {
        if(System.Net.IPAddress.TryParse(host,out _))return host;
        var parts = host.Split('.'); var count = 1;
        for (var i = 0; i < parts.Length; i++)
        {
            var tail = string.Join('.', parts[i..]);
            if (rules.Contains("!" + tail)) { count = parts.Length - i - 1; break; }
            if (rules.Contains(tail) || (i + 1 < parts.Length && rules.Contains("*." + string.Join('.', parts[(i + 1)..])))) count = Math.Max(count, parts.Length - i);
        }
        return string.Join('.', parts[Math.Max(0, parts.Length - count - 1)..]);
    }
}
