using System;
using System.IO;

namespace PhotoFinderRobot
{
    public class FoundPhoto : FoundItem
    {
        public override DateTime DateTaken { get; }

        public override string DestinationSubPath
        {
            get => Path.Combine(DateTaken.ToString("yyyy-MM-dd"));
        }

        public FoundPhoto(string path)
            : base(path)
        {
            // Try to get the actual date taken from EXIF metadata
            DateTime? dateTaken = Finder.PhotoTakenDatetime(CurrentFileName);
            
            // Fall back to file modification time if EXIF data is unavailable
            DateTaken = dateTaken ?? Finder.PhotoModifiedFileDatetime(CurrentFileName);
        }
    }
}
