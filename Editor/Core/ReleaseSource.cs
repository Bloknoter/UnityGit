#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit
{
    /// <summary>A package repository's releases, or why they couldn't be read.</summary>
    public sealed class ReleaseList
    {
        public IReadOnlyList<ReleaseTag> Tags { get; set; } = Array.Empty<ReleaseTag>();
        public string? Error { get; set; }
        public string? Details { get; set; }
    }

    /// <summary>A release's <c>package.json</c>, or why it couldn't be read.</summary>
    public sealed class PackageRead
    {
        public PackageManifest? Package { get; set; }
        public string? Error { get; set; }
        public string? Details { get; set; }
    }

    /// <summary>
    /// Reads package releases from their git repositories: <c>git ls-remote --tags</c> lists them without downloading
    /// anything, and a one-commit fetch of a tag into a bare cache repository reads its <c>package.json</c>. FlexGit runs
    /// git only inside a repository, so the cache repository is also where <c>ls-remote</c> runs. Every call has a
    /// timeout and never asks for credentials: it runs without anyone waiting for it, at editor start.
    /// </summary>
    public sealed class ReleaseSource
    {
        static readonly Regex SafeTag = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._/+-]*$", RegexOptions.CultureInvariant);

        // No terminal prompt (FlexGit sets that too), no Git Credential Manager window, no ssh password dialog.
        static readonly Dictionary<string, string?> NoPrompts = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "never",
            ["SSH_ASKPASS_REQUIRE"] = "never",
        };

        readonly string _cacheDirectory;
        readonly GitOptions _options;
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, PackageManifest> _packages = new Dictionary<string, PackageManifest>(StringComparer.Ordinal);
        GitRepository? _repo;

        public ReleaseSource(string cacheDirectory) : this(cacheDirectory, new GitOptions()) { }

        internal ReleaseSource(string cacheDirectory, GitOptions options)
        {
            _cacheDirectory = Path.GetFullPath(cacheDirectory);
            _options = options;
        }

        /// <summary>How long one git call may take before it is stopped.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>The releases at <paramref name="gitUrl"/> whose tags are <paramref name="tagPrefix"/> + a version, newest first.</summary>
        public async Task<ReleaseList> ListAsync(string gitUrl, string tagPrefix, CancellationToken cancellationToken = default)
        {
            if (!IsUrl(gitUrl)) return new ReleaseList { Error = "Not a repository URL: " + gitUrl };
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await RunAsync(new[] { "ls-remote", "--tags", gitUrl }, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded) return new ReleaseList { Error = Message(result), Details = Details(result) };
                return new ReleaseList { Tags = ReleaseTags.Parse(result.StandardOutput, tagPrefix) };
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
            {
                return new ReleaseList { Error = ExceptionMessage(ex) };
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>The <c>package.json</c> of release <paramref name="tag"/> at <paramref name="gitUrl"/>. Remembered once read.</summary>
        public async Task<PackageRead> ReadPackageAsync(string gitUrl, string tag, CancellationToken cancellationToken = default)
        {
            if (!IsUrl(gitUrl)) return new PackageRead { Error = "Not a repository URL: " + gitUrl };
            if (!SafeTag.IsMatch(tag) || tag.Contains("..")) return new PackageRead { Error = "Not a tag name: " + tag };
            var key = gitUrl + "\n" + tag;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_packages.TryGetValue(key, out var known)) return new PackageRead { Package = known };

                // The ref holds a hash of the URL, so the same tag in two repositories (a fork and its original) never mixes.
                var local = $"refs/unitygit/{Hash(gitUrl)}/tags/{tag}";
                var fetchArgs = new[] { "fetch", "--depth=1", "--no-tags", gitUrl, $"+refs/tags/{tag}:{local}" };
                var fetch = await RunAsync(fetchArgs, cancellationToken).ConfigureAwait(false);
                if (!fetch.Succeeded && !IsRemoteProblem(fetch))
                {
                    // A git process stopped mid-fetch (a domain reload) can leave lock files behind: start the cache over once.
                    await RecreateAsync(cancellationToken).ConfigureAwait(false);
                    fetch = await RunAsync(fetchArgs, cancellationToken).ConfigureAwait(false);
                }
                if (!fetch.Succeeded) return new PackageRead { Error = Message(fetch), Details = Details(fetch) };

                var show = await RunAsync(new[] { "show", local + ":package.json" }, cancellationToken).ConfigureAwait(false);
                if (!show.Succeeded) return new PackageRead { Error = $"{tag} has no package.json", Details = Details(show) };
                var package = PackageJson.Read(show.StandardOutput);
                if (package == null) return new PackageRead { Error = $"The package.json of {tag} can't be read" };
                _packages[key] = package;
                return new PackageRead { Package = package };
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
            {
                return new PackageRead { Error = ExceptionMessage(ex) };
            }
            finally
            {
                _gate.Release();
            }
        }

        async Task<GitResult> RunAsync(string[] arguments, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(Timeout);
                var repo = await RepoAsync(timeout.Token).ConfigureAwait(false);
                return await repo.RunRawAsync(arguments, new RawCommandOptions { Environment = NoPrompts }, timeout.Token).ConfigureAwait(false);
            }
        }

        // The cache must be its own bare repository. It sits inside the project folder, which is often a repository itself, so
        // a folder git doesn't accept as a repository would otherwise open the project's: anything else is replaced.
        async Task<GitRepository> RepoAsync(CancellationToken cancellationToken)
        {
            if (_repo != null) return _repo;
            if (File.Exists(Path.Combine(_cacheDirectory, "HEAD")))
            {
                try
                {
                    var repo = await GitRepository.OpenAsync(_cacheDirectory, _options, cancellationToken).ConfigureAwait(false);
                    if (repo.IsBare && SamePath(repo.GitDirectory, _cacheDirectory)) return _repo = repo;
                }
                catch (GitCommandException)
                {
                    // Not a repository: replaced below.
                }
            }
            return await CreateAsync(cancellationToken).ConfigureAwait(false);
        }

        Task<GitRepository> RecreateAsync(CancellationToken cancellationToken)
        {
            _repo = null;
            return CreateAsync(cancellationToken);
        }

        async Task<GitRepository> CreateAsync(CancellationToken cancellationToken)
        {
            DeleteDirectory(_cacheDirectory);
            return _repo = await GitRepository.InitAsync(_cacheDirectory, "main", true, _options, cancellationToken).ConfigureAwait(false);
        }

        static bool IsUrl(string url) => !string.IsNullOrWhiteSpace(url) && !url.StartsWith("-", StringComparison.Ordinal);

        static bool IsRemoteProblem(GitResult result)
        {
            var kind = result.ToError().Kind;
            return kind == GitErrorKind.NetworkError || kind == GitErrorKind.AuthenticationFailed || kind == GitErrorKind.RemoteNotFound;
        }

        static string Message(GitResult result)
        {
            var error = result.ToError();
            var firstLine = (result.StandardError ?? string.Empty).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return OutcomeMessages.ForErrorKind(error.Kind, firstLine ?? error.Message);
        }

        static string? Details(GitResult result) => string.IsNullOrWhiteSpace(result.StandardError) ? null : result.StandardError.Trim();

        string ExceptionMessage(Exception ex)
        {
            if (ex is OperationCanceledException) return $"No answer after {Timeout.TotalSeconds:0} seconds — check your connection";
            if (ex is GitCommandException git) return OutcomeMessages.ForErrorKind(git.Error.Kind, git.Error.Message);
            return ex.Message;
        }

        static string Hash(string text)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return string.Concat(bytes.Take(6).Select(b => b.ToString("x2")));
            }
        }

        static bool SamePath(string a, string b)
            => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

        static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal); // git makes its object files read-only
            Directory.Delete(path, true);
        }
    }
}
