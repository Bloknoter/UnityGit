#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class InstallPlannerTests
    {
        const string UnityGitUrl = "https://github.com/Bloknoter/UnityGit.git";
        const string FlexGitUrl = "https://github.com/Bloknoter/FlexGit.git";

        static SemVer V(string v) => SemVer.Parse(v);

        static InstalledPackage UnityGit(string version, string? requires = "2.2.0", PackageSource source = PackageSource.Git)
            => new InstalledPackage(PackageNames.UnityGit, source, V(version),
                source == PackageSource.Git ? $"{UnityGitUrl}#v{version}" : "file:com.bloknoter.unitygit",
                requires == null ? (SemVer?)null : V(requires));

        static InstalledPackage FlexGit(string version, PackageSource source = PackageSource.Git)
            => new InstalledPackage(PackageNames.FlexGit, source, V(version),
                source == PackageSource.Git ? $"{FlexGitUrl}#upm/v{version}" : "file:com.bloknoter.flexgit");

        static ReleaseTag U(string v) => new ReleaseTag(V(v), "v" + v, "sha-u" + v);
        static ReleaseTag F(string v) => new ReleaseTag(V(v), "upm/v" + v, "sha-f" + v);
        static IReadOnlyList<ReleaseTag> Fs(params string[] versions) => versions.Select(F).OrderByDescending(t => t.Version).ToList();
        static IReadOnlyList<ReleaseTag> Us(params string[] versions) => versions.Select(U).OrderByDescending(t => t.Version).ToList();

        static string[] Ids(InstallPlan plan) => plan.Steps.Select(s => s.Identifier).ToArray();

        // ---- one UnityGit release ----

        [Test] public void UnityGit_FlexGitAlreadyEnough_OnlyUnityGit()
        {
            var plan = InstallPlanner.UnityGit(UnityGit("1.2.0"), FlexGit("2.2.0"), U("1.4.0"), V("2.2.0"), Fs("2.2.0", "2.3.0"));
            Assert.That(plan.Refused, Is.Null);
            Assert.That(Ids(plan), Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0" }));
        }

        [Test] public void UnityGit_NeedsNewerFlexGit_AddsTheRequiredFlexGit()
        {
            var plan = InstallPlanner.UnityGit(UnityGit("1.2.0"), FlexGit("2.2.0"), U("1.4.0"), V("2.3.0"), Fs("2.2.0", "2.3.0", "2.4.0"));
            Assert.That(Ids(plan), Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0", FlexGitUrl + "#upm/v2.3.0" }));
        }

        [Test] public void UnityGit_RequiredFlexGitTagMissing_NewestCompatibleFlexGit()
        {
            var plan = InstallPlanner.UnityGit(UnityGit("1.2.0"), FlexGit("2.2.0"), U("1.4.0"), V("2.3.0"), Fs("2.2.0", "2.3.1", "2.4.0", "3.0.0"));
            Assert.That(Ids(plan), Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0", FlexGitUrl + "#upm/v2.4.0" }));
        }

        [Test] public void UnityGit_NoCompatibleFlexGitRelease_Refused()
        {
            var plan = InstallPlanner.UnityGit(UnityGit("1.2.0"), FlexGit("2.2.0"), U("1.4.0"), V("2.3.0"), Fs("2.2.0", "3.0.0"));
            Assert.That(plan.Refused, Is.EqualTo("UnityGit 1.4.0 needs FlexGit 2.3.0, and no such FlexGit release was found"));
            Assert.That(plan.Steps, Is.Empty);
        }

        [Test] public void UnityGit_NeedsNewerFlexGit_FlexGitNotFromGit_Refused()
            => Assert.That(InstallPlanner.UnityGit(UnityGit("1.2.0"), FlexGit("2.2.0", PackageSource.Embedded), U("1.4.0"), V("2.3.0"), Fs("2.3.0")).Refused,
                Is.EqualTo("UnityGit 1.4.0 needs FlexGit 2.3.0 — update FlexGit first (it isn't installed from a git URL)"));

        [Test] public void UnityGit_SameVersion_Refused()
            => Assert.That(InstallPlanner.UnityGit(UnityGit("1.2.0"), FlexGit("2.2.0"), U("1.2.0"), V("2.2.0"), Fs("2.2.0")).Refused,
                Is.EqualTo("UnityGit 1.2.0 is already installed"));

        [Test] public void UnityGit_Embedded_Refused()
            => Assert.That(InstallPlanner.UnityGit(UnityGit("1.3.0", source: PackageSource.Embedded), FlexGit("2.2.0"), U("1.4.0"), V("2.2.0"), Fs("2.2.0")).Refused,
                Is.EqualTo("UnityGit isn't installed from a git URL"));

        [Test] public void UnityGit_Older_FlexGitNewerSameMajor_FlexGitKept()
        {
            var plan = InstallPlanner.UnityGit(UnityGit("1.4.0"), FlexGit("2.4.0"), U("1.0.0"), V("2.0.0"), Fs("2.0.0", "2.4.0"));
            Assert.That(Ids(plan), Is.EqualTo(new[] { UnityGitUrl + "#v1.0.0" }));
        }

        [Test] public void UnityGit_Older_FlexGitOtherMajor_FlexGitMovedToTheRequired()
        {
            var plan = InstallPlanner.UnityGit(UnityGit("2.0.0", requires: "3.0.0"), FlexGit("3.0.0"), U("1.4.0"), V("2.2.0"), Fs("2.2.0", "2.4.0", "3.0.0"));
            Assert.That(Ids(plan), Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0", FlexGitUrl + "#upm/v2.2.0" }));
        }

        [Test] public void UnityGit_NoFlexGitRequirement_OnlyUnityGit()
            => Assert.That(Ids(InstallPlanner.UnityGit(UnityGit("1.2.0"), FlexGit("2.2.0"), U("1.4.0"), null, Fs("2.2.0"))),
                Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0" }));

        [Test] public void UnityGit_BeforeTheUpdater_RemovesUpdater()
        {
            Assert.That(InstallPlanner.UnityGit(UnityGit("1.4.0"), FlexGit("2.2.0"), U("1.3.0"), V("2.2.0"), Fs("2.2.0")).RemovesUpdater, Is.True);
            Assert.That(InstallPlanner.UnityGit(UnityGit("1.4.0"), FlexGit("2.2.0"), U("1.5.0"), V("2.2.0"), Fs("2.2.0")).RemovesUpdater, Is.False);
        }

        // ---- one FlexGit release ----

        [Test] public void FlexGit_Compatible_OneStep()
        {
            var plan = InstallPlanner.FlexGit(UnityGit("1.2.0"), FlexGit("2.2.0"), F("2.3.0"));
            Assert.That(plan.Refused, Is.Null);
            Assert.That(Ids(plan), Is.EqualTo(new[] { FlexGitUrl + "#upm/v2.3.0" }));
            Assert.That(plan.RemovesUpdater, Is.False);
        }

        [Test] public void FlexGit_BelowWhatUnityGitNeeds_Refused()
            => Assert.That(InstallPlanner.FlexGit(UnityGit("1.2.0"), FlexGit("2.2.0"), F("2.1.0")).Refused,
                Is.EqualTo("UnityGit 1.2.0 needs FlexGit 2.2.0 or newer"));

        [Test] public void FlexGit_OtherMajor_Refused()
            => Assert.That(InstallPlanner.FlexGit(UnityGit("1.2.0"), FlexGit("2.2.0"), F("3.0.0")).Refused,
                Is.EqualTo("UnityGit 1.2.0 is built for FlexGit 2.x"));

        [Test] public void FlexGit_UnityGitRequirementUnknown_Allowed()
            => Assert.That(InstallPlanner.FlexGit(UnityGit("1.2.0", requires: null), FlexGit("2.2.0"), F("2.0.0")).Refused, Is.Null);

        [Test] public void FlexGit_SameVersion_Refused()
            => Assert.That(InstallPlanner.FlexGit(UnityGit("1.2.0"), FlexGit("2.2.0"), F("2.2.0")).Refused, Is.EqualTo("FlexGit 2.2.0 is already installed"));

        [Test] public void FlexGit_NotFromGit_Refused()
            => Assert.That(InstallPlanner.FlexGit(UnityGit("1.2.0"), FlexGit("2.2.0", PackageSource.Local), F("2.3.0")).Refused,
                Is.EqualTo("FlexGit isn't installed from a git URL"));

        [Test] public void FlexGit_WithEmbeddedUnityGit_StillChecksItsRequirement()
            => Assert.That(InstallPlanner.FlexGit(UnityGit("1.2.0", source: PackageSource.Embedded), FlexGit("2.2.0"), F("2.1.0")).Refused,
                Is.EqualTo("UnityGit 1.2.0 needs FlexGit 2.2.0 or newer"));

        // ---- what Update offers ----

        [Test] public void UnityGitUpdate_NewestWhenNewer()
        {
            Assert.That(InstallPlanner.UnityGitUpdate(UnityGit("1.2.0"), Us("1.0.0", "1.2.0", "1.4.0"))?.Tag, Is.EqualTo("v1.4.0"));
            Assert.That(InstallPlanner.UnityGitUpdate(UnityGit("1.4.0"), Us("1.0.0", "1.4.0")), Is.Null);
            Assert.That(InstallPlanner.UnityGitUpdate(UnityGit("1.2.0", source: PackageSource.Embedded), Us("1.4.0")), Is.Null);
        }

        [Test] public void FlexGitUpdate_NewestCompatibleWithInstalledUnityGit()
        {
            Assert.That(InstallPlanner.FlexGitUpdate(UnityGit("1.2.0"), FlexGit("2.2.0"), Fs("2.2.0", "2.3.0", "2.4.0", "3.0.0"))?.Tag, Is.EqualTo("upm/v2.4.0"));
            Assert.That(InstallPlanner.FlexGitUpdate(UnityGit("1.2.0"), FlexGit("2.4.0"), Fs("2.4.0", "3.0.0")), Is.Null);
            Assert.That(InstallPlanner.FlexGitUpdate(UnityGit("1.2.0"), FlexGit("2.2.0", PackageSource.Embedded), Fs("2.4.0")), Is.Null);
        }

        // ---- Update all ----

        [Test] public void All_BothNewer_UnityGitAndNewestCompatibleFlexGit()
        {
            var plan = InstallPlanner.All(UnityGit("1.2.0"), FlexGit("2.2.0"), Us("1.2.0", "1.4.0"), V("2.3.0"), Fs("2.2.0", "2.3.0", "2.4.0", "3.0.0"));
            Assert.That(Ids(plan), Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0", FlexGitUrl + "#upm/v2.4.0" }));
        }

        [Test] public void All_UnityGitNewer_FlexGitAlreadyNewestCompatible_OnlyUnityGit()
            => Assert.That(Ids(InstallPlanner.All(UnityGit("1.2.0"), FlexGit("2.4.0"), Us("1.4.0"), V("2.3.0"), Fs("2.4.0", "3.0.0"))),
                Is.EqualTo(new[] { UnityGitUrl + "#v1.4.0" }));

        [Test] public void All_OnlyFlexGitNewer_OnlyFlexGit()
            => Assert.That(Ids(InstallPlanner.All(UnityGit("1.4.0"), FlexGit("2.2.0"), Us("1.4.0"), V("2.2.0"), Fs("2.2.0", "2.3.0"))),
                Is.EqualTo(new[] { FlexGitUrl + "#upm/v2.3.0" }));

        [Test] public void All_EmbeddedUnityGit_UpdatesFlexGitOnly()
            => Assert.That(Ids(InstallPlanner.All(UnityGit("1.3.0", source: PackageSource.Embedded), FlexGit("2.0.0"), Us("1.4.0"), V("2.3.0"), Fs("2.0.0", "2.2.0", "3.0.0"))),
                Is.EqualTo(new[] { FlexGitUrl + "#upm/v2.2.0" }));

        [Test] public void All_NothingNewer_Refused()
            => Assert.That(InstallPlanner.All(UnityGit("1.4.0"), FlexGit("2.3.0"), Us("1.4.0"), V("2.3.0"), Fs("2.3.0")).Refused,
                Is.EqualTo("Everything is up to date"));

        [Test] public void All_NewestUnityGitNeedsAMissingFlexGit_Refused()
            => Assert.That(InstallPlanner.All(UnityGit("1.2.0"), FlexGit("2.2.0"), Us("1.4.0"), V("2.5.0"), Fs("2.2.0", "2.3.0")).Refused,
                Is.EqualTo("UnityGit 1.4.0 needs FlexGit 2.5.0, and no such FlexGit release was found"));

        // ---- wording ----

        [Test] public void Summary_ListsEachChange()
        {
            var plan = InstallPlanner.UnityGit(UnityGit("1.2.0"), FlexGit("2.2.0"), U("1.4.0"), V("2.3.0"), Fs("2.3.0"));
            Assert.That(plan.Summary, Is.EqualTo("UnityGit 1.2.0 → 1.4.0, FlexGit 2.2.0 → 2.3.0"));
        }

        [Test] public void FlexGitConflict_ForTheVersionList()
        {
            Assert.That(InstallPlanner.FlexGitConflict(UnityGit("1.2.0"), V("2.1.0")), Is.EqualTo("UnityGit 1.2.0 needs FlexGit 2.2.0 or newer"));
            Assert.That(InstallPlanner.FlexGitConflict(UnityGit("1.2.0"), V("2.2.0")), Is.Null);
        }
    }
}
