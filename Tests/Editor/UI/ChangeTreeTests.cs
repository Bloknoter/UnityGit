#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityGit.UI;

namespace UnityGit.Tests
{
    public class ChangeTreeTests
    {
        static FileChange C(string path, ChangeKind kind = ChangeKind.Modified, string? orig = null, bool conflicted = false)
            => new FileChange { Path = path, Kind = kind, OriginalPath = orig, IsConflicted = conflicted };

        static string[] Keys(IEnumerable<ChangeEntry> e) => e.Select(x => x.Key).ToArray();

        static ChangeEntry E(IReadOnlyList<ChangeEntry> e, string key) => e.Single(x => x.Key == key);

        [Test] public void Hidden_IsTheRowsAsTheyAre()
        {
            var rows = MetaPairing.Group(new[] { C("b/x.txt"), C("a.txt"), C("b.meta") });

            var e = ChangeTree.Build(rows, false);

            Assert.That(Keys(e), Is.EqualTo(rows.Select(r => r.Path)));
            Assert.That(e.Select(x => x.Label), Is.EqualTo(rows.Select(r => r.Path)));
            Assert.That(e.Any(x => x.IsFolder), Is.False);
        }

        [Test] public void Shown_EachFolderLevelAboveItsContents_FilesKeepTheirOrder()
        {
            var rows = MetaPairing.Group(new[] { C("Assets/1.png"), C("Assets/Editor/EditorUI/EditorUI.cs"), C("Assets/Editor/EditorUI/Theme.uss"),
                C("Assets/Test.unity"), C("ProjectSettings/S.json"), C("top.txt") });

            var e = ChangeTree.Build(rows, true);

            Assert.That(Keys(e), Is.EqualTo(new[] { "Assets/", "Assets/1.png", "Assets/Editor/", "Assets/Editor/EditorUI/",
                "Assets/Editor/EditorUI/EditorUI.cs", "Assets/Editor/EditorUI/Theme.uss", "Assets/Test.unity",
                "ProjectSettings/", "ProjectSettings/S.json", "top.txt" }));
            Assert.That(e.Where(x => x.IsFolder).Select(x => x.Label), Is.EqualTo(new[] { "Assets", "Assets/Editor", "Assets/Editor/EditorUI", "ProjectSettings" }));
            Assert.That(Keys(e.Where(x => !x.IsFolder)), Is.EqualTo(rows.Select(r => r.Path)));
            Assert.That(e.Where(x => !x.IsFolder).Select(x => x.Label), Is.EqualTo(rows.Select(r => r.Path)));
        }

        [Test] public void Shown_NameSortingBetweenFolderAndContents_StaysOutOfTheBlock()
            => Assert.That(Keys(ChangeTree.Build(MetaPairing.Group(new[] { C("Assets/A/x.txt"), C("Assets/A-b.txt"), C("Assets/A/y.txt") }), true)),
                Is.EqualTo(new[] { "Assets/", "Assets/A-b.txt", "Assets/A/", "Assets/A/x.txt", "Assets/A/y.txt" }));

        [Test] public void FolderFiles_AreEveryRowUnderIt()
        {
            var e = ChangeTree.Build(MetaPairing.Group(new[] { C("Assets/A/x.txt"), C("Assets/A/B/y.txt"), C("top.txt") }), true);

            Assert.That(E(e, "Assets/").Files, Is.EqualTo(new[] { "Assets/A/B/y.txt", "Assets/A/x.txt" }));
            Assert.That(E(e, "Assets/A/B/").Files, Is.EqualTo(new[] { "Assets/A/B/y.txt" }));
            Assert.That(E(e, "top.txt").Files, Is.EqualTo(new[] { "top.txt" }));
        }

        [Test] public void FolderMeta_FoldsIntoItsFolderRow()
        {
            var e = ChangeTree.Build(MetaPairing.Group(new[] { C("Assets/A.meta", ChangeKind.Untracked), C("Assets/A/x.txt", ChangeKind.Untracked),
                C("Assets/A/x.txt.meta", ChangeKind.Untracked) }), true);

            Assert.That(Keys(e), Is.EqualTo(new[] { "Assets/", "Assets/A/", "Assets/A/x.txt" }));
            Assert.That(E(e, "Assets/A/").MetaRow, Is.EqualTo("Assets/A.meta"));
            Assert.That(E(e, "Assets/A/").Files, Is.EqualTo(new[] { "Assets/A.meta", "Assets/A/x.txt" }));
            Assert.That(E(e, "Assets/").Files, Is.EqualTo(new[] { "Assets/A.meta", "Assets/A/x.txt" }));
            Assert.That(E(e, "Assets/").MetaRow, Is.Null);
        }

        [Test] public void MetaOfFolderWithoutRow_StaysAFileRow()
            => Assert.That(Keys(ChangeTree.Build(MetaPairing.Group(new[] { C("Assets/A.meta"), C("Assets/B/x.txt") }), true)),
                Is.EqualTo(new[] { "Assets/", "Assets/A.meta", "Assets/B/", "Assets/B/x.txt" }));

        [Test] public void ConflictedFolderMeta_StaysAFileRow()
        {
            var e = ChangeTree.Build(MetaPairing.Group(new[] { C("Assets/A.meta", ChangeKind.Conflicted, conflicted: true), C("Assets/A/x.txt") }), true);

            Assert.That(Keys(e), Is.EqualTo(new[] { "Assets/", "Assets/A.meta", "Assets/A/", "Assets/A/x.txt" }));
            Assert.That(E(e, "Assets/A/").MetaRow, Is.Null);
            Assert.That(E(e, "Assets/A/").Files, Is.EqualTo(new[] { "Assets/A/x.txt" }));
        }

        // git lists an untracked nested repository as one entry ending in '/', which mustn't become a folder line too.
        [Test] public void RowEndingInSlash_IsAFileRow_NotAFolder()
        {
            var e = ChangeTree.Build(MetaPairing.Group(new[] { C("Assets/Plugins/Lib/", ChangeKind.Untracked), C("Assets/Plugins/Lib.meta", ChangeKind.Untracked) }), true);

            Assert.That(Keys(e), Is.EqualTo(new[] { "Assets/", "Assets/Plugins/", "Assets/Plugins/Lib.meta", "Assets/Plugins/Lib/" }));
            Assert.That(E(e, "Assets/Plugins/Lib/").IsFolder, Is.False);
            Assert.That(E(e, "Assets/Plugins/").Files, Is.EqualTo(new[] { "Assets/Plugins/Lib.meta", "Assets/Plugins/Lib/" }));
        }

        [Test] public void RenamedRow_IsPlacedByItsNewPath()
            => Assert.That(Keys(ChangeTree.Build(MetaPairing.Group(new[] { C("New/x.txt", ChangeKind.Renamed, "Old/x.txt") }), true)),
                Is.EqualTo(new[] { "New/", "New/x.txt" }));
    }
}
