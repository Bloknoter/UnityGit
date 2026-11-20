#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit.Tests
{
    /// <summary>One file of a merge scenario; null means the file is absent in that version.</summary>
    internal sealed class ConflictFile
    {
        public string Path = "";
        public byte[]? Base;
        public byte[]? Ours;
        public byte[]? Theirs;
    }

    internal static class ConflictScenarios
    {
        static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);
        static byte[] Bytes(string text) => Encoding.GetEncoding("ISO-8859-1").GetBytes(text);

        public static ConflictFile Text(string path, string baseText, string ours, string theirs)
            => new ConflictFile { Path = path, Base = Utf8(baseText), Ours = Utf8(ours), Theirs = Utf8(theirs) };

        public static readonly ConflictFile TwoPlaces = Text("f.txt", "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\n",
            "one\nTWO main\nthree\nfour\nfive\nsix\nseven\nEIGHT main\nnine\n",
            "one\nTWO feature\nthree\nfour\nfive\nsix\nseven\nEIGHT feature\nnine\n");

        public static readonly ConflictFile Binary = new ConflictFile
        {
            Path = "img.bin", Base = Bytes("B\0base"), Ours = Bytes("B\0main"), Theirs = Bytes("B\0feature"),
        };

        public static readonly ConflictFile DeletedByThem = new ConflictFile
        {
            Path = "gone.txt", Base = Utf8("keep\n"), Ours = Utf8("keep changed\n"), Theirs = null,
        };

        public static readonly ConflictFile Scene = Text("Assets/Level.unity", "a: 1\n", "a: 2\n", "a: 3\n");
        public static readonly ConflictFile SceneMeta = Text("Assets/Level.unity.meta", "guid: 1\n", "guid: 2\n", "guid: 3\n");

        /// <summary>Leaves <paramref name="repo"/> in a conflicted merge of branch feature into main.</summary>
        public static async Task MergeAsync(GitRepository repo, params ConflictFile[] files)
        {
            foreach (var f in files) if (f.Base != null) Write(repo, f.Path, f.Base);
            await CommitAllAsync(repo, "base");

            Ok(await repo.RunRawAsync("checkout", "-b", "feature"), "checkout feature");
            Apply(repo, files, f => f.Theirs);
            await CommitAllAsync(repo, "feature");

            Ok(await repo.RunRawAsync("checkout", "main"), "checkout main");
            Apply(repo, files, f => f.Ours);
            await CommitAllAsync(repo, "main");

            var merge = await repo.RunRawAsync("merge", "feature");
            if (merge.Succeeded) throw new InvalidOperationException("The merge did not conflict.");
            if (repo.GetState() != RepositoryState.Merging)
                throw new InvalidOperationException("The merge did not stop in the merging state: " + merge.StandardError);
        }

        static void Apply(GitRepository repo, ConflictFile[] files, Func<ConflictFile, byte[]?> pick)
        {
            foreach (var f in files)
            {
                var content = pick(f);
                if (content != null) Write(repo, f.Path, content);
                else File.Delete(Path.Combine(repo.WorkingDirectory, f.Path));
            }
        }

        static void Write(GitRepository repo, string relativePath, byte[] content)
        {
            var path = Path.Combine(repo.WorkingDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        static async Task CommitAllAsync(GitRepository repo, string message)
        {
            Ok(await repo.RunRawAsync("add", "-A"), "add");
            var result = await repo.CommitAsync(message);
            if (!result.Succeeded) throw new InvalidOperationException("Commit failed: " + result.Message);
        }

        static void Ok(GitResult result, string what)
        {
            if (!result.Succeeded) throw new InvalidOperationException($"git {what} failed: {result.StandardError}");
        }
    }
}
