using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Penguin.Cms.Errors;
using Penguin.Cms.Files;
using Penguin.Cms.Files.Repositories;
using Penguin.Cms.Modules.Dynamic.Areas.Admin.Controllers;
using Penguin.Cms.Modules.Files.Areas.Admin.Models;
using Penguin.Cms.Modules.Files.Constants.Strings;
using Penguin.Configuration.Abstractions.Extensions;
using Penguin.Configuration.Abstractions.Interfaces;
using Penguin.Extensions.String;
using Penguin.Files.Services;
using Penguin.Messaging.Core;
using Penguin.Persistence.Abstractions.Interfaces;
using Penguin.Security.Abstractions;
using Penguin.Security.Abstractions.Interfaces;
using Penguin.Web.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Penguin.Cms.Modules.Files.Areas.Admin.Controllers
{
    public class FileController : ObjectManagementController<DatabaseFile>
    {
        protected class FileUpload
        {
            public byte[] Data { get; set; }

            public string Path { get; set; }

            public FileUpload(byte[] data, string path)
            {
                Data = data;
                Path = path;
            }
        }

        protected IProvideConfigurations ConfigurationService { get; set; }

        protected DatabaseFileRepository DatabaseFileRepository { get; set; }

        protected IRepository<AuditableError> ErrorRepository { get; set; }

        protected FileService FileService { get; set; }

        protected MessageBus MessageBus { get; set; }

        protected ISecurityProvider<DatabaseFile>? SecurityProvider { get; set; }

        public FileController(IUserSession userSession, DatabaseFileRepository databaseFileRepository, IRepository<AuditableError> errorRepository, IProvideConfigurations configurationService, IServiceProvider serviceProvider, FileService fileService, MessageBus messageBus, ISecurityProvider<DatabaseFile>? securityProvider = null) : base(serviceProvider, userSession)
        {
            SecurityProvider = securityProvider;
            ;
            DatabaseFileRepository = databaseFileRepository;
            ConfigurationService = configurationService;
            ErrorRepository = errorRepository;
            MessageBus = messageBus;
            FileService = fileService;
        }

        [HttpGet]
        public ActionResult CreateFolder(string FilePath)
        {
            FileBrowserPageModel model = new();

            if (string.IsNullOrWhiteSpace(FilePath))
            {
                FilePath = FileService.GetUserHome();
            }

            model.FilePath = FilePath;

            return View(model);
        }

        [HttpPost]
        public ActionResult CreateFolder(string FilePath, string FolderName)
        {
            DatabaseFile thisFile;

            using (IWriteContext context = DatabaseFileRepository.WriteContext())
            {
                thisFile = DatabaseFileRepository.GetByFullName(Path.Combine(FilePath, FolderName));

                thisFile = thisFile != null && !thisFile.IsDeleted
                    ? throw new UnauthorizedAccessException()
                    : new DatabaseFile
                    {
                        IsDirectory = true,
                        FileName = FolderName,
                        FilePath = FilePath
                    };

                DatabaseFileRepository.AddOrUpdate(thisFile);
            }

            return RedirectToAction(nameof(Index), new { Id = thisFile.FullName.Remove(FileService.GetUserFilesRoot()).Replace("\\", "/", StringComparison.OrdinalIgnoreCase) });
        }

        public ActionResult Delete(int Id)
        {
            using (IWriteContext context = DatabaseFileRepository.WriteContext())
            {
                DatabaseFile thisFile = DatabaseFileRepository.Find(Id) ?? throw new NullReferenceException($"Can not find file with id {Id}");

                if (!SecurityProvider.CheckAccess(thisFile, PermissionTypes.Write))
                {
                    throw new UnauthorizedAccessException();
                }

                DatabaseFileRepository.Delete(thisFile);
            }

            return RedirectToAction(nameof(Index));
        }

        public ActionResult Download(DatabaseFile thisFile)
        {
            if (thisFile is null)
            {
                throw new ArgumentNullException(nameof(thisFile));
            }

            if (!SecurityProvider.CheckAccess(thisFile, PermissionTypes.Read))
            {
                throw new UnauthorizedAccessException();
            }

            string Extension = thisFile.FileName.FromLast(".");
            string MimeType = MimeMappings.GetMimeType(Extension);

            return thisFile.Data.Length == 0
                ? MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                    ? Content(System.IO.File.ReadAllText(thisFile.FullName))
                    : File(System.IO.File.ReadAllBytes(thisFile.FullName), MimeType)
                : File(thisFile.Data, MimeType, thisFile.FileName);
        }

        public ActionResult Import(string FilePath = "", bool Public = false)
        {
            List<string> Files = Directory.EnumerateFiles(FilePath, "*", SearchOption.AllDirectories).ToList();
            List<string> output = new();

            foreach (string thisFile in Files)
            {
                if (DatabaseFileRepository.GetByFullName(thisFile) is null)
                {
                    output.Add($"Uploaded file {thisFile}");
                    _ = Upload(new List<FileUpload>() { new FileUpload(System.IO.File.ReadAllBytes(thisFile), thisFile) }, Public);
                }
                else
                {
                    output.Add($"{thisFile} already exists");
                }
            }

            return View("IndexOutput", output);
        }

        public override IActionResult Index(string Id = "")
        {
            return View(ModelForPath(Id));
        }

        [HttpGet]
        public ActionResult Upload(string FilePath = "")
        {
            FileBrowserPageModel model = new();

            if (string.IsNullOrWhiteSpace(FilePath))
            {
                FilePath = FileService.GetUserHome();
            }

            model.FilePath = FilePath;

            return View(model);
        }

        [HttpPost]
        public ActionResult Upload(List<IFormFile> upload, string FilePath = "", bool Public = false)
        {
            if (upload is null)
            {
                throw new ArgumentNullException(nameof(upload));
            }

            List<FileUpload> Files = new();

            foreach (IFormFile thisUpload in upload)
            {
                string uploadFileName = thisUpload.FileName.Replace("/", "\\", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(FilePath))
                {
                    FilePath = FileService.GetUserHome();
                }

                if (thisUpload != null)
                {
                    using BinaryReader binaryReader = new(thisUpload.OpenReadStream());

                    FileUpload newUpload = new(binaryReader.ReadBytes((int)thisUpload.Length), Path.Combine(FilePath, uploadFileName));

                    Files.Add(newUpload);
                }
            }

            _ = Upload(Files, Public);

            return RedirectToAction(nameof(Index), new { Id = FilePath.Remove(FileService.GetUserFilesRoot()).Replace("\\", "/", StringComparison.Ordinal).Trim('/') });
        }

        public ActionResult UploadAjax(List<IFormFile> upload, bool Public = true)
        {
            if (upload is null)
            {
                throw new ArgumentNullException(nameof(upload));
            }

            List<FileUpload> Files = new();

            foreach (IFormFile thisUpload in upload)
            {
                string uploadFileName = Path.Combine(Guid.NewGuid().ToString().Remove("-") + Path.GetExtension(thisUpload.FileName));

                using BinaryReader binaryReader = new(thisUpload.OpenReadStream());

                string FilePath = Path.Combine(FileService.GetUserFilesRoot(), "Ajax");

                if (thisUpload != null)
                {
                    FileUpload newUpload = new(binaryReader.ReadBytes((int)thisUpload.Length), Path.Combine(FilePath, uploadFileName));

                    Files.Add(newUpload);
                }
            }

            List<DatabaseFile> uploaded = Upload(Files, Public);

            return Json(new { files = uploaded.Select(d => new { Id = d._Id, d.ExternalId }) });
        }

        public ActionResult ViewByPath(string Path)
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                Path = "/";
            }

            if (Path.StartsWith('/'))
            {
                Path = Path[1..];
            }

            Path = Path.Replace("/", "\\", StringComparison.Ordinal);

            string FullName = System.IO.Path.Combine(FileService.GetUserFilesRoot(), Path ?? "");

            DatabaseFile thisFile = DatabaseFileRepository.GetByFullName(FullName);

            return thisFile is null || thisFile.IsDirectory ? View("Index", ModelForPath(FullName)) : Download(thisFile);
        }

        private FileBrowserPageModel ModelForPath(string FilePath)
        {
            FilePath = string.IsNullOrWhiteSpace(FilePath) ? FileService.GetUserHome() : Path.Combine(FileService.GetUserFilesRoot(), FilePath);
            FileBrowserPageModel model = new(DatabaseFileRepository.GetByPath(FilePath))
            {
                FilePath = FilePath
            };

            return model;
        }

        private List<DatabaseFile> Upload(List<FileUpload> upload, bool Public = false)
        {
            List<DatabaseFile> Imports = new();

            bool StoreFilesInDatabase = ConfigurationService.GetBool(ConfigurationNames.STORE_FILES_IN_DATABASE);

            List<DatabaseFile> Uploaded = new();

            using (IWriteContext context = DatabaseFileRepository.WriteContext())
            {
                //Prevent containing directories from being created multiple times
                List<DatabaseFile> CachedDirs = new();

                foreach (FileUpload thisUpload in upload)
                {
                    DirectoryInfo newFile = new(thisUpload.Path);

                    DatabaseFile thisFile = DatabaseFileRepository.GetByFullName(newFile.FullName);

                    if (thisFile != null && !thisFile.IsDeleted)
                    {
                        if (!SecurityProvider.CheckAccess(thisFile, PermissionTypes.Write))
                        {
                            throw new UnauthorizedAccessException();
                        }

                        if (System.IO.File.Exists(thisFile.FullName))
                        {
                            System.IO.File.Delete(thisFile.FullName);
                        }
                    }
                    else
                    {
                        thisFile = new DatabaseFile();
                    }

                    thisFile.Data = thisUpload.Data;
                    thisFile.FileName = newFile.Name;
                    thisFile.FilePath = newFile.Parent.FullName;

                    if (!StoreFilesInDatabase)
                    {
                        thisFile.Length = thisUpload.Data.Length;
                    }

                    if (Public)
                    {
                        SecurityProvider?.SetPublic(thisFile);
                    }

                    DatabaseFileRepository.AddOrUpdate(thisFile);

                    DirectoryInfo thisDir = new FileInfo(thisFile.FullName).Directory;
                    DirectoryInfo rootDir = new(FileService.GetUserFilesRoot());

                    DatabaseFile thisDataDir = DatabaseFileRepository.GetByFullName(thisDir.FullName) ?? CachedDirs.FirstOrDefault(d => d.FullName == thisDir.FullName);

                    while (thisDataDir is null && thisDir.FullName != rootDir.FullName)
                    {
                        thisDataDir = new DatabaseFile()
                        {
                            IsDirectory = true,
                            FileName = thisDir.Name,
                            FilePath = thisDir.Parent.FullName
                        };

                        DatabaseFileRepository.AddOrUpdate(thisDataDir);
                        CachedDirs.Add(thisDataDir);

                        thisDir = thisDir.Parent;
                        thisDataDir = DatabaseFileRepository.GetByFullName(thisDir.FullName) ?? CachedDirs.FirstOrDefault(d => d.FullName == thisDir.FullName);
                    }

                    Uploaded.Add(thisFile);
                }
            }

            return Uploaded;
        }
    }
}