using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace ShowPing
{
    internal sealed class EndpointReader
    {
        private const int InitialTailBytes = 512 * 1024;
        private const int FallbackTailBytes = 2 * 1024 * 1024;

        private static readonly string[] KnownLogFiles = { "GameNetLogger.log", "Hearthstone.log" };
        private static DateTime nextNoMatchLogUtc = DateTime.MinValue;

        private static readonly Regex[] EndpointRegexes =
        {
            new Regex(
                @"Network\.GotoGameServe(?:r)?[^\r\n]{0,2000}?address\s*=\s*(?<endpoint>\[[^\]]+\]:\d{1,5}|\d{1,3}(?:\.\d{1,3}){3}:\d{1,5})",
                RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(
                @"(?:game\s*)?server[^\r\n]{0,2000}?address\s*=\s*(?<endpoint>\[[^\]]+\]:\d{1,5}|\d{1,3}(?:\.\d{1,3}){3}:\d{1,5})",
                RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        private static readonly Regex EndpointValueRegex = new Regex(
            @"^\s*(?:\[(?<host>[^\]]+)\]|(?<host>\d{1,3}(?:\.\d{1,3}){3})):(?<port>\d{1,5})\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public bool TryReadFromHearthstoneLogs(out string address, out ushort port)
        {
            address = null;
            port = 0;

            var root = GetConfiguredLogRoot();
            if (!TryReadLatestEndpoint(root, out address, out port))
                return false;

            IPAddress parsed;
            return IPAddress.TryParse(address, out parsed);
        }

        private static string GetConfiguredLogRoot()
        {
            try
            {
                return Path.Combine(Config.Instance.HearthstoneDirectory, Config.Instance.HearthstoneLogsDirectoryName);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryReadLatestEndpoint(string root, out string address, out ushort port)
        {
            address = null;
            port = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    return false;

                var rootDir = new DirectoryInfo(root);
                var directories = new[] { rootDir }
                    .Concat(rootDir.GetDirectories())
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Take(5);

                var files = directories
                    .SelectMany(dir => KnownLogFiles.Select(name => new FileInfo(Path.Combine(dir.FullName, name))))
                    .Where(file => file.Exists)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToList();

                foreach (var file in files)
                {
                    if (TryReadEndpointFromFile(file.FullName, out address, out port))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Info("ShowPing endpoint scan failed: " + ex.Message);
                address = null;
                port = 0;
            }

            return false;
        }

        private static bool TryReadEndpointFromFile(string path, out string address, out ushort port)
        {
            address = null;
            port = 0;
            try
            {
                if (!File.Exists(path))
                    return false;

                string text;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    text = ReadTail(stream, InitialTailBytes);
                    if (string.IsNullOrEmpty(text))
                        return false;

                    var found = TryParseLatestEndpoint(text, out address, out port);
                    if (!found && stream.Length > InitialTailBytes)
                    {
                        text = ReadTail(stream, FallbackTailBytes);
                        found = TryParseLatestEndpoint(text, out address, out port);
                    }

                    if (found)
                        return true;
                }

                LogNoMatch(path);
                return false;
            }
            catch
            {
                address = null;
                port = 0;
                return false;
            }
        }

        private static string ReadTail(Stream stream, int maxBytes)
        {
            var bytesToRead = (int)Math.Min(stream.Length, maxBytes);
            if (bytesToRead <= 0)
                return null;

            stream.Seek(-bytesToRead, SeekOrigin.End);
            var buffer = new byte[bytesToRead];
            var offset = 0;
            while (offset < bytesToRead)
            {
                var read = stream.Read(buffer, offset, bytesToRead - offset);
                if (read <= 0)
                    break;
                offset += read;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, offset);
            if (stream.Length > bytesToRead)
            {
                var firstLineEnd = text.IndexOf('\n');
                if (firstLineEnd >= 0 && firstLineEnd + 1 < text.Length)
                    text = text.Substring(firstLineEnd + 1);
            }

            return text;
        }

        private static bool TryParseLatestEndpoint(string text, out string address, out ushort port)
        {
            address = null;
            port = 0;

            Match last = null;
            foreach (var regex in EndpointRegexes)
            {
                var matches = regex.Matches(text);
                if (matches.Count > 0 && (last == null || matches[matches.Count - 1].Index > last.Index))
                    last = matches[matches.Count - 1];
            }

            if (last == null)
                return false;

            return TryParseEndpointValue(last.Groups["endpoint"].Value, out address, out port);
        }

        private static bool TryParseEndpointValue(string value, out string address, out ushort port)
        {
            address = null;
            port = 0;

            var match = EndpointValueRegex.Match(value);
            if (!match.Success)
                return false;

            var host = match.Groups["host"].Value;
            int parsedPort;
            IPAddress parsedAddress;
            if (!IPAddress.TryParse(host, out parsedAddress))
                return false;
            if (!int.TryParse(match.Groups["port"].Value, out parsedPort) || parsedPort <= 0 || parsedPort > 65535)
                return false;

            address = parsedAddress.ToString();
            port = (ushort)parsedPort;
            return true;
        }

        private static void LogNoMatch(string path)
        {
            if (DateTime.UtcNow < nextNoMatchLogUtc)
                return;

            nextNoMatchLogUtc = DateTime.UtcNow.AddMinutes(2);
            Log.Info("ShowPing endpoint not found in log tail: " + path);
        }
    }
}
