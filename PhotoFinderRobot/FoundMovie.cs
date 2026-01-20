using System;
using System.IO;

namespace PhotoFinderRobot
{
    public class FoundMovie : FoundItem
    {
        public override DateTime DateTaken { get; }

        public override string DestinationSubPath
        {
            get => Path.Combine(DateTaken.ToString("yyyy-MM-dd"));
        }

        public FoundMovie(string path)
            : base(path)
        {
            // Try to get the actual recording date from video metadata
            DateTime? recordedDate = Finder.VideoRecordedDatetime(path);
            
            // Fall back to file modification time if metadata is unavailable
            DateTaken = recordedDate ?? File.GetLastWriteTime(path);
        }
    }
}
