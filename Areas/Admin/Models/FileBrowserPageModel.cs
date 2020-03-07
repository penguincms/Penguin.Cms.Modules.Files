using Penguin.Cms.Files;
using System.Collections.Generic;
using System.Linq;

namespace Penguin.Cms.Modules.Files.Areas.Admin.Models
{
    public class FileBrowserPageModel
    {
        public string FilePath { get; set; } = string.Empty;

        public List<DatabaseFile> Files { get; } = new List<DatabaseFile>();

        public FileBrowserPageModel()
        {
        }

        public FileBrowserPageModel(IEnumerable<DatabaseFile> files)
        {
            this.Files = files.ToList();
        }
    }
}