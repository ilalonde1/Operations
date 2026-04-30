using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EmailFilerv2
{
    public static class ProjectHelper
    {
        public static List<string> LoadValidProjects()
        {
            string basePath = @"\\Kor-fs01\Projects\Projects";
            var projects = new List<string>();

            try
            {
                var categories = Directory.GetDirectories(basePath);
                foreach (var category in categories)
                {
                    var subfolders = Directory.GetDirectories(category);
                    foreach (var folder in subfolders)
                    {
                        string name = Path.GetFileName(folder);
                        if (name.Length >= 8 && name.Substring(0, 8).Contains("-"))
                        {
                            projects.Add(folder);
                        }
                    }
                }
            }
            catch { }

            return projects.OrderBy(p => p).ToList();
        }
    }
}
