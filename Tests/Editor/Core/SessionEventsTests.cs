#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class SessionEventsTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

        [Test] public async Task ThrowingChangedSubscriber_DoesNotWedgeSession()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());
            await s.RefreshAsync();
            var boom = new InvalidOperationException("boom");
            var errors = new List<Exception>();
            var laterSubscriberCalls = 0;
            s.UnexpectedError += errors.Add;
            s.Changed += () => throw boom;
            s.Changed += () => laterSubscriberCalls++;

            var o = await s.InitAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Repository created with a Unity .gitignore")));
            Assert.That(s.IsBusy, Is.False);
            Assert.That(errors, Is.Not.Empty.And.All.SameAs(boom));
            Assert.That(laterSubscriberCalls, Is.GreaterThanOrEqualTo(2));
        }

        [Test] public async Task ThrowingUnexpectedErrorSubscriber_IsSwallowed()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());
            await s.RefreshAsync();
            var boom = new InvalidOperationException("boom");
            var errors = new List<Exception>();
            s.UnexpectedError += e => { errors.Add(e); throw new InvalidOperationException("handler failed"); };
            s.Changed += () => throw boom;

            var o = await s.InitAsync();

            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded));
            Assert.That(s.IsBusy, Is.False);
            Assert.That(errors, Is.Not.Empty.And.All.SameAs(boom));
        }

        // The commit succeeds, then its refresh fails (the index is corrupted once git is done) and reaches the Log and
        // Warning subscribers, which throw. That must not turn the commit into "Unexpected error".
        [Test] public async Task ThrowingLogAndWarningSubscribers_DoNotFailCommit()
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "README.md", "readme", "initial");
            GitSandbox.WriteFile(repo, "a.txt", "a");
            var hooks = new FakeEditorHooks();
            var s = _sb.CreateSession(repo.WorkingDirectory, hooks);
            await s.RefreshAsync();
            hooks.OnEndGitOperation = () => File.WriteAllText(Path.Combine(repo.GitDirectory, "index"), "garbage");
            s.Log = _ => throw new InvalidOperationException("log failed");
            s.Warning = _ => throw new InvalidOperationException("warning failed");

            var o = await s.CommitAsync("add a", new[] { "a.txt" }, new[] { "a.txt" });

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Committed 1 file")));
            Assert.That(s.IsBusy, Is.False);
        }
    }
}
