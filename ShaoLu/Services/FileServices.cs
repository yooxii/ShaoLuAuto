using Microsoft.Win32;
using System.IO;

namespace ShaoLu.Services
{
    public class FileServices
    {
        public string OpenPathDialog(string title = "Open File", string filter = "All File|*.*", string initPath = null, bool isDir = false)
        {
            OpenFileDialog dialog = new()
            {
                Title = title,
                Filter = filter,
                InitialDirectory = initPath
            };
            bool? result = dialog.ShowDialog();
            return result == true ? isDir ? Path.GetDirectoryName(dialog.FileName) : dialog.FileName : null;
        }

        public string SavePathDialog(string title = "Open File", string filter = "All File|*.*", string saveName = "", string initPath = null)
        {
            SaveFileDialog dialog = new()
            {
                Title = title,
                FileName = saveName,
                Filter = filter,
                InitialDirectory = initPath
            };
            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }
    }
}
