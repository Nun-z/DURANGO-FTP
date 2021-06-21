﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers.Provider;
using Windows.UI.Xaml;
using Zhaobang.FtpServer.File;

namespace UniversalFtpServer
{
    class UwpFileProvider : IFileProvider
    {
        string rootFolder;
        string workFolder = string.Empty;

        public UwpFileProvider(string rootFolder)
        {
            this.rootFolder = rootFolder;
        }

        public async Task CreateDirectoryAsync(string path)
        {
            string fullPath = GetLocalVfsPath(path);
            await RecursivelyCreateDirectoryAsync(fullPath);
            //await RecursivelyCreateDirectoryAsync(parentPath);
        }

        public async Task<Stream> CreateFileForWriteAsync(string path)
        {
            path = GetLocalVfsPath(path);
            string parentPath = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            var itemexists = ItemExists(parentPath);
            bool parentpathexists = itemexists;
            if (!parentpathexists)
            {
                await RecursivelyCreateDirectoryAsync(parentPath);
            }
            StorageFolder parent = await StorageFolder.GetFolderFromPathAsync(parentPath);
            StorageFile file = await parent.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
            return await file.OpenStreamForWriteAsync();
        }

        public async Task RecursivelyCreateDirectoryAsync(string path)
        {
            string parentPath = System.IO.Directory.GetParent(path).ToString();
            var itemexists = ItemExists(parentPath);
            if (!itemexists) 
            { 
                await RecursivelyCreateDirectoryAsync(parentPath);
            }
            await Task.Run(() => {PinvokeFilesystem.CreateDirectoryFromApp((@"\\?\" + path), IntPtr.Zero);});
        }


        public bool ItemExists(string path) 
        {
            PinvokeFilesystem.GetFileAttributesExFromApp((@"\\?\" + path), PinvokeFilesystem.GET_FILEEX_INFO_LEVELS.GetFileExInfoStandard, out var lpFileInfo);
            if (lpFileInfo.dwFileAttributes != 0)
            {
                return true;
            } else {
                return false;
            }
        }

        public async Task DeleteAsync(string path)
        {
            path = GetLocalVfsPath(path);
            PinvokeFilesystem.GetFileAttributesExFromApp((@"\\?\" + path), PinvokeFilesystem.GET_FILEEX_INFO_LEVELS.GetFileExInfoStandard, out var lpFileInfo);
            //this handling is only neccessary as winscp for some reason sends directory delete commands as file delete commands
            if (lpFileInfo.dwFileAttributes.HasFlag(System.IO.FileAttributes.Directory))
            {
                await RecursivelyDeleteDirectoryAsync(path);
            }
            else if (lpFileInfo.dwFileAttributes != 0)
            {
                await Task.Run(() => { PinvokeFilesystem.DeleteFileFromApp(@"\\?\" + path); });
            } else {
                throw new FileBusyException("Items of unknown type can't be deleted");
            }
        }

        public async Task DeleteDirectoryAsync(string path)
        {
            path = GetLocalVfsPath(path);
            await RecursivelyDeleteDirectoryAsync(path);
        }

        public async Task RecursivelyDeleteDirectoryAsync(string path) 
        {
            List<MonitoredFolderItem> mininfo = PinvokeFilesystem.GetMinInfo(path);
            foreach (MonitoredFolderItem item in mininfo) 
            {
                string itempath = System.IO.Path.Combine(item.ParentFolderPath, item.Name);
                if (item.attributes.HasFlag(System.IO.FileAttributes.Directory))
                {
                    await RecursivelyDeleteDirectoryAsync(itempath);
                } else {
                    await Task.Run(()=> { PinvokeFilesystem.DeleteFileFromApp(@"\\?\" + itempath); });
                }
            }
            await Task.Run(() => { PinvokeFilesystem.RemoveDirectoryFromApp(@"\\?\" + path); });
        }

        public async Task<IEnumerable<FileSystemEntry>> GetListingAsync(string path)
        {
            if (path == "-a" || path == "-al") 
            {
                path = "";
            }
            string fullPath = GetLocalPath(path);
            //SetWorkingDirectory(fullPath);
            string[] splitpath = fullPath.Split("VFSROOT");

            //create empty list of filesystem entries
            List<FileSystemEntry> result = new List<FileSystemEntry>();

            //check if the current path is the root of the virtual filesystem
            // the \\- check is to make sure that it is not a sub command being entered
            if (splitpath[1] == "" || splitpath[1].StartsWith("\\-"))
            {
                //if in this if statement then the check returned true
                //get all drives
                List<string> drives = PinvokeFilesystem.GetDrives();
                //loop through all the drives
                foreach (string drive in drives)
                {
                    //create an entry for the current drive
                    FileSystemEntry entry = new FileSystemEntry()
                    {
                        IsDirectory = true,
                        IsReadOnly = true,
                        LastWriteTime = DateTime.Now,
                        Length = 0,
                        Name = drive
                    };
                    //add the entry
                    result.Add(entry);
                }

                //add entry for the so called Local folder - used to access apps local data
                FileSystemEntry localentry = new FileSystemEntry()
                {
                    IsDirectory = true,
                    IsReadOnly = true,
                    LastWriteTime = DateTime.Now,
                    Length = 0,
                    Name = "LOCALFOLDER"
                };
                //add the entry that was just created

                result.Add(localentry);
            } else {
                string reformatedpath = GetLocalVfsPath(path);
                List<MonitoredFolderItem> monfiles = PinvokeFilesystem.GetItems(reformatedpath);
                foreach (var item in monfiles)
                {
                    FileSystemEntry entry = new FileSystemEntry()
                    {
                        IsDirectory = item.IsDir,
                        IsReadOnly = item.attributes.HasFlag(System.IO.FileAttributes.ReadOnly),
                        LastWriteTime = item.DateModified.ToUniversalTime().DateTime,
                        Length = (long)item.Size,
                        Name = item.Name
                    };
                    result.Add(entry);
                }   
            }

            //return list of entrys
            return result;
        }

        public Task<IEnumerable<string>> GetNameListingAsync(string path)
        {
            path = GetLocalVfsPath(path);
            List<string> result = new List<string>();
            List<MonitoredFolderItem> monfiles = PinvokeFilesystem.GetNames(path);
            foreach (var item in monfiles)
            {
                result.Add(item.Name);
            }
            return Task.FromResult(result.AsEnumerable());
        }

        public string GetWorkingDirectory()
        {
            return "/" + workFolder;
        }

        public async Task<Stream> OpenFileForReadAsync(string path)
        {
            path = GetLocalVfsPath(path);
            var file = await StorageFile.GetFileFromPathAsync(path);
            return await file.OpenStreamForReadAsync();
        }

        public async Task<Stream> OpenFileForWriteAsync(string path)
        {
            path = GetLocalVfsPath(path);
            var file = await StorageFile.GetFileFromPathAsync(path);
            return await file.OpenStreamForWriteAsync();
        }

        public async Task RenameAsync(string fromPath, string toPath)
        {
            //get full path from parameter "fromPath"
            fromPath = GetLocalVfsPath(fromPath);
            toPath = GetLocalVfsPath(toPath);

            IStorageItem item = null;
            try
            {
                item = await StorageFile.GetFileFromPathAsync(fromPath);
                goto rename;
            }
            catch { }
            try
            {
                item = await StorageFolder.GetFolderFromPathAsync(fromPath);
                goto rename;
            }
            catch { }
            if (item == null)
            {
                throw new FileNoAccessException("Can't find the item to rename");
            }

            rename:
            if (Path.GetDirectoryName(fromPath) != Path.GetDirectoryName(toPath))
            {
                string toFullPathParent = Path.GetDirectoryName(toPath);
                StorageFolder destinationFolder = await StorageFolder.GetFolderFromPathAsync(toFullPathParent);
                if (item is IStorageFile file)
                {
                    await file.MoveAsync(destinationFolder, Path.GetFileName(toPath));
                }
                else if (item is IStorageFolder folder)
                {
                    if (!(await MoveFolder(folder, destinationFolder)))
                        throw new FileBusyException("Some items can't be moved");
                }
                else
                {
                    throw new FileBusyException("Items of unknown type can't be moved");
                }
            }
            else
            {
                await item.RenameAsync(Path.GetFileName(toPath));
            }
        }

        public async Task RenameAsync2(string fromPath, string toPath)
        {
            //get full path from parameter "fromPath"
            fromPath = GetLocalVfsPath(fromPath);
            toPath = GetLocalVfsPath(toPath);

            IStorageItem item = null;
            try
            {
                item = await StorageFile.GetFileFromPathAsync(fromPath);
                goto rename;
            }
            catch { }
            try
            {
                item = await StorageFolder.GetFolderFromPathAsync(fromPath);
                goto rename;
            }
            catch { }
            if (item == null)
            {
                throw new FileNoAccessException("Can't find the item to rename");
            }

        rename:
            if (Path.GetDirectoryName(fromPath) != Path.GetDirectoryName(toPath))
            {
                string toFullPathParent = Path.GetDirectoryName(toPath);
                StorageFolder destinationFolder = await StorageFolder.GetFolderFromPathAsync(toFullPathParent);
                if (item is IStorageFile file)
                {
                    await file.MoveAsync(destinationFolder, Path.GetFileName(toPath));
                }
                else if (item is IStorageFolder folder)
                {
                    if (!(await MoveFolder(folder, destinationFolder)))
                        throw new FileBusyException("Some items can't be moved");
                }
                else
                {
                    throw new FileBusyException("Items of unknown type can't be moved");
                }
            }
            else
            {
                await item.RenameAsync(Path.GetFileName(toPath));
            }
        }

        private async Task<bool> MoveFolder(IStorageFolder folder, IStorageFolder destination)
        {
            foreach (var file in await folder.GetFilesAsync())
            {
                await file.MoveAsync(destination);
            }
            foreach (var subFolder in await folder.GetFoldersAsync())
            {
                var destSubFolder = await destination.CreateFolderAsync(subFolder.Name);
                await MoveFolder(subFolder, destSubFolder);
            }
            if (!(await folder.GetItemsAsync()).Any())
            {
                await folder.DeleteAsync();
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool SetWorkingDirectory(string path)
        {
            try
            {
                string localPath = GetLocalPath(path);
                workFolder = GetFtpPath(localPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string GetLocalVfsPath(string path)
        {
            //get full path from parameter "toPath"
            string toFullPath = GetLocalPath(path);
            //split from path based on vfsroot, overwrite old split path variable as it won't be referenced again and I don't want to assign another variable
            string[] splitpath = toFullPath.Split("VFSROOT");
            //replace from full path with corrected string and trim the start of it
            toFullPath = splitpath[1].TrimStart('/', '\\');
            //trim end of string
            toFullPath = toFullPath.TrimEnd('/', '\\');
            if (!toFullPath.Contains("LOCALFOLDER"))
            {
                //add the colon for file access
                toFullPath = toFullPath.Insert(1, ":");
            }
            else
            {
                //split the path into each folder
                string[] localpath = toFullPath.Split("\\");
                //get the parent of the apps local appdata folder
                var Localfoldertask = ApplicationData.Current.LocalFolder.GetParentAsync();
                Localfoldertask.AsTask().Wait();
                var localfolderresult = Localfoldertask.GetResults();
                //get the parent of that folder
                Localfoldertask = localfolderresult.GetParentAsync();
                Localfoldertask.AsTask().Wait();
                //get the path value of that folder as a string and set the base directory of the input path to be the local app data folder
                localpath[0] = Localfoldertask.GetResults().Path;
                //rejoin the parts of the path together
                toFullPath = String.Join("\\", localpath);
            }
            //retrim end of string, just as a precautionary measure
            //I tried implementing this as a trim end but it did not seem to work
            if (toFullPath.EndsWith("\\"))
            {
                toFullPath = toFullPath.Substring(0, toFullPath.Length - "\\".Length);
            }

            return toFullPath;
        }

        /// <exception cref="ArgumentException"/>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="UnauthorizedAccessException"/>
        /// <exception cref="SecurityException"/>
        /// <exception cref="NotSupportedException"/>
        /// <exception cref="PathTooLongException"/>
        private string GetLocalPath(string path)
        {
            string fullPath = Path.Combine(workFolder, path).TrimStart('/', '\\');
            string localPath = Path.GetFullPath(Path.Combine(rootFolder, fullPath)).TrimEnd('/', '\\');
            string baseLocalPath = Path.GetFullPath(rootFolder).TrimEnd('/', '\\');
            if (!Path.GetFullPath(localPath).Contains(baseLocalPath))
                throw new UnauthorizedAccessException("User tried to access out of base directory");
            return localPath;
        }

        private string GetFtpPath(string localPath)
        {
            string localFullPath = Path.GetFullPath(localPath).TrimEnd('/', '\\');
            string baseFullPath = Path.GetFullPath(rootFolder).TrimEnd('/', '\\');
            if (!localFullPath.Contains(baseFullPath))
            {
                throw new UnauthorizedAccessException("User tried to access out of base directory");
            }
            return localFullPath.Replace(baseFullPath, string.Empty).TrimStart('/', '\\').Replace('\\', '/');
        }
    }
}
