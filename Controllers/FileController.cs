using Microsoft.AspNetCore.Mvc;
using Penguin.Cms.Files;
using Penguin.Cms.Files.Repositories;
using Penguin.Extensions.String;
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
            DatabaseFileRepository = databaseFileRepository;
            FileService = fileService;
        }

        public ActionResult Download(int Id)
        {
            return Download(DatabaseFileRepository.Find(Id) ?? throw new NullReferenceException($"No DatabaseFile found with Id {Id}"));
        }

        public ActionResult ViewByPath(string Path)
        {
            if (Path is null)
            {
                throw new ArgumentNullException(nameof(Path));
            }

            Path = Path.Replace('/', '\\');

            string FullName = System.IO.Path.Combine(FileService.GetUserFilesRoot(), Path);

            DatabaseFile thisFile = DatabaseFileRepository.GetByFullName(FullName);

            return thisFile is null ? NotFound() : !thisFile.IsDirectory ? Download(thisFile) : throw new UnauthorizedAccessException();
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
                    return Content(System.IO.File.ReadAllText(thisFile.FullName));
                }
                else
                {
                    FileStream fileStream = new(thisFile.FullName, FileMode.Open, FileAccess.Read);

                    FileStreamResult fsResult = new(fileStream, MimeType)
                    {
                        EnableRangeProcessing = true,
                        FileDownloadName = Path.GetFileName(thisFile.FullName)
                    };

                    return fsResult;
                }
            }
            else
            {
                return File(thisFile.Data, MimeType, thisFile.FileName);
            }
        }
    }
}