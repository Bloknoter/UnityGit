using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class AuthorsParserTests
    {
        [Test] public void Parse_ReadsCountNameEmail_OrderedByCount()
        {
            var authors = AuthorsParser.Parse("     2\tTest User <test@example.com>\n    5\tTeammate <mate@example.com>\n");
            Assert.That(authors.Select(a => (a.Name, a.Email, a.CommitCount)),
                Is.EqualTo(new[] { ("Teammate", "mate@example.com", 5), ("Test User", "test@example.com", 2) }));
        }

        [Test] public void Parse_Empty_ReturnsEmpty() => Assert.That(AuthorsParser.Parse(""), Is.Empty);
    }
}
