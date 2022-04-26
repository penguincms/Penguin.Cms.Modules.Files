using Microsoft.AspNetCore.Mvc;
using Penguin.Cms.Files;
using Penguin.Cms.Files.Repositories;
using Penguin.Extensions.Strings;
using Penguin.Files.Services;
using Penguin.Web.Data;
using System;
using System.IO;

namespace Penguin.Cms.Modules.Files.Controllers
{
    public class FileController : Controller
    {
        protected DatabaseFileRepository DatabaseFileRepository { get; set; }

        protected FileService FileService { get; set; }

        public FileController(DatabaseFileRepository databaseFileRepository, FileService fileService)
        {
            this.DatabaseFileRepository = databaseFileRepository;
            this.FileService = fileService;
        }

        public ActionResult Download(int Id) => this.Download(this.DatabaseFileRepository.Find(Id) ?? throw new NullReferenceException($"No DatabaseFile found with Id {Id}"));

        public ActionResult ViewByPath(string Path)
        {
            if (Path is null)
            {
                throw new ArgumentNullException(nameof(Path));
            }

            Path = Path.Replace('/', '\\');

            string FullName = System.IO.Path.Combine(this.FileService.GetUserFilesRoot(), Path);

            DatabaseFile thisFile = this.DatabaseFileRepository.GetByFullName(FullName);

            if (thisFile is null)
            {
                return this.NotFound();
            }
            if (!thisFile.IsDirectory)
            {
                return this.Download(thisFile);
            }
            else
            {
                throw new UnauthorizedAccessException();
            }
        }

        private ActionResult Download(DatabaseFile thisFile)
        {
            if (thisFile is null)
            {
                throw new ArgumentNullException(nameof(thisFile));
            }

            string Extension = thisFile.FileName.FromLast(".");
            string MimeType = MimeMappings.GetMimeType(Extension);

            if ((thisFile.Data?.Length ?? 0) == 0)
            {
                if (MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    return this.Content(System.IO.File.ReadAllText(thisFile.FullName));
                }
                else
                {
                    FileStream fileStream = new FileStream(thisFile.FullName, FileMode.Open, FileAccess.Read);

                    FileStreamResult fsResult = new FileStreamResult(fileStream, MimeType)
                    {
                        EnableRangeProcessing = true,
                        FileDownloadName = Path.GetFileName(thisFile.FullName)
                    };

                    return fsResult;
                }
            }
            else
            {
                return this.File(thisFile.Data, MimeType, thisFile.FileName);
            }
        }
    }
}