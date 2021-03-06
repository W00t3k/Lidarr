using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Languages;
using TagLib;

namespace NzbDrone.Core.Parser
{
    public static class Parser
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Parser));

        private static readonly Regex[] ReportMusicTitleRegex = new[]
        {
            // Track with artist (01 - artist - trackName)
            new Regex(@"(?<trackNumber>\d*){0,1}([-| ]{0,1})(?<artist>[a-zA-Z0-9, ().&_]*)[-| ]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Track without artist (01 - trackName)
            new Regex(@"(?<trackNumber>\d*)[-| .]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Track without trackNumber or artist(trackName)
            new Regex(@"(?<trackNumber>\d*)[-| .]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Track without trackNumber and  with artist(artist - trackName)
            new Regex(@"(?<trackNumber>\d*)[-| .]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Track with artist and starting title (01 - artist - trackName)
            new Regex(@"(?<trackNumber>\d*){0,1}[-| ]{0,1}(?<artist>[a-zA-Z0-9, ().&_]*)[-| ]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private static readonly Regex[] ReportAlbumTitleRegex = new[]
        {
            //ruTracker - (Genre) [Source]? Artist - Discography
            new Regex(@"^(?:\(.+?\))(?:\W*(?:\[(?<source>.+?)\]))?\W*(?<artist>.+?)(?: - )(?<discography>Discography|Discografia).+?(?<startyear>\d{4}).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist - Discography with two years
            new Regex(@"^(?<artist>.+?)(?: - )(?:.+?)?(?<discography>Discography|Discografia).+?(?<startyear>\d{4}).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist - Discography with end year
            new Regex(@"^(?<artist>.+?)(?: - )(?:.+?)?(?<discography>Discography|Discografia).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist Discography with two years
            new Regex(@"^(?<artist>.+?)\W*(?<discography>Discography|Discografia).+?(?<startyear>\d{4}).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist Discography with end year
            new Regex(@"^(?<artist>.+?)\W*(?<discography>Discography|Discografia).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist Discography
            new Regex(@"^(?<artist>.+?)\W*(?<discography>Discography|Discografia)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //ruTracker - (Genre) [Source]? Artist - Album - Year
            new Regex(@"^(?:\(.+?\))(?:\W*(?:\[(?<source>.+?)\]))?\W*(?<artist>.+?)(?: - )(?<album>.+?)(?: - )(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist-Album-Version-Source-Year
            //ex. Imagine Dragons-Smoke And Mirrors-Deluxe Edition-2CD-FLAC-2015-JLM
            new Regex(@"^(?<artist>.+?)[-](?<album>.+?)[-](?:[\(|\[]?)(?<version>.+?(?:Edition)?)(?:[\)|\]]?)[-](?<source>\d?CD|WEB).+?(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist-Album-Source-Year
            //ex. Dani_Sbert-Togheter-WEB-2017-FURY
            new Regex(@"^(?<artist>.+?)[-](?<album>.+?)[-](?<source>\d?CD|WEB).+?(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist - Album (Year) Strict
            new Regex(@"^(?:(?<artist>.+?)(?: - )+)(?<album>.+?)\W*(?:\(|\[).+?(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist - Album (Year)
            new Regex(@"^(?:(?<artist>.+?)(?: - )+)(?<album>.+?)\W*(?:\(|\[)(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist - Album - Year [something]
            new Regex(@"^(?:(?<artist>.+?)(?: - )+)(?<album>.+?)\W*(?: - )(?<releaseyear>\d{4})\W*(?:\(|\[)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist - Album [something] or Artist - Album (something)
            new Regex(@"^(?:(?<artist>.+?)(?: - )+)(?<album>.+?)\W*(?:\(|\[)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist - Album Year
            new Regex(@"^(?:(?<artist>.+?)(?: - )+)(?<album>.+?)\W*(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist-Album (Year) Strict
            //Hyphen no space between artist and album
            new Regex(@"^(?:(?<artist>.+?)(?:-)+)(?<album>.+?)\W*(?:\(|\[).+?(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist-Album (Year)
            //Hyphen no space between artist and album
            new Regex(@"^(?:(?<artist>.+?)(?:-)+)(?<album>.+?)\W*(?:\(|\[)(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist-Album [something] or Artist-Album (something)
            //Hyphen no space between artist and album
            new Regex(@"^(?:(?<artist>.+?)(?:-)+)(?<album>.+?)\W*(?:\(|\[)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist-Album-something-Year
            new Regex(@"^(?:(?<artist>.+?)(?:-)+)(?<album>.+?)(?:-.+?)(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist-Album Year
            //Hyphen no space between artist and album
            new Regex(@"^(?:(?<artist>.+?)(?:-)+)(?:(?<album>.+?)(?:-)+)(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Artist - Year - Album
            // Hypen with no or more spaces between artist/album/year
            new Regex(@"^(?:(?<artist>.+?)(?:-))(?<releaseyear>\d{4})(?:-)(?<album>[^-]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
        };

        private static readonly Regex[] RejectHashedReleasesRegex = new Regex[]
            {
                // Generic match for md5 and mixed-case hashes.
                new Regex(@"^[0-9a-zA-Z]{32}", RegexOptions.Compiled),

                // Generic match for shorter lower-case hashes.
                new Regex(@"^[a-z0-9]{24}$", RegexOptions.Compiled),

                // Format seen on some NZBGeek releases
                // Be very strict with these coz they are very close to the valid 101 ep numbering.
                new Regex(@"^[A-Z]{11}\d{3}$", RegexOptions.Compiled),
                new Regex(@"^[a-z]{12}\d{3}$", RegexOptions.Compiled),

                //Backup filename (Unknown origins)
                new Regex(@"^Backup_\d{5,}S\d{2}-\d{2}$", RegexOptions.Compiled),

                //123 - Started appearing December 2014
                new Regex(@"^123$", RegexOptions.Compiled),

                //abc - Started appearing January 2015
                new Regex(@"^abc$", RegexOptions.Compiled | RegexOptions.IgnoreCase),

                //b00bs - Started appearing January 2015
                new Regex(@"^b00bs$", RegexOptions.Compiled | RegexOptions.IgnoreCase)
            };

        //Regex to detect whether the title was reversed.
        private static readonly Regex ReversedTitleRegex = new Regex(@"[-._ ](p027|p0801|\d{2}E\d{2}S)[-._ ]", RegexOptions.Compiled);

        private static readonly Regex NormalizeRegex = new Regex(@"((?:\b|_)(?<!^)(a(?!$)|an|the|and|or|of)(?:\b|_))|\W|_",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FileExtensionRegex = new Regex(@"\.[a-z0-9]{2,4}$",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        //TODO Rework this Regex for Music
        private static readonly Regex SimpleTitleRegex = new Regex(@"(?:(480|720|1080|2160|320)[ip]|[xh][\W_]?26[45]|DD\W?5\W1|[<>*:|]|848x480|1280x720|1920x1080|3840x2160|4096x2160|(8|10)b(it)?)\s*",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WebsitePrefixRegex = new Regex(@"^\[\s*[a-z]+(\.[a-z]+)+\s*\][- ]*|^www\.[a-z]+\.(?:com|net)[ -]*",
                                                                   RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AirDateRegex = new Regex(@"^(.*?)(?<!\d)((?<airyear>\d{4})[_.-](?<airmonth>[0-1][0-9])[_.-](?<airday>[0-3][0-9])|(?<airmonth>[0-1][0-9])[_.-](?<airday>[0-3][0-9])[_.-](?<airyear>\d{4}))(?!\d)",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SixDigitAirDateRegex = new Regex(@"(?<=[_.-])(?<airdate>(?<!\d)(?<airyear>[1-9]\d{1})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9]))(?=[_.-])",
                                                                        RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CleanReleaseGroupRegex = new Regex(@"^(.*?[-._ ])|(-(RP|1|NZBGeek|Obfuscated|Scrambled|sample|Pre|postbot|xpost))+$",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CleanTorrentSuffixRegex = new Regex(@"\[(?:ettv|rartv|rarbg|cttv)\]$",
                                                                   RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ReleaseGroupRegex = new Regex(@"-(?<releasegroup>[a-z0-9]+)(?<!MP3|ALAC|FLAC|WEB)(?:\b|[-._ ])",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AnimeReleaseGroupRegex = new Regex(@"^(?:\[(?<subgroup>(?!\s).+?(?<!\s))\](?:_|-|\s|\.)?)",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YearInTitleRegex = new Regex(@"^(?<title>.+?)(?:\W|_)?(?<year>\d{4})",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WordDelimiterRegex = new Regex(@"(\s|\.|,|_|-|=|\|)+", RegexOptions.Compiled);
        private static readonly Regex PunctuationRegex = new Regex(@"[^\w\s]", RegexOptions.Compiled);
        private static readonly Regex CommonWordRegex = new Regex(@"\b(a|an|the|and|or|of)\b\s?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SpecialEpisodeWordRegex = new Regex(@"\b(part|special|edition|christmas)\b\s?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DuplicateSpacesRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        private static readonly Regex RequestInfoRegex = new Regex(@"\[.+?\]", RegexOptions.Compiled);

        private static readonly string[] Numbers = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        private static readonly Regex[] CommonTagRegex = new Regex[] {
            new Regex(@"(\[|\()*\b((featuring|feat.|feat|ft|ft.)\s{1}){1}\s*.*(\]|\))*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?:\(|\[)(?:[^\(\[]*)(?:version|limited|deluxe|single|clean|album|special|bonus|promo|remastered)(?:[^\)\]]*)(?:\)|\])", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        private static readonly Regex[] BracketRegex = new Regex[]
        {
            new Regex(@"\(.*\)", RegexOptions.Compiled),
            new Regex(@"\[.*\]", RegexOptions.Compiled)
        };

        private static readonly Regex AfterDashRegex = new Regex(@"[-:].*", RegexOptions.Compiled);
        
        public static ParsedTrackInfo ParseMusicPath(string path)
        {
            var fileInfo = new FileInfo(path);

            ParsedTrackInfo result;

            if (MediaFiles.MediaFileExtensions.Extensions.Contains(fileInfo.Extension))
            {
                result = ParseAudioTags(path);
            }
            else
            {
                result = null;
            }

            // TODO: Check if it is common that we might need to fallback to parser to gather details
            //var result = ParseMusicTitle(fileInfo.Name);


            if (result == null)
            {
                Logger.Debug("Attempting to parse track info using directory and file names. {0}", fileInfo.Directory.Name);
                result = ParseMusicTitle(fileInfo.Directory.Name + " " + fileInfo.Name);
            }

            if (result == null)
            {
                Logger.Debug("Attempting to parse track info using directory name. {0}", fileInfo.Directory.Name);
                result = ParseMusicTitle(fileInfo.Directory.Name + fileInfo.Extension);
            }

            return result;
        }

        public static ParsedTrackInfo ParseMusicTitle(string title)
        {
            try
            {
                if (!ValidateBeforeParsing(title)) return null;

                Logger.Debug("Parsing string '{0}'", title);

                if (ReversedTitleRegex.IsMatch(title))
                {
                    var titleWithoutExtension = RemoveFileExtension(title).ToCharArray();
                    Array.Reverse(titleWithoutExtension);

                    title = new string(titleWithoutExtension) + title.Substring(titleWithoutExtension.Length);

                    Logger.Debug("Reversed name detected. Converted to '{0}'", title);
                }

                var releaseTitle = RemoveFileExtension(title);

                var simpleTitle = SimpleTitleRegex.Replace(releaseTitle, string.Empty);

                // TODO: Quick fix stripping [url] - prefixes.
                simpleTitle = WebsitePrefixRegex.Replace(simpleTitle, string.Empty);

                simpleTitle = CleanTorrentSuffixRegex.Replace(simpleTitle, string.Empty);

                var airDateMatch = AirDateRegex.Match(simpleTitle);
                if (airDateMatch.Success)
                {
                    simpleTitle = airDateMatch.Groups[1].Value + airDateMatch.Groups["airyear"].Value + "." + airDateMatch.Groups["airmonth"].Value + "." + airDateMatch.Groups["airday"].Value;
                }

                var sixDigitAirDateMatch = SixDigitAirDateRegex.Match(simpleTitle);
                if (sixDigitAirDateMatch.Success)
                {
                    var airYear = sixDigitAirDateMatch.Groups["airyear"].Value;
                    var airMonth = sixDigitAirDateMatch.Groups["airmonth"].Value;
                    var airDay = sixDigitAirDateMatch.Groups["airday"].Value;

                    if (airMonth != "00" || airDay != "00")
                    {
                        var fixedDate = string.Format("20{0}.{1}.{2}", airYear, airMonth, airDay);

                        simpleTitle = simpleTitle.Replace(sixDigitAirDateMatch.Groups["airdate"].Value, fixedDate);
                    }
                }

                foreach (var regex in ReportMusicTitleRegex)
                {
                    var match = regex.Matches(simpleTitle);

                    if (match.Count != 0)
                    {
                        Logger.Trace(regex);
                        try
                        {
                            var result = ParseMatchMusicCollection(match);

                            if (result != null)
                            {
                                result.Quality = QualityParser.ParseQuality(title, null, 0);
                                Logger.Debug("Quality parsed: {0}", result.Quality);

                                return result;
                            }
                        }
                        catch (InvalidDateException ex)
                        {
                            Logger.Debug(ex, ex.Message);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!title.ToLower().Contains("password") && !title.ToLower().Contains("yenc"))
                    Logger.Error(e, "An error has occurred while trying to parse {0}", title);
            }

            Logger.Debug("Unable to parse {0}", title);
            return null;
        }

        public static ParsedAlbumInfo ParseAlbumTitleWithSearchCriteria(string title, Artist artist, List<Album> album)
        {
            try
            {
                if (!ValidateBeforeParsing(title)) return null;

                Logger.Debug("Parsing string '{0}' using search criteria artist: '{1}' album: '{2}'",
                             title, artist.Name, string.Join(", ", album.Select(a => a.Title)));

                if (ReversedTitleRegex.IsMatch(title))
                {
                    var titleWithoutExtension = RemoveFileExtension(title).ToCharArray();
                    Array.Reverse(titleWithoutExtension);

                    title = new string (titleWithoutExtension) + title.Substring(titleWithoutExtension.Length);

                    Logger.Debug("Reversed name detected. Converted to '{0}'", title);
                }

                var releaseTitle = RemoveFileExtension(title);

                var simpleTitle = SimpleTitleRegex.Replace(releaseTitle, string.Empty);

                simpleTitle = WebsitePrefixRegex.Replace(simpleTitle, string.Empty);

                simpleTitle = CleanTorrentSuffixRegex.Replace(simpleTitle, string.Empty);

                var escapedArtist = Regex.Escape(artist.Name).Replace(@"\ ", @"[\W_]");
                var escapedAlbums = Regex.Escape(string.Join("|", album.Select(s => s.Title).ToList())).Replace(@"\ ", @"[\W_]");;

                var releaseRegex = new Regex(@"^(\W*|\b)(?<artist>" + escapedArtist + @")(\W*|\b).*(\W*|\b)(?<album>" + escapedAlbums + @")(\W*|\b)", RegexOptions.IgnoreCase);


                var match = releaseRegex.Matches(simpleTitle);

                if (match.Count != 0)
                {
                    try
                    {
                        var result = ParseAlbumMatchCollection(match);

                        if (result != null)
                        {
                            result.Language = LanguageParser.ParseLanguage(releaseTitle);
                            Logger.Debug("Language parsed: {0}", result.Language);

                            result.Quality = QualityParser.ParseQuality(title, null, 0);
                            Logger.Debug("Quality parsed: {0}", result.Quality);

                            result.ReleaseGroup = ParseReleaseGroup(releaseTitle);

                            var subGroup = GetSubGroup(match);
                            if (!subGroup.IsNullOrWhiteSpace())
                            {
                                result.ReleaseGroup = subGroup;
                            }

                            Logger.Debug("Release Group parsed: {0}", result.ReleaseGroup);

                            result.ReleaseHash = GetReleaseHash(match);
                            if (!result.ReleaseHash.IsNullOrWhiteSpace())
                            {
                                Logger.Debug("Release Hash parsed: {0}", result.ReleaseHash);
                            }

                            return result;
                        }
                    }
                    catch (InvalidDateException ex)
                    {
                        Logger.Debug(ex, ex.Message);
                    }
                }
            }
            catch (Exception e)
            {
                if (!title.ToLower().Contains("password") && !title.ToLower().Contains("yenc"))
                    Logger.Error(e, "An error has occurred while trying to parse {0}", title);
            }

            Logger.Debug("Unable to parse {0}", title);
            return null;
        }

        public static ParsedAlbumInfo ParseAlbumTitle(string title)
        {
            try
            {
                if (!ValidateBeforeParsing(title)) return null;

                Logger.Debug("Parsing string '{0}'", title);


                if (ReversedTitleRegex.IsMatch(title))
                {
                    var titleWithoutExtension = RemoveFileExtension(title).ToCharArray();
                    Array.Reverse(titleWithoutExtension);

                    title = new string(titleWithoutExtension) + title.Substring(titleWithoutExtension.Length);

                    Logger.Debug("Reversed name detected. Converted to '{0}'", title);
                }

                var releaseTitle = RemoveFileExtension(title);

                var simpleTitle = SimpleTitleRegex.Replace(releaseTitle, string.Empty);

                // TODO: Quick fix stripping [url] - prefixes.
                simpleTitle = WebsitePrefixRegex.Replace(simpleTitle, string.Empty);

                simpleTitle = CleanTorrentSuffixRegex.Replace(simpleTitle, string.Empty);

                var airDateMatch = AirDateRegex.Match(simpleTitle);
                if (airDateMatch.Success)
                {
                    simpleTitle = airDateMatch.Groups[1].Value + airDateMatch.Groups["airyear"].Value + "." + airDateMatch.Groups["airmonth"].Value + "." + airDateMatch.Groups["airday"].Value;
                }

                var sixDigitAirDateMatch = SixDigitAirDateRegex.Match(simpleTitle);
                if (sixDigitAirDateMatch.Success)
                {
                    var airYear = sixDigitAirDateMatch.Groups["airyear"].Value;
                    var airMonth = sixDigitAirDateMatch.Groups["airmonth"].Value;
                    var airDay = sixDigitAirDateMatch.Groups["airday"].Value;

                    if (airMonth != "00" || airDay != "00")
                    {
                        var fixedDate = string.Format("20{0}.{1}.{2}", airYear, airMonth, airDay);

                        simpleTitle = simpleTitle.Replace(sixDigitAirDateMatch.Groups["airdate"].Value, fixedDate);
                    }
                }

                foreach (var regex in ReportAlbumTitleRegex)
                {
                    var match = regex.Matches(simpleTitle);

                    if (match.Count != 0)
                    {
                        Logger.Trace(regex);
                        try
                        {
                            var result = ParseAlbumMatchCollection(match);

                            if (result != null)
                            {
                                result.Language = LanguageParser.ParseLanguage(releaseTitle);
                                Logger.Debug("Language parsed: {0}", result.Language);

                                result.Quality = QualityParser.ParseQuality(title, null, 0);
                                Logger.Debug("Quality parsed: {0}", result.Quality);

                                result.ReleaseGroup = ParseReleaseGroup(releaseTitle);

                                var subGroup = GetSubGroup(match);
                                if (!subGroup.IsNullOrWhiteSpace())
                                {
                                    result.ReleaseGroup = subGroup;
                                }

                                Logger.Debug("Release Group parsed: {0}", result.ReleaseGroup);

                                result.ReleaseHash = GetReleaseHash(match);
                                if (!result.ReleaseHash.IsNullOrWhiteSpace())
                                {
                                    Logger.Debug("Release Hash parsed: {0}", result.ReleaseHash);
                                }

                                return result;
                            }
                        }
                        catch (InvalidDateException ex)
                        {
                            Logger.Debug(ex, ex.Message);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!title.ToLower().Contains("password") && !title.ToLower().Contains("yenc"))
                    Logger.Error(e, "An error has occurred while trying to parse {0}", title);
            }

            Logger.Debug("Unable to parse {0}", title);
            return null;
        }

        public static string CleanArtistName(this string name)
        {
            long number = 0;

            //If Title only contains numbers return it as is.
            if (long.TryParse(name, out number))
                return name;

            return NormalizeRegex.Replace(name, string.Empty).ToLower().RemoveAccent();
        }

        public static string NormalizeTrackTitle(this string title)
        {
            title = SpecialEpisodeWordRegex.Replace(title, string.Empty);
            title = PunctuationRegex.Replace(title, " ");
            title = DuplicateSpacesRegex.Replace(title, " ");

            return title.Trim().ToLower();
        }

        public static string NormalizeTitle(string title)
        {
            title = WordDelimiterRegex.Replace(title, " ");
            title = PunctuationRegex.Replace(title, string.Empty);
            title = CommonWordRegex.Replace(title, string.Empty);
            title = DuplicateSpacesRegex.Replace(title, " ");

            return title.Trim().ToLower();
        }

        public static string ParseReleaseGroup(string title)
        {
            title = title.Trim();
            title = RemoveFileExtension(title);
            title = WebsitePrefixRegex.Replace(title, "");

            var animeMatch = AnimeReleaseGroupRegex.Match(title);

            if (animeMatch.Success)
            {
                return animeMatch.Groups["subgroup"].Value;
            }

            title = CleanReleaseGroupRegex.Replace(title, "");

            var matches = ReleaseGroupRegex.Matches(title);

            if (matches.Count != 0)
            {
                var group = matches.OfType<Match>().Last().Groups["releasegroup"].Value;
                int groupIsNumeric;

                if (int.TryParse(group, out groupIsNumeric))
                {
                    return null;
                }

                return group;
            }

            return null;
        }

        public static string RemoveFileExtension(string title)
        {
            title = FileExtensionRegex.Replace(title, m =>
                {
                    var extension = m.Value.ToLower();
                    if (MediaFiles.MediaFileExtensions.Extensions.Contains(extension) || new[] { ".par2", ".nzb" }.Contains(extension))
                    {
                        return string.Empty;
                    }
                    return m.Value;
                });

            return title;
        }

        public static string CleanAlbumTitle(string album)
        {
            return CommonTagRegex[1].Replace(album, string.Empty).Trim();
        }

        public static string RemoveBracketsAndContents(this string album)
        {
            var intermediate = album;
            foreach (var regex in BracketRegex)
            {
                intermediate = regex.Replace(intermediate, string.Empty).Trim();
            }
            
            return intermediate;
        }

        public static string RemoveAfterDash(this string text)
        {
            return AfterDashRegex.Replace(text, string.Empty).Trim();
        }

        public static string CleanTrackTitle(string title)
        {
            var intermediateTitle = title;
            foreach (var regex in CommonTagRegex)
            {
                intermediateTitle = regex.Replace(intermediateTitle, string.Empty).Trim();
            }

            return intermediateTitle;
        }

        private static ParsedTrackInfo ParseAudioTags(string path)
        {
            var file = TagLib.File.Create(path);
            Logger.Debug("Starting Tag Parse for {0}", file.Name);

            var trackNumber = file.Tag.Track;
            var trackTitle = file.Tag.Title;
            var discNumber = (int)file.Tag.Disc;

            var artist = file.Tag.FirstAlbumArtist;

            if (artist.IsNullOrWhiteSpace())
            {
                artist = file.Tag.FirstPerformer;
            }

            var artistTitleInfo = new ArtistTitleInfo
            {
                Title = artist,
                Year = (int)file.Tag.Year
            };

            var temp = new int[1];
            temp[0] = (int)trackNumber;

            var result = new ParsedTrackInfo
            {
                Language = Language.English, //TODO Parse from Tag/Mediainfo
                AlbumTitle = file.Tag.Album,
                ArtistTitle = artist,
                ArtistMBId = file.Tag.MusicBrainzArtistId,
                ReleaseMBId = file.Tag.MusicBrainzReleaseId,
                DiscNumber = discNumber,
                TrackMBId = file.Tag.MusicBrainzTrackId,
                TrackNumbers = temp,
                ArtistTitleInfo = artistTitleInfo,
                Title = trackTitle
            };
            
            Logger.Trace("File Tags Parsed: Artist: {0}, Album: {1}, Disc: {2}, Track Numbers(s): {3}, TrackTitle: {4}", result.ArtistTitle, result.AlbumTitle, result.DiscNumber, trackNumber, result.Title);

            foreach (ICodec codec in file.Properties.Codecs)
            {
                IAudioCodec acodec = codec as IAudioCodec;
                IVideoCodec vcodec = codec as IVideoCodec;

                if (acodec != null && (acodec.MediaTypes & MediaTypes.Audio) != MediaTypes.None)
                {
                    Logger.Debug("Audio Properties : " + acodec.Description + ", Bitrate: " + acodec.AudioBitrate + ", Sample Size: " +
                        file.Properties.BitsPerSample + ", SampleRate: " + acodec.AudioSampleRate + ", Channels: " + acodec.AudioChannels);

                    result.Quality = QualityParser.ParseQuality(file.Name, acodec.Description, acodec.AudioBitrate, file.Properties.BitsPerSample);
                    Logger.Debug("Quality parsed: {0}", result.Quality);
                }
            }

            return result;
        }

        private static ParsedTrackInfo ParseMatchMusicCollection(MatchCollection matchCollection)
        {
            var artistName = matchCollection[0].Groups["artist"].Value./*Removed for cases like Will.I.Am Replace('.', ' ').*/Replace('_', ' ');
            artistName = RequestInfoRegex.Replace(artistName, "").Trim(' ');

            // Coppied from Radarr (https://github.com/Radarr/Radarr/blob/develop/src/NzbDrone.Core/Parser/Parser.cs)
            // TODO: Split into separate method and write unit tests for. 
            var parts = artistName.Split('.');
            artistName = "";
            int n = 0;
            bool previousAcronym = false;
            string nextPart = "";
            foreach (var part in parts)
            {
                if (parts.Length >= n + 2)
                {
                    nextPart = parts[n + 1];
                }
                if (part.Length == 1 && part.ToLower() != "a" && !int.TryParse(part, out n))
                {
                    artistName += part + ".";
                    previousAcronym = true;
                }
                else if (part.ToLower() == "a" && (previousAcronym == true || nextPart.Length == 1))
                {
                    artistName += part + ".";
                    previousAcronym = true;
                }
                else
                {
                    if (previousAcronym)
                    {
                        artistName += " ";
                        previousAcronym = false;
                    }
                    artistName += part + " ";
                }
                n++;
            }

            artistName = artistName.Trim(' ');

            int trackNumber;
            int.TryParse(matchCollection[0].Groups["trackNumber"].Value, out trackNumber);

            ParsedTrackInfo result = new ParsedTrackInfo();

            result.ArtistTitle = artistName;
            result.ArtistTitleInfo = GetArtistTitleInfo(result.ArtistTitle);

            Logger.Debug("Track Parsed. {0}", result);
            return result;
        }

        private static ArtistTitleInfo GetArtistTitleInfo(string title)
        {
            var artistTitleInfo = new ArtistTitleInfo();
            artistTitleInfo.Title = title;

            return artistTitleInfo;
        }

        public static string ParseArtistName(string title)
        {
            Logger.Debug("Parsing string '{0}'", title);

            var parseResult = ParseAlbumTitle(title);

            if (parseResult == null)
            {
                return CleanArtistName(title);
            }

            return parseResult.ArtistName;
        }

        private static ParsedAlbumInfo ParseAlbumMatchCollection(MatchCollection matchCollection)
        {
            var artistName = matchCollection[0].Groups["artist"].Value.Replace('.', ' ').Replace('_', ' ');
            var albumTitle = matchCollection[0].Groups["album"].Value.Replace('.', ' ').Replace('_', ' ');
            var releaseVersion = matchCollection[0].Groups["version"].Value.Replace('.', ' ').Replace('_', ' ');
            artistName = RequestInfoRegex.Replace(artistName, "").Trim(' ');
            albumTitle = RequestInfoRegex.Replace(albumTitle, "").Trim(' ');
            releaseVersion = RequestInfoRegex.Replace(releaseVersion, "").Trim(' ');

            int releaseYear;
            int.TryParse(matchCollection[0].Groups["releaseyear"].Value, out releaseYear);

            ParsedAlbumInfo result;

            result = new ParsedAlbumInfo();

            result.ArtistName = artistName;
            result.AlbumTitle = albumTitle;
            result.ArtistTitleInfo = GetArtistTitleInfo(result.ArtistName);
            result.ReleaseDate = releaseYear.ToString();
            result.ReleaseVersion = releaseVersion;

            if (matchCollection[0].Groups["discography"].Success)
            {
                int discStart;
                int discEnd;
                int.TryParse(matchCollection[0].Groups["startyear"].Value, out discStart);
                int.TryParse(matchCollection[0].Groups["endyear"].Value, out discEnd);
                result.Discography = true;

                if (discStart > 0 && discEnd > 0)
                {
                    result.DiscographyStart = discStart;
                    result.DiscographyEnd = discEnd;
                }
                else if (discEnd > 0)
                {
                    result.DiscographyEnd = discEnd;
                }

                result.AlbumTitle = "Discography";
            }

            Logger.Debug("Album Parsed. {0}", result);

            return result;
        }

        private static bool ValidateBeforeParsing(string title)
        {
            if (title.ToLower().Contains("password") && title.ToLower().Contains("yenc"))
            {
                Logger.Debug("");
                return false;
            }

            if (!title.Any(char.IsLetterOrDigit))
            {
                return false;
            }

            var titleWithoutExtension = RemoveFileExtension(title);

            if (RejectHashedReleasesRegex.Any(v => v.IsMatch(titleWithoutExtension)))
            {
                Logger.Debug("Rejected Hashed Release Title: " + title);
                return false;
            }

            return true;
        }

        private static string GetSubGroup(MatchCollection matchCollection)
        {
            var subGroup = matchCollection[0].Groups["subgroup"];

            if (subGroup.Success)
            {
                return subGroup.Value;
            }

            return string.Empty;
        }

        private static string GetReleaseHash(MatchCollection matchCollection)
        {
            var hash = matchCollection[0].Groups["hash"];

            if (hash.Success)
            {
                var hashValue = hash.Value.Trim('[', ']');

                if (hashValue.Equals("1280x720"))
                {
                    return string.Empty;
                }

                return hashValue;
            }

            return string.Empty;
        }

        private static int ParseNumber(string value)
        {
            int number;

            if (int.TryParse(value, out number))
            {
                return number;
            }

            number = Array.IndexOf(Numbers, value.ToLower());

            if (number != -1)
            {
                return number;
            }

            throw new FormatException(string.Format("{0} isn't a number", value));
        }
    }
}
