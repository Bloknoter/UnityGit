#nullable enable
using System;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class IconTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        async Task<GitSession> ReadySessionAsync()
        {
            var trio = await _sb.CreateTrioAsync();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            return s;
        }

        static void AssertHasIcon(Button button)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.iconImage.texture, Is.Not.Null, button.name);
        }

        [Test] public void EveryIcon_Loads()
        {
            foreach (GitIcon icon in Enum.GetValues(typeof(GitIcon)))
                Assert.That(GitIcons.Load(icon), Is.Not.Null, GitIcons.Folder + "/" + icon.ToString().ToLowerInvariant() + ".png");
        }

        // The committed .meta files: crisp editor icons, drawn at 16px from a 32px source.
        [Test] public void EveryIcon_ImportsAsEditorGuiTexture()
        {
            foreach (GitIcon icon in Enum.GetValues(typeof(GitIcon)))
            {
                var path = GitIcons.PathOf(icon);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer!.textureType, Is.EqualTo(TextureImporterType.GUI), path);
                Assert.That(importer.mipmapEnabled, Is.False, path);
                Assert.That(importer.alphaIsTransparency, Is.True, path);
                Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None), path);
                Assert.That(importer.sRGBTexture, Is.True, path);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), path);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
                var texture = GitIcons.Load(icon)!;
                Assert.That(texture.width, Is.EqualTo(32), path);
                Assert.That(texture.mipmapCount, Is.EqualTo(1), path);
            }
        }

        [Test] public async Task ToolbarAndActionButtons_HaveIcons()
        {
            var s = await ReadySessionAsync();

            var v = new GitWindowView(s);

            foreach (var name in new[] { UiNames.CommitButton, UiNames.FetchButton, UiNames.PullButton, UiNames.PushButton, UiNames.RefreshButton, UiNames.CancelButton })
                AssertHasIcon(v.Q<Button>(name));
            Assert.That(v.Q<ToolbarMenu>(UiNames.BranchMenu).Q<Image>(className: "unitygit-icon")?.image, Is.Not.Null);
            Assert.That(v.Q<Button>(UiNames.PushButton).text, Is.EqualTo(ActionLabels.Push(s.Snapshot)));
        }

        // GitWindow.uss tints "Button > .unity-button__image": pins the element Unity's Button.iconImage creates.
        [Test] public async Task ButtonIcon_IsImageBeforeText()
        {
            var s = await ReadySessionAsync();
            var v = new GitWindowView(s);

            var fetch = v.Q<Button>(UiNames.FetchButton);

            Assert.That(fetch.childCount, Is.GreaterThan(0));
            Assert.That(fetch[0], Is.InstanceOf<Image>());
            Assert.That(fetch[0].ClassListContains("unity-button__image"), Is.True);
            Assert.That(fetch.text, Is.EqualTo("Fetch"));
        }

        [Test] public async Task PanelButtons_HaveIcons()
        {
            var s = await ReadySessionAsync();

            AssertHasIcon(new ChangesPanel(s).Q<Button>(UiNames.CommitSubmit));
            AssertHasIcon(new MorePanel(s).Q<Button>(UiNames.LocksRefresh));

            var notRepo = _sb.CreateSession(_sb.MakeDir("not-a-repo"), new FakeEditorHooks());
            await notRepo.RefreshAsync();
            AssertHasIcon(new GitWindowView(notRepo).Q<Button>(UiNames.InitButton));
        }

        [Test] public async Task LfsSetupButton_HasIcon()
        {
            var repo = await _sb.InitRepoAsync("p");
            if (!repo.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            GitSandbox.WriteFile(repo, ".gitattributes", "*.psd filter=lfs diff=lfs merge=lfs -text\n");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            var p = new OverviewPanel(s);

            AssertHasIcon(p.Q<Button>(UiNames.LfsSetupButton));
        }
    }
}
