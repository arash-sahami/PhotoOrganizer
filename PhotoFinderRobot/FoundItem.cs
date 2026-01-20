using System;

namespace PhotoFinderRobot
{
    public abstract class FoundItem
    {
        public string CurrentFileName { get; set; }

        public abstract DateTime DateTaken { get; }

        public abstract string DestinationSubPath { get; }

        public FoundItem(string path) => CurrentFileName = path;
    }
}
