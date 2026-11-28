#nullable enable
using System;
using System.IO;
using System.Text;

namespace UnityGit
{
    /// <summary>A .gitignore as read from disk: its text with LF line endings, and what's needed to write it back the same way.</summary>
    public sealed class GitignoreFileText
    {
        public GitignoreFileText(string text, bool hasBom, string lineEnding, bool isUtf8, DateTime writeTimeUtc, long length)
        {
            Text = text;
            HasBom = hasBom;
            LineEnding = lineEnding;
            IsUtf8 = isUtf8;
            WriteTimeUtc = writeTimeUtc;
            Length = length;
        }

        /// <summary>LF line endings, without the BOM.</summary>
        public string Text { get; }

        public bool HasBom { get; }

        /// <summary>"\r\n" when CRLF line endings outnumber lone LF ones, else "\n".</summary>
        public string LineEnding { get; }

        /// <summary>False when the bytes aren't valid UTF-8: <see cref="Text"/> is then a lenient decode, to show read-only.</summary>
        public bool IsUtf8 { get; }

        public DateTime WriteTimeUtc { get; }

        public long Length { get; }
    }

    /// <summary>Reads and writes .gitignore files without changing bytes the user didn't touch (spec, anti-goals).</summary>
    public static class GitignoreFile
    {
        static readonly UTF8Encoding Strict = new UTF8Encoding(false, true);
        static readonly UTF8Encoding Lenient = new UTF8Encoding(false, false);
        static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

        /// <summary>The file's text and format, or null when it doesn't exist.</summary>
        public static GitignoreFileText? Read(string fullPath)
        {
            if (!File.Exists(fullPath)) return null;
            var info = new FileInfo(fullPath);
            var bytes = File.ReadAllBytes(fullPath);
            var hasBom = bytes.Length >= 3 && bytes[0] == Bom[0] && bytes[1] == Bom[1] && bytes[2] == Bom[2];
            var offset = hasBom ? 3 : 0;
            string raw;
            var isUtf8 = true;
            try
            {
                raw = Strict.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                raw = Lenient.GetString(bytes, offset, bytes.Length - offset);
                isUtf8 = false;
            }
            return new GitignoreFileText(Normalize(raw), hasBom, LineEndingOf(raw), isUtf8, info.LastWriteTimeUtc, info.Length);
        }

        /// <summary>Writes LF text with <paramref name="lineEnding"/> line endings, after a BOM when asked.</summary>
        public static void Write(string fullPath, string lfText, bool bom, string lineEnding)
        {
            var text = lineEnding == "\n" ? lfText : Normalize(lfText).Replace("\n", lineEnding);
            var body = Strict.GetBytes(text);
            if (!bom)
            {
                File.WriteAllBytes(fullPath, body);
                return;
            }
            var bytes = new byte[Bom.Length + body.Length];
            Bom.CopyTo(bytes, 0);
            body.CopyTo(bytes, Bom.Length);
            File.WriteAllBytes(fullPath, bytes);
        }

        /// <summary>True when the file still has the time and length it had when <paramref name="read"/> was read.</summary>
        public static bool SameStamp(GitignoreFileText read, string fullPath)
        {
            if (!File.Exists(fullPath)) return false;
            var info = new FileInfo(fullPath);
            return info.LastWriteTimeUtc == read.WriteTimeUtc && info.Length == read.Length;
        }

        public static string Normalize(string text) => text.Replace("\r\n", "\n");

        static string LineEndingOf(string raw)
        {
            int crlf = 0, lf = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '\n') continue;
                if (i > 0 && raw[i - 1] == '\r') crlf++;
                else lf++;
            }
            return crlf > lf ? "\r\n" : "\n";
        }
    }
}
