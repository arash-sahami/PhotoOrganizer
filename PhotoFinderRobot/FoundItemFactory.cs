using System;
using System.IO;

namespace PhotoFinderRobot
{
    public static class FoundItemFactory
    {
        private static readonly string[] VideoExtensions = { ".mov", ".mp4", ".m4v", ".avi", ".mkv", ".wmv", ".webm" };
        private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".heic" };

        public static FoundItem GetItem(string path)
        {
            if (File.Exists(path))
            {
                string extension = Path.GetExtension(path).ToLower();
                
                if (Array.Exists(VideoExtensions, ext => ext == extension))
                    return new FoundMovie(path);
                
                if (Array.Exists(PhotoExtensions, ext => ext == extension))
                    return new FoundPhoto(path);
            }
            return null;
        }
    }
}
