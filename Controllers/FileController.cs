using Microsoft.AspNetCore.Mvc;
using Penguin.Cms.Files;
using Penguin.Cms.Files.Repositories;
using Penguin.Extensions.Strings;
using Penguin.Files.Services;
using Penguin.Web.Data;
using System;

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

        public ActionResult Download(DatabaseFile thisFile)
        {
            if (thisFile is null)
            {
                throw new ArgumentNullException(nameof(thisFile));
            }

            string Extension = thisFile.FileName.FromLast(".");
            string MimeType = MimeMappings.GetMimeType(Extension);

            if (thisFile.Data.Length == 0)
            {
                if (MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    return this.Content(System.IO.File.ReadAllText(thisFile.FullName));
                }
                else
                {
                    return this.File(System.IO.File.ReadAllBytes(thisFile.FullName), MimeType);
                }
            }
            else
            {
                return this.File(thisFile.Data, MimeType, thisFile.FileName);
            }
        }

        public ActionResult Download(int Id) => this.Download(this.DatabaseFileRepository.Find(Id) ?? throw new NullReferenceException($"No DatabaseFile found with Id {Id}"));

        public ActionResult ViewByPath(string Path)
        {
            if (Path is null)
            {
                throw new ArgumentNullException(nameof(Path));
            }

            Path = Path.Replace('/', '\\');

            string FullName = System.IO.Path.Combine((FileService as FileService).GetUserFilesRoot(), Path);

            DatabaseFile thisFile = this.DatabaseFileRepository.GetByFullName(FullName);

            if (thisFile is null)
            {
                return NotFound();
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
    }
}