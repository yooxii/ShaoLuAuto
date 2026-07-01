using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImageTool
{
    public class Folder : List<Image>
    {
        public String Path { get; }

        public Folder() { }

        public Folder(string path)
        {
            Path = path;

            var files = new DirectoryInfo(path).EnumerateFiles().Where(t => (t.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary)) == 0);

            if (files.Any())
            {
                foreach (var file in files)
                {
                    if (SupportedFiles.IsSupportedFile(System.IO.Path.GetExtension(file.FullName)))
                    {
                        Add(new Image(file.FullName));
                    }
                }
            }
        }
    }
}
