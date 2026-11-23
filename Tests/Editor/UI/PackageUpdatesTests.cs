#nullable enable
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class PackageUpdatesTests : UpdatesFixture
    {
        // ---- the check ----

        [Test] public async Task Check_FindsTheNewerReleases()
        {
            var u = await CheckedAsync();

            Assert.That(u.UnityGitTags.Select(t => t.Tag), Is.EqualTo(new[] { "v1.4.0", "v1.2.0" }));
            Assert.That(u.FlexGitTags.Select(t => t.Tag), Is.EqualTo(new[] { "upm/v3.0.0", "upm/v2.3.0", "upm/v2.2.0" }));
            Assert.That(u.UnityGitUpdate?.Tag, Is.EqualTo("v1.4.0"));
            Assert.That(u.FlexGitUpdate?.Tag, Is.EqualTo("upm/v2.3.0")); // 3.0.0 is another major
            Assert.That(u.NewestUnityGit?.Dependencies[PackageNames.FlexGit], Is.EqualTo("2.3.0"));
            Assert.That(u.UpdateCount, Is.EqualTo(2));
            Assert.That(u.CheckedAt, Is.Not.Null);
        }

        [Test] public async Task Check_UpToDate_NoUpdates()
        {
            Install(PackageNames.UnityGit, "1.4.0", requires: "2.3.0");
            Install(PackageNames.FlexGit, "2.3.0");

            var u = await CheckedAsync();

            Assert.That(u.UpdateCount, Is.EqualTo(0));
            Assert.That(u.Announcement, Is.Null);
        }

        [Test] public async Task Check_Announcement_NamesEachUpdate()
            => Assert.That((await CheckedAsync()).Announcement,
                Is.EqualTo("UnityGit 1.4.0 and FlexGit 2.3.0 are available — Tools → Git → UnityGit → Updates"));

        [Test] public async Task Check_EmbeddedUnityGit_OnlyFlexGitListed()
        {
            Install(PackageNames.UnityGit, "1.3.0", requires: "2.2.0", source: PackageSource.Embedded);

            var u = await CheckedAsync();

            Assert.That(u.UnityGitTags, Is.Empty);
            Assert.That(u.UnityGitUpdate, Is.Null);
            Assert.That(u.FlexGitUpdate?.Tag, Is.EqualTo("upm/v2.3.0"));
            Assert.That(u.UpdateCount, Is.EqualTo(1));
        }

        [Test] public async Task Check_UnreachableRepository_KeepsTheError()
        {
            Host.Installed[PackageNames.FlexGit] = new InstalledPackage(PackageNames.FlexGit, PackageSource.Git, SemVer.Parse("2.2.0"),
                Path.Combine(Sb.Root, "missing.git") + "#upm/v2.2.0");
            var u = Create();

            await u.CheckAsync();

            Assert.That(u.Error, Does.StartWith("FlexGit: "));
            Assert.That(u.FlexGitTags, Is.Empty);
            Assert.That(u.UnityGitUpdate?.Tag, Is.EqualTo("v1.4.0")); // the other package still checked
        }

        [Test] public async Task Check_NewestNeedsANewerUnity_NotOffered()
        {
            var unityGit = await GitRepository.OpenAsync(UnityGitUrl, Sb.Options);
            await ReleaseAsync(unityGit, "v1.5.0", Package("com.bloknoter.unitygit", "1.5.0", "2.3.0", unity: "9999.1"));

            var u = await CheckedAsync();

            Assert.That(u.UnityGitUpdate, Is.Null);
            Assert.That(u.UnityGitBlocked, Is.EqualTo("UnityGit 1.5.0 needs Unity 9999.1 or newer"));
        }

        [Test] public async Task State_SurvivesSaveAndRestore()
        {
            var checkedOnce = await CheckedAsync();
            var restored = Create();

            restored.RestoreState(checkedOnce.SaveState());

            Assert.That(restored.UnityGitTags.Select(t => t.Tag + " " + t.Sha), Is.EqualTo(checkedOnce.UnityGitTags.Select(t => t.Tag + " " + t.Sha)));
            Assert.That(restored.FlexGitTags.Select(t => t.Tag + " " + t.Sha), Is.EqualTo(checkedOnce.FlexGitTags.Select(t => t.Tag + " " + t.Sha)));
            Assert.That(restored.UpdateCount, Is.EqualTo(2));
            Assert.That(restored.NewestUnityGit?.Dependencies[PackageNames.FlexGit], Is.EqualTo("2.3.0"));
            Assert.That(restored.CheckedAt, Is.Not.Null);
        }

        // ---- installing ----

        [Test] public async Task UpdateAll_ConfirmsThenInstallsBothInOneGo()
        {
            var u = await CheckedAsync();

            await u.UpdateAllAsync();

            Assert.That(Hooks.Calls, Does.Contain("Confirm:Update packages"));
            Assert.That(Hooks.LastConfirmMessage, Does.Contain("UnityGit 1.2.0 → 1.4.0, FlexGit 2.2.0 → 2.3.0"));
            Assert.That(Host.Installs.Single(), Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0", FlexGitUrl + "#upm/v2.3.0" }));
            Assert.That(Session.LastOutcome?.Action, Is.EqualTo(GitAction.UpdatePackages));
            Assert.That(Session.LastOutcome?.Kind, Is.EqualTo(OutcomeKind.Succeeded));
            Assert.That(Session.LastOutcome?.Message, Is.EqualTo("Updated UnityGit 1.2.0 → 1.4.0, FlexGit 2.2.0 → 2.3.0 — Unity is recompiling"));
        }

        [Test] public async Task UpdateUnityGit_BringsTheFlexGitItNeeds()
        {
            var u = await CheckedAsync();

            await u.UpdateUnityGitAsync();

            Assert.That(Host.Installs.Single(), Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0", FlexGitUrl + "#upm/v2.3.0" }));
        }

        [Test] public async Task UpdateFlexGit_OnlyFlexGit()
        {
            var u = await CheckedAsync();

            await u.UpdateFlexGitAsync();

            Assert.That(Host.Installs.Single(), Is.EqualTo(new[] { FlexGitUrl + "#upm/v2.3.0" }));
        }

        [Test] public async Task ConfirmDeclined_NothingInstalled()
        {
            var u = await CheckedAsync();
            Hooks.ConfirmResult = false;

            await u.UpdateAllAsync();

            Assert.That(Host.Installs, Is.Empty);
            Assert.That(Session.LastOutcome?.Kind, Is.EqualTo(OutcomeKind.Cancelled));
            Assert.That(u.IsInstalling, Is.False);
        }

        [Test] public async Task InPlayMode_Refused()
        {
            var u = await CheckedAsync();
            Session.IsPlayMode = true;

            await u.UpdateAllAsync();

            Assert.That(Host.Installs, Is.Empty);
            Assert.That(Session.LastOutcome?.Kind, Is.EqualTo(OutcomeKind.Refused));
            Assert.That(Session.LastOutcome?.Message, Is.EqualTo("Not available in Play Mode"));
        }

        [Test] public async Task OlderFlexGitThanUnityGitNeeds_Refused()
        {
            Install(PackageNames.FlexGit, "2.3.0");
            var u = await CheckedAsync();

            await u.InstallAsync(PackageNames.FlexGit, u.FlexGitTags.Single(t => t.Tag == "upm/v2.2.0"));

            // Installed UnityGit 1.2.0 needs 2.2.0, so 2.2.0 is fine; 3.0.0 isn't.
            Assert.That(Host.Installs.Single(), Is.EqualTo(new[] { FlexGitUrl + "#upm/v2.2.0" }));
            Host.Installs.Clear();
            await u.InstallAsync(PackageNames.FlexGit, u.FlexGitTags.Single(t => t.Tag == "upm/v3.0.0"));
            Assert.That(Host.Installs, Is.Empty);
            Assert.That(Session.LastOutcome?.Message, Is.EqualTo("UnityGit 1.2.0 is built for FlexGit 2.x"));
        }

        [Test] public async Task OlderUnityGit_WarnsTheUpdatesTabGoesAway()
        {
            Install(PackageNames.UnityGit, "1.4.0", requires: "2.3.0");
            Install(PackageNames.FlexGit, "2.3.0");
            var u = await CheckedAsync();

            await u.InstallAsync(PackageNames.UnityGit, u.UnityGitTags.Single(t => t.Tag == "v1.2.0"));

            Assert.That(Hooks.LastConfirmMessage, Does.Contain("UnityGit 1.2.0 has no Updates tab"));
            Assert.That(Host.Installs.Single(), Is.EqualTo(new[] { UnityGitUrl + "#v1.2.0" })); // FlexGit 2.3.0 still fits 2.2.0
            Assert.That(Session.LastOutcome?.Message, Does.StartWith("Installed UnityGit 1.4.0 → 1.2.0"));
        }

        [Test] public async Task PackageManagerFails_OutcomeFailedWithItsMessage()
        {
            var u = await CheckedAsync();
            Host.Reports.Clear();
            Host.Reports.Add((InstallReport.Failed, "Cannot resolve package"));

            await u.UpdateFlexGitAsync();

            Assert.That(Session.LastOutcome?.Kind, Is.EqualTo(OutcomeKind.Failed));
            Assert.That(Session.LastOutcome?.Message, Is.EqualTo("Package Manager couldn't install FlexGit 2.2.0 → 2.3.0"));
            Assert.That(Session.LastOutcome?.Details, Is.EqualTo("Cannot resolve package"));
            Assert.That(u.IsInstalling, Is.False);
        }

        [Test] public async Task CompileFailsAfterInstalling_OutcomeFailed()
        {
            var u = await CheckedAsync();
            Host.Reports.Add((InstallReport.CompileFailed, null));

            await u.UpdateFlexGitAsync();

            Assert.That(Session.LastOutcome?.Kind, Is.EqualTo(OutcomeKind.Failed));
            Assert.That(Session.LastOutcome?.Message, Does.StartWith("Scripts don't compile after installing FlexGit 2.2.0 → 2.3.0"));
        }

        // ---- the version lists ----

        [Test] public async Task FlexGitVersions_InstalledChecked_IncompatibleDisabledWithTheReason()
        {
            var items = (await CheckedAsync()).VersionItems(PackageNames.FlexGit);

            Assert.That(items.Select(i => i.Label), Is.EqualTo(new[]
            {
                "3.0.0 — UnityGit 1.2.0 is built for FlexGit 2.x",
                "2.3.0",
                "2.2.0 (installed)",
            }));
            Assert.That(items.Select(i => i.Enabled), Is.EqualTo(new[] { false, true, false }));
            Assert.That(items.Select(i => i.Installed), Is.EqualTo(new[] { false, false, true }));
        }

        [Test] public async Task UnityGitVersions_InstalledOneDisabled()
        {
            var items = (await CheckedAsync()).VersionItems(PackageNames.UnityGit);

            Assert.That(items.Select(i => i.Label), Is.EqualTo(new[] { "1.4.0", "1.2.0 (installed)" }));
            Assert.That(items.Select(i => i.Enabled), Is.EqualTo(new[] { true, false }));
        }
    }
}
