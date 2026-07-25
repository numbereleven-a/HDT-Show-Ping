using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ShowPing
{
    internal sealed class VersionCheckResult
    {
        public VersionCheckResult(string latestVersion, bool updateAvailable, string releasesUrl)
        {
            LatestVersion = latestVersion;
            UpdateAvailable = updateAvailable;
            ReleasesUrl = releasesUrl;
        }

        public string LatestVersion { get; }
        public bool UpdateAvailable { get; }
        public string ReleasesUrl { get; }
    }

    internal static class VersionChecker
    {
        public const string DefaultRepository = "numbereleven-a/HDT-Show-Ping";
        public const string RepositoryEnvironmentVariable = "HDT_SHOWPING_UPDATE_REPOSITORY";
        public const string TokenEnvironmentVariable = "HDT_SHOWPING_UPDATE_TOKEN";

        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly Regex OwnerRegex = new Regex(
            @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$",
            RegexOptions.CultureInvariant);

        private static readonly Regex RepositoryRegex = new Regex(
            @"^[A-Za-z0-9._-]+$",
            RegexOptions.CultureInvariant);

        private static readonly Regex VersionRegex = new Regex(
            @"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$",
            RegexOptions.CultureInvariant);

        public static async Task<VersionCheckResult> CheckAsync(
            Version installedVersion,
            string repository,
            string token,
            CancellationToken cancellationToken)
        {
            string owner;
            string name;
            if (!TryParseRepository(repository, out owner, out name))
                throw new InvalidOperationException("Invalid update repository.");

            var apiUrl = "https://api.github.com/repos/" + owner + "/" + name + "/releases/latest";
            var releasesUrl = "https://github.com/" + owner + "/" + name + "/releases/latest";

            using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
            {
                request.Headers.UserAgent.ParseAdd("Show-Ping-HDT-Plugin");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
                if (!string.IsNullOrWhiteSpace(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

                using (var response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                        var release = serializer.ReadObject(stream) as GitHubRelease;
                        SemanticVersion latest;
                        if (release == null || !SemanticVersion.TryParse(release.TagName, out latest))
                            throw new InvalidDataException("The latest release tag is not a supported version.");

                        var installed = SemanticVersion.FromVersion(installedVersion);
                        return new VersionCheckResult(
                            latest.DisplayText,
                            latest.CompareTo(installed) > 0,
                            releasesUrl);
                    }
                }
            }
        }

        public static string GetConfiguredRepository()
        {
            var repository = Environment.GetEnvironmentVariable(RepositoryEnvironmentVariable);
            return string.IsNullOrWhiteSpace(repository) ? DefaultRepository : repository.Trim();
        }

        public static string GetConfiguredToken()
        {
            return Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
        }

        internal static bool TryParseRepository(string repository, out string owner, out string name)
        {
            owner = null;
            name = null;
            if (string.IsNullOrWhiteSpace(repository))
                return false;

            var parts = repository.Trim().Split('/');
            if (parts.Length != 2 ||
                !OwnerRegex.IsMatch(parts[0]) ||
                !RepositoryRegex.IsMatch(parts[1]) ||
                parts[1] == "." ||
                parts[1] == "..")
            {
                return false;
            }

            owner = parts[0];
            name = parts[1];
            return true;
        }

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }
        }

        private sealed class SemanticVersion : IComparable<SemanticVersion>
        {
            private readonly int[] components;
            private readonly string prerelease;

            private SemanticVersion(int[] components, string prerelease, string displayText)
            {
                this.components = components;
                this.prerelease = prerelease;
                DisplayText = displayText;
            }

            public string DisplayText { get; }

            public static SemanticVersion FromVersion(Version version)
            {
                if (version == null)
                    version = new Version(0, 0);

                return new SemanticVersion(
                    new[]
                    {
                        Math.Max(0, version.Major),
                        Math.Max(0, version.Minor),
                        Math.Max(0, version.Build),
                        Math.Max(0, version.Revision)
                    },
                    null,
                    version.ToString());
            }

            public static bool TryParse(string value, out SemanticVersion version)
            {
                version = null;
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                var trimmed = value.Trim();
                var match = VersionRegex.Match(trimmed);
                if (!match.Success)
                    return false;

                var components = new int[4];
                for (var index = 0; index < components.Length; index++)
                {
                    var group = match.Groups[index + 1];
                    if (group.Success && !int.TryParse(group.Value, out components[index]))
                        return false;
                }

                var displayText = trimmed[0] == 'v' || trimmed[0] == 'V'
                    ? trimmed.Substring(1)
                    : trimmed;
                var prerelease = match.Groups[5].Success ? match.Groups[5].Value : null;
                version = new SemanticVersion(components, prerelease, displayText);
                return true;
            }

            public int CompareTo(SemanticVersion other)
            {
                if (other == null)
                    return 1;

                for (var index = 0; index < components.Length; index++)
                {
                    var comparison = components[index].CompareTo(other.components[index]);
                    if (comparison != 0)
                        return comparison;
                }

                if (prerelease == null)
                    return other.prerelease == null ? 0 : 1;
                if (other.prerelease == null)
                    return -1;

                var left = prerelease.Split('.');
                var right = other.prerelease.Split('.');
                var length = Math.Max(left.Length, right.Length);
                for (var index = 0; index < length; index++)
                {
                    if (index >= left.Length)
                        return -1;
                    if (index >= right.Length)
                        return 1;

                    int leftNumber;
                    int rightNumber;
                    var leftIsNumber = int.TryParse(left[index], out leftNumber);
                    var rightIsNumber = int.TryParse(right[index], out rightNumber);
                    if (leftIsNumber && rightIsNumber)
                    {
                        var comparison = leftNumber.CompareTo(rightNumber);
                        if (comparison != 0)
                            return comparison;
                    }
                    else if (leftIsNumber != rightIsNumber)
                    {
                        return leftIsNumber ? -1 : 1;
                    }
                    else
                    {
                        var comparison = string.CompareOrdinal(left[index], right[index]);
                        if (comparison != 0)
                            return comparison;
                    }
                }

                return 0;
            }
        }
    }
}
