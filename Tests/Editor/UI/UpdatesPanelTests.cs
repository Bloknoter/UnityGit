#nullable enable
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class UpdatesPanelTests : UpdatesFixture
    {
        static string Card(string package, string part) => UiNames.UpdatesCard(package) + part;

        [Test] public async Task Window_UpdatesTabLast_BadgeCountsUpdates()
        {
            var updates = await CheckedAsync();

            var v = new GitWindowView(Session, updates);

            Assert.That(v.VisibleTabs.Last(), Is.EqualTo(WindowTab.Updates));
            Assert.That(v.TabBadge(WindowTab.Updates), Is.EqualTo("2"));
            Assert.That(v.TabBadgeTone(WindowTab.Updates), Is.EqualTo(UnityGit.UI.Theme.EditorUiTone.Accent));
        }

        [Test] public async Task Window_UpToDate_NoBadge()
        {
            Install(PackageNames.UnityGit, "1.4.0", requires: "2.3.0");
            Install(PackageNames.FlexGit, "2.3.0");

            var v = new GitWindowView(Session, await CheckedAsync());

            Assert.That(v.TabBadge(WindowTab.Updates), Is.Null);
        }

        [Test] public async Task Panel_ShowsInstalledAndLatest_UpdateButtonsNameTheVersion()
        {
            var p = new UpdatesPanel(await CheckedAsync());

            Assert.That(p.Q<Label>(Card(PackageNames.UnityGit, UiNames.UpdatesInstalled)).text, Is.EqualTo("1.2.0"));
            Assert.That(p.Q<Label>(Card(PackageNames.UnityGit, UiNames.UpdatesSource)).text, Is.EqualTo("git · v1.2.0"));
            Assert.That(p.Q<Label>(Card(PackageNames.UnityGit, UiNames.UpdatesLatest)).text, Is.EqualTo("1.4.0 — needs FlexGit 2.3.0"));
            var unityGitUpdate = p.Q<Button>(Card(PackageNames.UnityGit, UiNames.UpdatesUpdate));
            Assert.That(unityGitUpdate.text, Is.EqualTo("Update to 1.4.0"));
            Assert.That(unityGitUpdate.enabledSelf, Is.True);
            Assert.That(p.Q<Button>(Card(PackageNames.FlexGit, UiNames.UpdatesUpdate)).text, Is.EqualTo("Update to 2.3.0"));
            Assert.That(p.Q<Label>(Card(PackageNames.FlexGit, UiNames.UpdatesLatest)).text, Is.EqualTo("2.3.0"));
            Assert.That(p.Q<Button>(UiNames.UpdatesAll).enabledSelf, Is.True);
        }

        [Test] public async Task Panel_UpToDate_ButtonsDisabled()
        {
            Install(PackageNames.UnityGit, "1.4.0", requires: "2.3.0");
            Install(PackageNames.FlexGit, "2.3.0");

            var p = new UpdatesPanel(await CheckedAsync());

            var update = p.Q<Button>(Card(PackageNames.UnityGit, UiNames.UpdatesUpdate));
            Assert.That(update.text, Is.EqualTo("Up to date"));
            Assert.That(update.enabledSelf, Is.False);
            Assert.That(p.Q<Label>(Card(PackageNames.UnityGit, UiNames.UpdatesLatest)).text, Is.EqualTo("1.4.0 (installed)"));
            Assert.That(p.Q<Button>(UiNames.UpdatesAll).enabledSelf, Is.False);
            Assert.That(p.Q<Button>(Card(PackageNames.FlexGit, UiNames.UpdatesVersions)).enabledSelf, Is.True);
        }

        [Test] public async Task Panel_EmbeddedUnityGit_NoButtons_SaysWhy()
        {
            Install(PackageNames.UnityGit, "1.3.0", requires: "2.2.0", source: PackageSource.Embedded);

            var p = new UpdatesPanel(await CheckedAsync());

            Assert.That(p.Q<Label>(Card(PackageNames.UnityGit, UiNames.UpdatesSource)).text, Is.EqualTo("embedded in Packages — UnityGit leaves it alone"));
            Assert.That(IsDisplayed(p.Q<Button>(Card(PackageNames.UnityGit, UiNames.UpdatesUpdate))), Is.False);
            Assert.That(IsDisplayed(p.Q<Button>(Card(PackageNames.UnityGit, UiNames.UpdatesVersions))), Is.False);
            Assert.That(IsDisplayed(p.Q<Button>(Card(PackageNames.FlexGit, UiNames.UpdatesUpdate))), Is.True);
        }

        [Test] public async Task Panel_CheckFailed_ShowsTheError()
        {
            Host.Installed[PackageNames.FlexGit] = new InstalledPackage(PackageNames.FlexGit, PackageSource.Git, SemVer.Parse("2.2.0"),
                System.IO.Path.Combine(Sb.Root, "missing.git") + "#upm/v2.2.0");
            var updates = Create();
            await updates.CheckAsync();

            var p = new UpdatesPanel(updates);

            var error = p.Q<Label>(UiNames.UpdatesError);
            Assert.That(IsDisplayed(error), Is.True);
            Assert.That(error.text, Does.StartWith("FlexGit: "));
        }

        [Test] public async Task Panel_NotCheckedYet_SaysSo()
        {
            var p = new UpdatesPanel(Create());

            Assert.That(p.Q<Label>(UiNames.UpdatesStatus).text, Is.EqualTo("Not checked yet"));
            Assert.That(IsDisplayed(p.Q<Label>(UiNames.UpdatesError)), Is.False);
            await Task.CompletedTask;
        }

        [Test] public async Task Panel_RendersAfterACheck()
        {
            var updates = Create();
            var p = new UpdatesPanel(updates);
            updates.Changed += p.Render; // the window does this for its panels

            await updates.CheckAsync();

            Assert.That(p.Q<Label>(UiNames.UpdatesStatus).text, Does.StartWith("Checked at "));
            Assert.That(p.Q<Button>(Card(PackageNames.UnityGit, UiNames.UpdatesUpdate)).text, Is.EqualTo("Update to 1.4.0"));
        }

        static bool IsDisplayed(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e.style.display == DisplayStyle.None) return false;
            return true;
        }
    }
}
