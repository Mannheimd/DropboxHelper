using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Net.Http;
using System.Security.Cryptography;
using Dropbox.Api;
using Dropbox.Api.FileRequests;
using Dropbox.Api.Files;
using Dropbox.Api.Sharing;
using Microsoft.Win32;
using System.IO;

namespace DropboxHelper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            DoTheThing();
        }

        public async void DoTheThing()
        {
            await ChangeToFolder("");
        }

        private async Task ChangeToFolder(string path, bool recursive = false)
        {
            DropboxFolderContent.ItemsSource = await DropboxHandler.GetFolderContent(path, recursive);
            CurrentDirectoryPath_Label.Content = path;
        }

        private async Task GetFileRequestLink(FolderMetadata folder)
        {
            FileRequest fileRequest = await DropboxHandler.HandleCreateFileRequest(folder.PathLower, folder.Name, DateTime.Now + new TimeSpan(7, 0, 0, 0));

            if (fileRequest == null)
                return;

            RequestLink_TextBox.Text = fileRequest.Url;
        }

        #region UI Interaction

        private async void GetShareLink_Button_Click(object sender, RoutedEventArgs e)
        {
            FileShareLink_TextBox.Text = null;

            Metadata selectedItem = DropboxFolderContent.SelectedIndex > -1 ? ((Metadata)DropboxFolderContent.SelectedItem) : null;

            if (selectedItem == null)
                return;

            if (selectedItem.IsFolder)
            {
                MessageBox.Show("This is a folder. Please select a file to share.");
                return;
            }

            if (selectedItem.IsDeleted)
            {
                MessageBox.Show("This item has been deleted and cannot be shared. I don't even know why it's displaying here. I don't even know why I'm making this error, you should never see it... In fact it might be possible to share the file, I haven't checked. I'm just not going to let you try.");
                return;
            }

            SharedLinkMetadata share = await DropboxHandler.HandleShareFile(selectedItem, RequestedVisibility.Password.Instance, "password");

            if (share.Url != null)
            {
                FileShareLink_TextBox.Text = share.Url;
                try
                {
                    Clipboard.SetDataObject(share.Url);
                }
                catch (Exception error)
                {
                    MessageBox.Show("Unable to copy to clipboard. This can be caused by a an active WebEx session interfering with clipboard operations. Try again after closing your WebEx session.\n\n" + error.Message);
                }
            }
        }

        private async void DropboxFolderContent_ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Metadata selectedItem = DropboxFolderContent.SelectedIndex > -1 ? ((Metadata)DropboxFolderContent.SelectedItem) : null;

            if (selectedItem == null)
                return;

            if (selectedItem.IsFolder)
            {
                await ChangeToFolder(selectedItem.PathLower);
            }
        }

        private async void Up_Button_Click(object sender, RoutedEventArgs e)
        {
            string currentPath = CurrentDirectoryPath_Label.Content.ToString();

            if (!currentPath.Contains('/'))
                return;

            int slashPos = currentPath.LastIndexOf('/');
            
            string newPath = currentPath.Substring(0, slashPos);

            await ChangeToFolder(newPath);
        }

        private async void CreateFolder_Button_Click(object sender, RoutedEventArgs e)
        {
            string path = String.Format("/ExternalUpload/Cloud Data/{0} - {1}", CreateFolder_AccountName_TextBox.Text, CreateFolder_TicketNumber_TextBox.Text);
            FolderMetadata folder = await DropboxHandler.HandleCreateFolder(path);

            if (folder == null)
                return;

            await ChangeToFolder("/ExternalUpload/Cloud Data/");

            await GetFileRequestLink(folder);
        }

        private async void CreateRequest_Button_Click(object sender, RoutedEventArgs e)
        {
            Metadata selectedItem = DropboxFolderContent.SelectedIndex > -1 ? ((Metadata)DropboxFolderContent.SelectedItem) : null;

            if (selectedItem == null)
                return;

            if (selectedItem.IsFile)
            {
                MessageBox.Show("This is a file. Please select a folder to create an upload request.");
                return;
            }

            if (selectedItem.IsDeleted)
            {
                MessageBox.Show("This item has been deleted. I don't even know why it's displaying here. I don't even know why I'm making this error, you should never see it... In fact it might be possible to share the file, I haven't checked. I'm just not going to let you try.");
                return;
            }

            FolderMetadata folder = selectedItem.AsFolder;

            if (folder == null)
                return;

            await GetFileRequestLink(folder);
        }

        private async void DropboxFileUpload_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                foreach (string filePath in files)
                {
                    FileInfo fileInfo = new FileInfo(filePath);

                    if (!fileInfo.Exists)
                        continue;

                    DropboxUploadPendingItem pendingItem = new DropboxUploadPendingItem(filePath, CurrentDirectoryPath_Label.Content.ToString() + "/" + fileInfo.Name);
                }
            }
        }

        #endregion
    }

    public static class DropboxClientHandler
    {
        private static DropboxClient _client;
        public static DropboxClient client
        {
            get
            {
                if (_client == null)
                {
                    _client = SetupClient();
                }

                return _client;
            }
        }

        private static DropboxClient SetupClient()
        {
            string accessToken = DropboxAuth.GetAccessToken();

            try
            {
                DropboxClientConfig config = new DropboxClientConfig();
                config.HttpClient = new HttpClient();

                return new DropboxClient(accessToken, config);
            }
            catch (HttpException e)
            {
                string msg = "Exception reported from RPC layer";
                msg += string.Format("\n    Status code: {0}", e.StatusCode);
                msg += string.Format("\n    Message    : {0}", e.Message);
                if (e.RequestUri != null)
                {
                    msg += string.Format("\n    Request uri: {0}", e.RequestUri);
                }
                MessageBox.Show(msg);

                return null;
            }
        }
    }

    public class DropboxHandler
    {
        public static async Task<List<Metadata>> GetFolderContent(string path, bool recursive)
        {
            ListFolderResult result = await DropboxClientHandler.client.Files.ListFolderAsync(path, recursive);
            List<Metadata> list = result.Entries.ToList();

            while (result.HasMore)
            {
                result = await DropboxClientHandler.client.Files.ListFolderContinueAsync(result.Cursor);
                list.AddRange(result.Entries.ToList());
            }

            return list;
        }

        public static async Task<SharedLinkMetadata> HandleShareFile(Metadata file, RequestedVisibility requestedVisibility, string password = null, bool forceNewLink = false)
        {
            SharedLinkMetadata existingLink = await GetFileShareLink(file);
            if (existingLink != null)
            {
                if (forceNewLink)
                {
                    await RevokeFileShareLink(existingLink.Url);
                }
                else if (!IsMoreVisiblePermission(existingLink, requestedVisibility))
                {
                    return await ChangeShareLinkPermissions(existingLink.Url, requestedVisibility);
                }
                else return existingLink;
            }

            return await CreateFileShareLink(file, requestedVisibility, password);
        }

        /// <summary>
        /// Checks if link has an equal or more 'visible' permission than requested visibility
        /// TeamOnly < Password < Public
        /// </summary>
        /// <param name="link"></param>
        /// <param name="requestedVisibility"></param>
        /// <returns></returns>
        public static bool IsMoreVisiblePermission(SharedLinkMetadata link, RequestedVisibility requestedVisibility)
        {
            if (ConvertResolvedVisibilityToInt(link.LinkPermissions.ResolvedVisibility)
                >= ConvertRequestedVisibilityToInt(requestedVisibility))
                return true;
            else
                return false;
        }

        public static int ConvertResolvedVisibilityToInt(ResolvedVisibility visibility)
        {
            if (visibility.IsTeamOnly
                || visibility.IsTeamAndPassword
                || visibility.IsOther
                || visibility.IsSharedFolderOnly)
                return 0;
            else if (visibility.IsPassword)
                return 1;
            else if (visibility.IsPublic)
                return 2;
            else
                return 0;
        }

        public static int ConvertRequestedVisibilityToInt(RequestedVisibility visibility)
        {
            if (visibility.IsTeamOnly)
                return 0;
            else if (visibility.IsPassword)
                return 1;
            else if (visibility.IsPublic)
                return 2;
            else
                return 0;
        }

        public static async Task<SharedLinkMetadata> CreateFileShareLink(Metadata file, RequestedVisibility requestedVisibility, string password)
        {
            SharedLinkSettings settings = new SharedLinkSettings();
            if (requestedVisibility.IsPassword)
            {
                if (password == null)
                {
                    //TODO: Add error handling
                    return new SharedLinkMetadata();
                }
                settings = new SharedLinkSettings(requestedVisibility, password);
            }
            else
            {
                settings = new SharedLinkSettings(requestedVisibility);
            }
            
            CreateSharedLinkWithSettingsArg arg = new CreateSharedLinkWithSettingsArg(file.PathLower, settings);
            try
            {
                return await DropboxClientHandler.client.Sharing.CreateSharedLinkWithSettingsAsync(arg);
            }
            catch (ApiException<CreateSharedLinkWithSettingsError> error)
            {
                //TODO: Add error handling
                return new SharedLinkMetadata();
            }
        }

        public static async Task<SharedLinkMetadata> GetFileShareLink(Metadata file)
        {
            ListSharedLinksResult sharedLinks;
            try
            {
                sharedLinks = await DropboxClientHandler.client.Sharing.ListSharedLinksAsync(file.PathLower);
            }
            catch (ApiException<GetSharedLinkFileError> error)
            {
                //TODO: Add error handling
                return null;
            }

            if (sharedLinks.Links.Count < 1)
            {
                //TODO: Add error handling
                return null;
            }

            if (sharedLinks.Links[0] == null)
            {
                //TODO: Add error handling
                return null;
            }

            return sharedLinks.Links[0];
        }

        public static async Task RevokeFileShareLink(string url)
        {
            try
            {
                await DropboxClientHandler.client.Sharing.RevokeSharedLinkAsync(url);
            }
            catch (ApiException<RevokeSharedLinkError> error)
            {
                //TODO: Add error handling
            }
        }

        public static async Task<SharedLinkMetadata> GetShareLinkMetadata(string url, string password)
        {
            try
            {
                return await DropboxClientHandler.client.Sharing.GetSharedLinkMetadataAsync(url);
            }
            catch (ApiException<SharedLinkError> error)
            {
                //TODO: Add error handling
                return new SharedLinkMetadata();
            }
        }

        public static async Task<SharedLinkMetadata> ChangeShareLinkPermissions(string url, RequestedVisibility requestedVisibility)
        {
            SharedLinkSettings settings = new SharedLinkSettings(requestedVisibility);

            try
            {
                return await DropboxClientHandler.client.Sharing.ModifySharedLinkSettingsAsync(url, settings);
            }
            catch (ApiException<ModifySharedLinkSettingsError> error)
            {
                //TODO: Add error handling
                return new SharedLinkMetadata();
            }
        }

        public static async Task<FolderMetadata> HandleCreateFolder(string path)
        {
            FolderMetadata currentFolder = await GetFolder(path);
            if (currentFolder == null)
            {
                return await CreateFolder(path);
            }
            else
            {
                return currentFolder;
            }
        }

        public static async Task<FolderMetadata> GetFolder(string path)
        {
            Metadata metadata = new Metadata();

            try
            {
                metadata = await DropboxClientHandler.client.Files.GetMetadataAsync(path);
            }
            catch
            {
                //TODO: Add error handling
                return null;
            }

            if (metadata.IsFolder)
            {
                return metadata.AsFolder;
            }
            else
            {
                //TODO: Add error handling
                return null;
            }
        }

        public static async Task<FolderMetadata> CreateFolder(string path)
        {
            try
            {
                CreateFolderResult result = await DropboxClientHandler.client.Files.CreateFolderV2Async(path);
                return result.Metadata;
            }
            catch
            {
                //TODO: Add error handling
                return null;
            }
        }

        public static async Task<FileRequest> HandleCreateFileRequest(string path, string title, DateTime deadline)
        {
            FileRequest existingRequest = await GetFileRequest(path);

            if (existingRequest == null)
            {
                return await CreateFileRequest(path, title, deadline);
            }
            else
            {
                return await UpdateFileRequest(existingRequest, deadline);
            }
        }

        private static async Task<FileRequest> GetFileRequest(string path)
        {
            IList<FileRequest> requests;

            try
            {
                requests = (await DropboxClientHandler.client.FileRequests.ListAsync()).FileRequests;
            }
            catch
            {
                //TODO: Add error handling
                return null;
            }

            foreach (FileRequest request in requests)
            {
                if (request.Destination == path
                    && request.IsOpen)
                    return request;
            }

            return null;
        }

        private static async Task<FileRequest> CreateFileRequest(string path, string title, DateTime deadline)
        {
            try
            {
                return await DropboxClientHandler.client.FileRequests.CreateAsync(title, path, new FileRequestDeadline(deadline));
            }
            catch
            {
                //TODO: Add error handling
                return null;
            }
        }

        private static async Task<FileRequest> UpdateFileRequest(FileRequest fileRequest, DateTime? deadline = null, string path = null, bool? isOpen = null, string title = null)
        {
            UpdateFileRequestDeadline frDeadline = null;

            if (deadline != null)
            {
                frDeadline = new UpdateFileRequestDeadline.Update(new FileRequestDeadline((DateTime)deadline));
            }

            UpdateFileRequestArgs args = new UpdateFileRequestArgs(fileRequest.Id, title, path, frDeadline, isOpen);

            try
            {
                return await DropboxClientHandler.client.FileRequests.UpdateAsync(args);
            }
            catch
            {
                //TODO: Add error handling
                return null;
            }
        }
    }

    public class DropboxUploadPendingItem
    {
        public string localFilePath { get; set; }
        public string dropboxFilePath { get; set; }

        public DropboxUploadPendingItem(string localFilePath, string dropboxFilePath)
        {
            this.localFilePath = localFilePath;
            this.dropboxFilePath = dropboxFilePath;
        }
    }

    public static class DropboxUploader
    {
        private static List<DropboxUploadPendingItem> _fileUploadQueue = new List<DropboxUploadPendingItem>();
        public static List<DropboxUploadPendingItem> fileUploadQueue
        {
            get
            {
                return _fileUploadQueue;
            }
        }

        private const int chunkSize = 4096 * 1024;

        private static Task uploadTask = UploadAsync();

        public static void QueueFile(DropboxUploadPendingItem item)
        {
            _fileUploadQueue.Add(item);
            TriggerUploadTask();
        }

        public static void RemoveFile(DropboxUploadPendingItem item)
        {
            _fileUploadQueue.Remove(item);
            TriggerUploadTask();
        }

        private static void TriggerUploadTask()
        {
            if (uploadTask.Status == TaskStatus.WaitingForChildrenToComplete)
            {
                uploadTask.Wait();
                uploadTask.Start();
            }
            else if (uploadTask.Status != TaskStatus.Running
                && uploadTask.Status != TaskStatus.WaitingToRun)
            {
                uploadTask.Start();
            }
        }

        private async static Task UploadAsync()
        {
            while(_fileUploadQueue.Count != 0)
            {
                DropboxUploadPendingItem file = fileUploadQueue[0];

                await UploadFile(file.localFilePath, file.dropboxFilePath);

                _fileUploadQueue.Remove(file);
            }
        }

        private static async Task UploadFile(string localFilePath, string dropboxFilePath)
        {
            // Shamelessly acquired from https://stackoverflow.com/questions/40970095/file-uploading-dropbox-v2-0-api

            using (var fileStream = File.Open(localFilePath, FileMode.Open))
            {
                if (fileStream.Length <= chunkSize)
                {
                    await DropboxClientHandler.client.Files.UploadAsync(dropboxFilePath, body: fileStream);
                }
                else
                {
                    await ChunkUploadFile(dropboxFilePath, fileStream);
                }
            }
        }

        private static async Task ChunkUploadFile(string dropboxFilePath, FileStream stream)
        {
            // Shamelessly acquired from https://stackoverflow.com/questions/40970095/file-uploading-dropbox-v2-0-api

            ulong numChunks = (ulong)Math.Ceiling((double)stream.Length / chunkSize);
            byte[] buffer = new byte[chunkSize];
            string sessionId = null;
            for (ulong idx = 0; idx < numChunks; idx++)
            {
                var byteRead = stream.Read(buffer, 0, chunkSize);

                using (var memStream = new MemoryStream(buffer, 0, byteRead))
                {
                    if (idx == 0)
                    {
                        var result = await DropboxClientHandler.client.Files.UploadSessionStartAsync(false, memStream);
                        sessionId = result.SessionId;
                    }
                    else
                    {
                        var cursor = new UploadSessionCursor(sessionId, chunkSize * idx);

                        if (idx == numChunks - 1)
                        {
                            FileMetadata fileMetadata = await DropboxClientHandler.client.Files.UploadSessionFinishAsync(cursor, new CommitInfo(dropboxFilePath), memStream);
                            Console.WriteLine(fileMetadata.PathDisplay);
                        }
                        else
                        {
                            await DropboxClientHandler.client.Files.UploadSessionAppendV2Async(cursor, false, memStream);
                        }
                    }
                }
            }
        }
    }

    public class DropboxAuth
    {
        private readonly static byte[] additionalEntropy = { 7, 2, 6, 5 ,9 }; // Used to further encrypt authentication information, changing this will cause any currently stored login details on the client machine to be invalid
        private static string accessToken;
        private static string oAuth2State;
        private static string appKey = "5xpffww5qkvm3hu";
        private static Uri redirectUri = new Uri("https://localhost/authorise");

        public static string GetAccessToken()
        {
            if (UnsecureCreds("APCDropbox") != null)
            {
                return Encoding.UTF8.GetString(UnsecureCreds("APCDropbox"));
            }
            else
            {
                AcquireNewOAuthToken();
                return accessToken;
            }
        }

        private static void StoreAccessToken()
        {
            SecureCreds(accessToken, "APCDropbox");
        }

        private static void AcquireNewOAuthToken()
        {
            oAuth2State = Guid.NewGuid().ToString("N");
            Uri authorizeUri = DropboxOAuth2Helper.GetAuthorizeUri(OAuthResponseType.Token, appKey, redirectUri, state: oAuth2State);
            BrowserWindow browser = new BrowserWindow(authorizeUri, redirectUri, oAuth2State);
            browser.ShowDialog();
            while (!browser.haveResult)
            {
                if (!browser.IsActive)
                    return;
            }
            accessToken = browser.accessToken;
            StoreAccessToken();
        }

        /// <summary>
        /// Secures the user's credentials against the Windows user profile and stores them in the registry under HKCU
        /// </summary>
        /// <param name="apiToken">API token to be stored</param>
        /// <param name="id">Name for the key</param>
        private static void SecureCreds(string accessToken, string id)
        {
            byte[] utf8Creds = Encoding.UTF8.GetBytes(accessToken);

            byte[] securedCreds = null;

            // Encrypt credentials
            try
            {
                securedCreds = ProtectedData.Protect(utf8Creds, additionalEntropy, DataProtectionScope.CurrentUser);

                // Check if registry path exists
                if (CheckOrCreateRegPath())
                {
                    // Save encrypted key to registry
                    RegistryKey credsKey = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\Dropbox Logins", true);
                    credsKey.SetValue(id, securedCreds);
                }
            }
            catch (CryptographicException error)
            {
                //TODO: MessageHandler.handleMessage(false, 3, error, "Encrypting Jenkins login credentials");
            }
        }

        /// <summary>
        /// Pulls stored credentials from registry and decrypts them
        /// </summary>
        /// <param name="id">Name for the key</param>
        /// <returns>Returns unsecured utf8-encrypted byte array representing stored credentials</returns>
        private static byte[] UnsecureCreds(string id)
        {
            // Check if registry path exists
            if (CheckOrCreateRegPath())
            {
                byte[] securedCreds = null;
                byte[] utf8Creds = null;

                // Get encrypted key from registry
                try
                {
                    RegistryKey credsKey = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\Dropbox Logins", false);
                    securedCreds = (byte[])credsKey.GetValue(id);

                    // Un-encrypt credentials
                    try
                    {
                        utf8Creds = ProtectedData.Unprotect(securedCreds, additionalEntropy, DataProtectionScope.CurrentUser);
                    }
                    catch (CryptographicException error)
                    {
                        //TODO: MessageHandler.handleMessage(false, 3, error, "Decrypting stored Jenkins login credentials"); ;
                    }
                }
                catch (Exception error)
                {
                    //TODO: MessageHandler.handleMessage(false, 3, error, "Locating reg key to get Jenkins credentials");
                }

                return utf8Creds;
            }
            return null;
        }

        /// <summary>
        /// Verifies that the registry key to store credentials exists, and creates it if not
        /// </summary>
        /// <returns>true if key is now created and valid, false if not</returns>
        private static bool CheckOrCreateRegPath()
        {
            //TODO: MessageHandler.handleMessage(false, 6, null, "Verifying Jenkins Login registry key path");
            RegistryKey key = null;

            // Check if subkey "HKCU\Software\Swiftpage Support" exists
            key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support", false);
            if (key == null)
            {
                //TODO: MessageHandler.handleMessage(false, 5, null, @"Creating registry key 'HKCU\Software\Swiftpage Support'");

                try
                {
                    key = Registry.CurrentUser.OpenSubKey(@"Software", true);
                    key.CreateSubKey("Swiftpage Support");
                }
                catch (Exception error)
                {
                    //TODO: MessageHandler.handleMessage(false, 3, error, @"Attempting to create registry key 'HKCU\Software\Swiftpage Support'");
                    return false;
                }
            }

            // Check if subkey HKCU\Software\Swiftpage Support\Dropbox Logins exists
            key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\Dropbox Logins", false);
            if (key == null)
            {
                //TODO: MessageHandler.handleMessage(false, 5, null, @"Creating registry key 'HKCU\Software\Swiftpage Support\Dropbox Logins'");

                try
                {
                    key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support", true);
                    key.CreateSubKey("Dropbox Logins");
                }
                catch (Exception error)
                {
                    //TODO: MessageHandler.handleMessage(false, 3, error, @"Attempting to create registry key 'HKCU\Software\Swiftpage Support\Dropbox Logins'");
                    return false;
                }
            }

            // Confirm that full subkey exists
            key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\Dropbox Logins", false);
            if (key != null)
            {
                //TODO: MessageHandler.handleMessage(false, 6, null, "Login reg key exists");
                return true;
            }
            else
            {
                //TODO: MessageHandler.handleMessage(false, 3, null, @"Testing access to key HKCU\Swiftpage Support\Dropbox Logins");
                return false;
            }
        }
    }

    #region Converters

    public class BoolToFileFolder_Converter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool && (bool)value == true)
            {
                return "File";
            }
            else if (value is bool && (bool)value == false)
            {
                return "Folder";
            }

            return "Unrecognised";
        }

        public object ConvertBack(object value, Type targetType, object Parameter, CultureInfo culture)
        {
            throw new Exception("This method is not implemented.");
        }
    }

    #endregion
}
