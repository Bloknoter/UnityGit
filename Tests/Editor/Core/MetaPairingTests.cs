#nullable enable
using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class MetaPairingTests
    {
        static FileChange C(string path, ChangeKind kind = ChangeKind.Modified, string? orig = null, bool conflicted = false, bool staged = false)
            => new FileChange { Path = path, Kind = kind, OriginalPath = orig, IsConflicted = conflicted, IsStaged = staged };

        [Test] public void AssetAndMeta_BecomeOneRow()
        {
            var rows = MetaPairing.Group(new[] { C("Assets/A.png", ChangeKind.Untracked), C("Assets/A.png.meta", ChangeKind.Untracked) });
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Path, Is.EqualTo("Assets/A.png"));
            Assert.That(rows[0].GitPaths, Is.EquivalentTo(new[] { "Assets/A.png", "Assets/A.png.meta" }));
            Assert.That(rows[0].HasMeta, Is.True);
            Assert.That(rows[0].Kind, Is.EqualTo(ChangeKind.Untracked));
        }
        [Test] public void MetaOnly_IsItsOwnRow()
            => Assert.That(MetaPairing.Group(new[] { C("Assets/A.png.meta") }).Single().Path, Is.EqualTo("Assets/A.png.meta"));
        [Test] public void FolderMeta_IsItsOwnRow()
            => Assert.That(MetaPairing.Group(new[] { C("Assets/Folder.meta", ChangeKind.Untracked), C("Assets/Folder/X.cs", ChangeKind.Untracked) }), Has.Count.EqualTo(2));
        [Test] public void RenamedPair_CarriesAllFourPaths()
        {
            var row = MetaPairing.Group(new[] { C("Assets/B.png", ChangeKind.Renamed, "Assets/A.png"), C("Assets/B.png.meta", ChangeKind.Renamed, "Assets/A.png.meta") }).Single();
            Assert.That(row.GitPaths, Is.EquivalentTo(new[] { "Assets/B.png", "Assets/A.png", "Assets/B.png.meta", "Assets/A.png.meta" }));
            Assert.That(row.OriginalPath, Is.EqualTo("Assets/A.png"));
        }
        [Test] public void ConflictedMeta_MakesRowConflicted()
            => Assert.That(MetaPairing.Group(new[] { C("Assets/S.unity"), C("Assets/S.unity.meta", ChangeKind.Conflicted, conflicted: true) }).Single().IsConflicted, Is.True);
        [Test] public void StagedMeta_MakesRowStaged()
            => Assert.That(MetaPairing.Group(new[] { C("Assets/S.unity"), C("Assets/S.unity.meta", staged: true) }).Single().IsStaged, Is.True);
        [Test] public void Rows_AreSortedByPathIgnoringCase()
            => Assert.That(MetaPairing.Group(new[] { C("b.txt"), C("A.txt") }).Select(r => r.Path), Is.EqualTo(new[] { "A.txt", "b.txt" }));
        [Test] public void UnicodePath_PairsWithItsMeta()
            => Assert.That(MetaPairing.Group(new[] { C("Assets/Мои сцены/Level 1.unity"), C("Assets/Мои сцены/Level 1.unity.meta") }), Has.Count.EqualTo(1));
    }
}
