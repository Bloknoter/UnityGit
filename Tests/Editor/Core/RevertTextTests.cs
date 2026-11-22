#nullable enable
using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class RevertTextTests
    {
        const string Warning = "Your changes will be lost. This can't be undone.";

        static ChangeItem Row(string path, ChangeKind kind = ChangeKind.Modified)
            => new ChangeItem { Path = path, GitPaths = new[] { path }, Kind = kind };

        [Test] public void Title_OneFile_NamesIt() => Assert.That(RevertText.Title(new[] { Row("Assets/Foo.cs") }), Is.EqualTo("Revert Assets/Foo.cs?"));

        [Test] public void Title_SeveralFiles_CountsThem() => Assert.That(RevertText.Title(new[] { Row("a"), Row("b"), Row("c") }), Is.EqualTo("Revert 3 files?"));

        [Test] public void Message_OneModifiedFile_OnlyWarns()
            => Assert.That(RevertText.Message(new[] { Row("a") }, 0), Is.EqualTo(Warning));

        [Test] public void Message_NewFiles_SaysTheyAreDeleted()
            => Assert.That(RevertText.Message(new[] { Row("a", ChangeKind.Untracked), Row("b", ChangeKind.Added), Row("c") }, 0),
                Is.EqualTo(Warning + "\n2 new files will be deleted.\n\na\nb\nc"));

        [Test] public void Message_OneNewFile_Singular()
            => Assert.That(RevertText.Message(new[] { Row("a", ChangeKind.Untracked) }, 0),
                Is.EqualTo(Warning + "\n1 new file will be deleted."));

        [Test] public void Message_MoreThanTenFiles_ListsTenAndCountsTheRest()
        {
            var rows = Enumerable.Range(1, 13).Select(i => Row($"f{i:00}")).ToArray();
            Assert.That(RevertText.Message(rows, 0), Is.EqualTo(Warning + "\n\n"
                + string.Join("\n", rows.Take(10).Select(r => r.Path)) + "\n…and 3 more"));
        }

        [Test] public void Message_SkippedConflicts_SaysSo()
        {
            Assert.That(RevertText.Message(new[] { Row("a") }, 1), Is.EqualTo(Warning + "\n\n1 conflicted file is skipped."));
            Assert.That(RevertText.Message(new[] { Row("a") }, 2), Is.EqualTo(Warning + "\n\n2 conflicted files are skipped."));
        }
    }
}
