#nullable enable
using System;
using System.Collections.Generic;
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
    }
}
