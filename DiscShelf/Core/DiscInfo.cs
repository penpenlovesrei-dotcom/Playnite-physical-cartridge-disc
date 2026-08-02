using System.Collections.Generic;

namespace DiscShelf.Core
{
    public class DiscInfo
    {
        public string DriveLetter { get; set; }

        public string VolumeLabel { get; set; }

        public List<DiscFile> Files { get; }
            = new List<DiscFile>();


        public bool HasFile(string filename)
        {
            foreach (DiscFile file in Files)
            {
                if (string.Equals(
                    file.Name,
                    filename,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }


        public DiscFile FindFile(string filename)
        {
            foreach (DiscFile file in Files)
            {
                if (string.Equals(
                    file.Name,
                    filename,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }

            return null;
        }
    }
}
