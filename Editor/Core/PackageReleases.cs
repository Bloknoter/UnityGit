#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UnityGit
{
    /// <summary>The two packages the Updates tab manages.</summary>
    public static class PackageNames
    {
        public const string UnityGit = "com.bloknoter.unitygit";
        public const string FlexGit = "com.bloknoter.flexgit";

        /// <summary>The name shown to people: "UnityGit" or "FlexGit".</summary>
        public static string Display(string name)
            => name == UnityGit ? "UnityGit" : name == FlexGit ? "FlexGit" : name;

        /// <summary>The tag prefix a package's releases use when the installed revision doesn't show one.</summary>
        public static string DefaultTagPrefix(string name) => name == FlexGit ? "upm/v" : "v";
    }

    /// <summary>A release version: exactly three numbers, <c>major.minor.patch</c>. Pre-releases aren't versions here.</summary>
    public readonly struct SemVer : IComparable<SemVer>, IEquatable<SemVer>
    {
        public SemVer(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public static bool TryParse(string? text, out SemVer version)
        {
            version = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text!.Split('.');
            if (parts.Length != 3) return false;
            var numbers = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var part = parts[i];
                if (part.Length == 0 || part.Length > 9 || !part.All(c => c >= '0' && c <= '9')) return false;
                numbers[i] = int.Parse(part, CultureInfo.InvariantCulture);
            }
            version = new SemVer(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        public static SemVer Parse(string text)
            => TryParse(text, out var v) ? v : throw new FormatException($"Not a version: {text}");

        public int CompareTo(SemVer other)
        {
            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            return Patch.CompareTo(other.Patch);
        }

        public bool Equals(SemVer other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        public override bool Equals(object? obj) => obj is SemVer other && Equals(other);
        public override int GetHashCode() => (Major * 397 ^ Minor) * 397 ^ Patch;
        public override string ToString() => $"{Major}.{Minor}.{Patch}";

        public static bool operator ==(SemVer a, SemVer b) => a.Equals(b);
        public static bool operator !=(SemVer a, SemVer b) => !a.Equals(b);
        public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
        public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
        public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
        public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
    }

    /// <summary>One release: a tag named prefix + version, and the commit it points at.</summary>
    public sealed class ReleaseTag
    {
        public ReleaseTag(SemVer version, string tag, string sha)
        {
            Version = version;
            Tag = tag;
            Sha = sha;
        }

        public SemVer Version { get; }
        public string Tag { get; }
        public string Sha { get; }
    }

    public static class ReleaseTags
    {
        const string TagsRef = "refs/tags/";
        const string Peeled = "^{}";

        /// <summary>
        /// The releases in <c>git ls-remote --tags</c> output: tags named exactly <paramref name="prefix"/> + a version,
        /// newest first. An annotated tag's sha is the commit it points at. Other tags (pre-releases, other prefixes) are
        /// left out, so FlexGit's plain <c>v2.2.0</c> tags never show up under <c>upm/v</c> and the reverse.
        /// </summary>
        public static IReadOnlyList<ReleaseTag> Parse(string lsRemoteOutput, string prefix)
        {
            var shas = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in lsRemoteOutput.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                var sha = line.Substring(0, tab);
                var refName = line.Substring(tab + 1);
                if (!refName.StartsWith(TagsRef, StringComparison.Ordinal)) continue;
                var tag = refName.Substring(TagsRef.Length);
                var peeled = tag.EndsWith(Peeled, StringComparison.Ordinal);
                if (peeled) tag = tag.Substring(0, tag.Length - Peeled.Length);
                if (peeled || !shas.ContainsKey(tag)) shas[tag] = sha; // the peeled line names the commit
            }

            var releases = new List<ReleaseTag>();
            foreach (var pair in shas)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (SemVer.TryParse(pair.Key.Substring(prefix.Length), out var version))
                    releases.Add(new ReleaseTag(version, pair.Key, pair.Value));
            }
            releases.Sort((a, b) => b.Version.CompareTo(a.Version));
            return releases;
        }

        /// <summary>
        /// The tag prefix in an installed revision: <c>upm/v2.0.0</c> → <c>upm/v</c>, <c>v1.0.0</c> → <c>v</c>. Null when the
        /// revision doesn't end in a version (a branch, a commit hash, a pre-release, none).
        /// </summary>
        public static string? PrefixOf(string? revision)
        {
            if (string.IsNullOrEmpty(revision)) return null;
            var end = revision!.Length;
            var start = end;
            while (start > 0 && (char.IsDigit(revision[start - 1]) || revision[start - 1] == '.')) start--;
            // The version is the longest run of digits and dots at the end; it must start with a digit.
            while (start < end && revision[start] == '.') start++;
            return SemVer.TryParse(revision.Substring(start), out _) ? revision.Substring(0, start) : null;
        }
    }

    public enum PackageSource { Missing, Git, Embedded, Local, Registry, Other }

    /// <summary>
    /// A package as the project has it: where it comes from, its version, and for a git package the manifest entry
    /// (<c>url#revision</c>) split into what Package Manager and git each need.
    /// </summary>
    public sealed class InstalledPackage
    {
        public InstalledPackage(string name, PackageSource source, SemVer? version, string? manifestEntry = null, SemVer? requiredFlexGit = null)
        {
            Name = name;
            Source = source;
            Version = version;
            ManifestEntry = manifestEntry;
            RequiredFlexGit = requiredFlexGit;

            if (source == PackageSource.Git && !string.IsNullOrEmpty(manifestEntry))
            {
                var hash = manifestEntry!.IndexOf('#');
                InstallUrl = hash < 0 ? manifestEntry : manifestEntry.Substring(0, hash);
                Revision = hash < 0 || hash == manifestEntry.Length - 1 ? null : manifestEntry.Substring(hash + 1);
                var git = InstallUrl;
                var query = git.IndexOf('?');
                if (query >= 0) git = git.Substring(0, query);
                if (git.StartsWith("git+", StringComparison.Ordinal)) git = git.Substring(4);
                GitUrl = git;
            }
            TagPrefix = ReleaseTags.PrefixOf(Revision) ?? PackageNames.DefaultTagPrefix(name);
        }

        public string Name { get; }
        public string DisplayName => PackageNames.Display(Name);
        public PackageSource Source { get; }
        public SemVer? Version { get; }
        /// <summary>The package's own entry in <c>Packages/manifest.json</c>, as written there.</summary>
        public string? ManifestEntry { get; }
        /// <summary>For UnityGit: the FlexGit version its <c>package.json</c> depends on.</summary>
        public SemVer? RequiredFlexGit { get; }

        /// <summary>The entry without its revision, as Package Manager reads it: <c>git+</c> and <c>?path=</c> kept.</summary>
        public string? InstallUrl { get; }
        /// <summary>The repository URL for git commands: no <c>git+</c> scheme prefix and no <c>?path=</c> query.</summary>
        public string? GitUrl { get; }
        /// <summary>What follows <c>#</c> in the entry: a tag, a branch or a commit; null when there is none.</summary>
        public string? Revision { get; }
        /// <summary>What this package's release tags start with: <c>v</c> for UnityGit, <c>upm/v</c> for FlexGit.</summary>
        public string TagPrefix { get; }

        /// <summary>True when UnityGit may change it: a git package with a URL in the manifest.</summary>
        public bool CanInstall => Source == PackageSource.Git && InstallUrl != null;
    }

    /// <summary>The parts of a <c>package.json</c> (or <c>manifest.json</c>) the Updates tab reads.</summary>
    public sealed class PackageManifest
    {
        public string? Version { get; set; }
        public string? Unity { get; set; }
        public IReadOnlyDictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>();
    }

    public static class PackageJson
    {
        /// <summary>Reads <c>version</c>, <c>unity</c> and the string entries of <c>dependencies</c>. Null when it isn't a JSON object.</summary>
        public static PackageManifest? Read(string text)
        {
            if (!(MiniJson.Parse(text) is Dictionary<string, object?> root)) return null;
            var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetValue("dependencies", out var deps) && deps is Dictionary<string, object?> map)
                foreach (var pair in map)
                    if (pair.Value is string value) dependencies[pair.Key] = value;
            return new PackageManifest
            {
                Version = root.TryGetValue("version", out var v) ? v as string : null,
                Unity = root.TryGetValue("unity", out var u) ? u as string : null,
                Dependencies = dependencies,
            };
        }

        /// <summary>
        /// True when an editor of <paramref name="editorVersion"/> (<c>6000.4.1f1</c>) meets a package's <c>unity</c>
        /// field (<c>6000.3</c>). A missing or unreadable field never blocks.
        /// </summary>
        public static bool UnitySatisfies(string? required, string editorVersion)
        {
            if (!TryMajorMinor(required, out var need) || !TryMajorMinor(editorVersion, out var have)) return true;
            return have.major != need.major ? have.major > need.major : have.minor >= need.minor;
        }

        static bool TryMajorMinor(string? text, out (int major, int minor) version)
        {
            version = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text!.Split('.');
            if (parts.Length < 2) return false;
            var minorDigits = new string(parts[1].TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)) return false;
            if (!int.TryParse(minorDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)) return false;
            version = (major, minor);
            return true;
        }
    }

    /// <summary>
    /// A small JSON reader: objects become dictionaries, arrays lists, numbers doubles. Returns null for anything that
    /// isn't valid JSON. FlexGit has one too, but it's internal to FlexGit.
    /// </summary>
    internal sealed class MiniJson
    {
        readonly string _s;
        int _i;

        MiniJson(string s) { _s = s; }

        public static object? Parse(string text)
        {
            var reader = new MiniJson(text ?? string.Empty);
            try
            {
                reader.SkipSpace();
                var value = reader.ReadValue();
                reader.SkipSpace();
                return reader._i == reader._s.Length ? value : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        object? ReadValue()
        {
            if (_i >= _s.Length) throw new FormatException();
            switch (_s[_i])
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default: return ReadNumber();
            }
        }

        Dictionary<string, object?> ReadObject()
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            _i++; // {
            SkipSpace();
            if (Peek() == '}') { _i++; return result; }
            while (true)
            {
                SkipSpace();
                if (Peek() != '"') throw new FormatException();
                var key = ReadString();
                SkipSpace();
                if (Peek() != ':') throw new FormatException();
                _i++;
                SkipSpace();
                result[key] = ReadValue();
                SkipSpace();
                var c = Next();
                if (c == '}') return result;
                if (c != ',') throw new FormatException();
            }
        }

        List<object?> ReadArray()
        {
            var result = new List<object?>();
            _i++; // [
            SkipSpace();
            if (Peek() == ']') { _i++; return result; }
            while (true)
            {
                SkipSpace();
                result.Add(ReadValue());
                SkipSpace();
                var c = Next();
                if (c == ']') return result;
                if (c != ',') throw new FormatException();
            }
        }

        string ReadString()
        {
            _i++; // "
            var sb = new StringBuilder();
            while (true)
            {
                var c = Next();
                if (c == '"') return sb.ToString();
                if (c < ' ') throw new FormatException();
                if (c != '\\') { sb.Append(c); continue; }
                var e = Next();
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) throw new FormatException();
                        if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) throw new FormatException();
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default: throw new FormatException();
                }
            }
        }

        double ReadNumber()
        {
            var start = _i;
            while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
            if (start == _i) throw new FormatException();
            if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw new FormatException();
            return number;
        }

        void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new FormatException();
            _i += word.Length;
        }

        char Peek() => _i < _s.Length ? _s[_i] : throw new FormatException();
        char Next() => _i < _s.Length ? _s[_i++] : throw new FormatException();

        void SkipSpace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }
}
