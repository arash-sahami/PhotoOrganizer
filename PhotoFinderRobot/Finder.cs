using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PhotoFinderRobot
{
    public class Finder
    {
        // EXIF property IDs
        private const int ExifDateTimeOriginal = 36867;  // 0x9003 - When photo was taken
        private const int ExifDateTimeDigitized = 36868; // 0x9004 - When image was digitized
        private const int ExifDateTime = 306;            // 0x0132 - File modification date

        public static DateTime? PhotoTakenDatetime(string path)
        {
            try
            {
                using var image = Image.FromFile(path);
                
                // Try DateTimeOriginal first (when the photo was actually taken)
                DateTime? date = GetExifDateTime(image, ExifDateTimeOriginal);
                if (date.HasValue)
                    return date;

                // Fall back to DateTimeDigitized
                date = GetExifDateTime(image, ExifDateTimeDigitized);
                if (date.HasValue)
                    return date;

                // Fall back to DateTime (file modification in EXIF)
                date = GetExifDateTime(image, ExifDateTime);
                if (date.HasValue)
                    return date;
            }
            catch
            {
                // Image couldn't be loaded or has no EXIF data
            }

            return null;
        }

        private static DateTime? GetExifDateTime(Image image, int propertyId)
        {
            try
            {
                if (!image.PropertyIdList.Contains(propertyId))
                    return null;

                var propItem = image.GetPropertyItem(propertyId);
                if (propItem?.Value == null)
                    return null;

                // EXIF date format: "yyyy:MM:dd HH:mm:ss"
                string dateStr = Encoding.ASCII.GetString(propItem.Value).Trim('\0', ' ');
                
                if (DateTime.TryParseExact(dateStr, "yyyy:MM:dd HH:mm:ss", 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                {
                    return result;
                }
            }
            catch
            {
                // Property couldn't be read
            }

            return null;
        }

        [Obsolete("Use PhotoTakenDatetime(string path) instead")]
        public static DateTime? PhotoTakenDatetime(Image input)
        {
            try
            {
                string str1 = Encoding.UTF8.GetString(input.GetPropertyItem(306).Value).Trim();
                string str2 = str1.Substring(str1.IndexOf(" "), str1.Length - str1.IndexOf(" "));
                return new DateTime?(DateTime.Parse(str1.Substring(0, 10).Replace(":", "-") + str2));
            }
            catch
            {
                return new DateTime?();
            }
            finally
            {
                input.Dispose();
            }
        }

        public static DateTime PhotoCreateFileDatetime(string path) => new FileInfo(path).CreationTime;

        public static DateTime PhotoModifiedFileDatetime(string path)
        {
            return new FileInfo(path).LastWriteTime;
        }

        public static DateTime? VideoRecordedDatetime(string path)
        {
            // First, try Windows Shell to get "Media created" date - most reliable for videos
            DateTime? shellDate = GetMediaCreatedDateViaShell(path);
            if (shellDate.HasValue)
                return shellDate;

            // Fall back to TagLib for other metadata
            try
            {
                using var file = TagLib.File.Create(path);
                
                // For MP4/MOV files, try to get creation time from Apple tag
                if (file is TagLib.Mpeg4.File)
                {
                    var appleTag = file.GetTag(TagLib.TagTypes.Apple) as TagLib.Mpeg4.AppleTag;
                    if (appleTag != null)
                    {
                        // Try to find the creation date in Apple metadata
                        string dateText = appleTag.GetDashBox("com.apple.quicktime", "creationdate");
                        if (!string.IsNullOrEmpty(dateText) && TryParseVideoDate(dateText, out DateTime creationDate))
                            return creationDate;
                        
                        if (appleTag.DateTagged.HasValue)
                            return appleTag.DateTagged.Value;
                    }
                }

                // Try DateTagged (works for many formats)
                if (file.Tag.DateTagged.HasValue)
                    return file.Tag.DateTagged.Value;

                // Check Year tag as fallback
                if (file.Tag.Year > 1900 && file.Tag.Year < 2100)
                {
                    return new DateTime((int)file.Tag.Year, 1, 1);
                }
            }
            catch
            {
                // If TagLib fails, continue to return null
            }

            return null;
        }

        private static DateTime? GetMediaCreatedDateViaShell(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                string fileName = Path.GetFileName(path);

                // Use dynamic COM interop for Shell32
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic folder = shell.NameSpace(directory);
                if (folder == null) return null;

                dynamic item = folder.ParseName(fileName);
                if (item == null) return null;

                // Property indices for video dates (these are Windows Shell property indices):
                // 208 = Media created (when the video was recorded)
                // 209 = Date released  
                // 12 = Date taken
                
                // Try "Media created" first (index 208) - most reliable for videos
                string mediaCreated = folder.GetDetailsOf(item, 208);
                if (!string.IsNullOrWhiteSpace(mediaCreated) && TryParseShellDate(mediaCreated, out DateTime date))
                    return date;

                // Try "Date taken" (index 12)
                string dateTaken = folder.GetDetailsOf(item, 12);
                if (!string.IsNullOrWhiteSpace(dateTaken) && TryParseShellDate(dateTaken, out date))
                    return date;
            }
            catch
            {
                // Shell access failed
            }

            return null;
        }

        private static bool TryParseShellDate(string dateStr, out DateTime result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(dateStr))
                return false;

            // Remove any special Unicode characters that Windows sometimes adds
            dateStr = new string(dateStr.Where(c => !char.IsControl(c) && c != '\u200E' && c != '\u200F').ToArray()).Trim();

            if (string.IsNullOrWhiteSpace(dateStr))
                return false;

            // Try parsing with current culture (Windows returns localized dates)
            if (DateTime.TryParse(dateStr, CultureInfo.CurrentCulture, DateTimeStyles.None, out result))
                return true;

            // Try invariant culture
            if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;

            return false;
        }

        private static bool TryParseVideoDate(string dateStr, out DateTime result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(dateStr))
                return false;

            // Common video date formats
            string[] formats = new[]
            {
                "yyyy-MM-ddTHH:mm:sszzz",      // ISO 8601 with timezone
                "yyyy-MM-ddTHH:mm:ss.fffzzz",  // ISO 8601 with milliseconds  
                "yyyy-MM-ddTHH:mm:ssZ",        // ISO 8601 UTC
                "yyyy-MM-ddTHH:mm:ss",         // ISO 8601 without timezone
                "yyyy-MM-dd HH:mm:ss",         // Simple datetime
                "yyyy:MM:dd HH:mm:ss",         // EXIF style
                "yyyy-MM-dd",                  // Date only
                "yyyy"                         // Year only
            };

            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(dateStr.Trim(), format, 
                    CultureInfo.InvariantCulture, 
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, 
                    out result))
                {
                    return true;
                }
            }

            // Try general parse as last resort
            return DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, 
                DateTimeStyles.AllowWhiteSpaces, out result);
        }
    }
}
